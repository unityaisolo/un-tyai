import { Router, type Request, type Response } from "express";
import { z } from "zod";
import type { ChatMessage, ToolCall } from "../providers/types.js";
import { TOOLS } from "../tools.js";
import { recordUsage, estimateUsd } from "../billing/metering.js";
import { resolveTarget } from "../lib/target.js";
import { gate, charge, budgetUsd } from "../lib/gate.js";

export const chatRouter = Router();

const BodySchema = z.object({
  model: z.string().default("nova-flash"),
  messages: z.array(
    z.object({
      role: z.enum(["system", "user", "assistant", "tool"]),
      content: z.string(),
      toolCallId: z.string().optional(),
      images: z.array(z.string()).optional(), // base64 PNG (kullanıcının eklediği görseller)
      toolCalls: z
        .array(z.object({ id: z.string(), name: z.string(), args: z.record(z.unknown()) }))
        .optional(),
    }),
  ),
  toolNames: z.array(z.string()).optional(),
  council: z.boolean().optional(),
  auditorModel: z.string().optional(),
});

/**
 * Geçmişi sağlayıcıların kabul edeceği hale getirir:
 *  - Bir 'tool' mesajı YALNIZCA kendisinden önce gelen ve aynı id'yi içeren
 *    assistant.toolCalls varsa geçerlidir. Sahipsiz olanlar atılır.
 *  - toolCallId'i olmayan 'tool' mesajları atılır.
 * Aksi halde OpenAI/Groq 400 verir ve sohbet kalıcı olarak kilitlenir.
 */
function sanitizeHistory(msgs: ChatMessage[]): ChatMessage[] {
  const known = new Set<string>();
  const out: ChatMessage[] = [];
  let dropped = 0;
  for (const m of msgs) {
    if (m.role === "assistant" && m.toolCalls?.length)
      for (const tc of m.toolCalls) if (tc?.id) known.add(tc.id);

    if (m.role === "tool") {
      if (!m.toolCallId || !known.has(m.toolCallId)) { dropped++; continue; }
    }
    out.push(m);
  }
  // Sahipsiz tool_call bırakan son assistant mesajı da sorun çıkarır: cevapsız kalan
  // tool çağrılarını temizle (metin varsa mesaj korunur, yoksa atılır).
  const answered = new Set(out.filter((m) => m.role === "tool" && m.toolCallId).map((m) => m.toolCallId!));
  const fixed: ChatMessage[] = [];
  for (const m of out) {
    if (m.role === "assistant" && m.toolCalls?.length) {
      const pending = m.toolCalls.filter((tc) => !answered.has(tc.id));
      if (pending.length > 0) {
        dropped += pending.length;
        const kept = m.toolCalls.filter((tc) => answered.has(tc.id));
        if (kept.length === 0 && !m.content?.trim()) continue; // tamamen boş → at
        fixed.push({ ...m, toolCalls: kept.length > 0 ? kept : undefined });
        continue;
      }
    }
    fixed.push(m);
  }
  if (dropped > 0) console.warn(`[chat] geçmiş onarıldı: ${dropped} sahipsiz tool kaydı temizlendi`);
  return fixed;
}

chatRouter.post("/chat", async (req: Request, res: Response) => {
  const parsed = BodySchema.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { model: requestedModel, toolNames, council } = parsed.data;
  // SAĞLAMLIK: geçmiş bozuk gelirse (ör. Unity derleme araya girip tool sonucu yarıda
  // kalırsa) sağlayıcı "messages.0.tool_call_id gerekli" gibi 400 döner ve sohbet
  // TAMAMEN kilitlenir. Sahipsiz 'tool' mesajlarını burada temizliyoruz.
  const messages = sanitizeHistory(parsed.data.messages as ChatMessage[]);

  // sanitizeHistory HER ŞEYİ eleyebilir (ör. geçmiş tamamen sahipsiz 'tool' mesajıysa).
  // O durumda sağlayıcıya boş dizi gidiyor ve kullanıcı anlaşılmaz bir sağlayıcı hatası
  // görüyordu: "groq 400: messages : minimum number of items is 1". Sahada bu çıktı.
  // Boş isteği hiç göndermeyip anlaşılır bir mesaj dönüyoruz.
  if (messages.length === 0) {
    res.status(400).json({ error: "Gönderilecek mesaj yok. Bir şeyler yazıp tekrar dene." });
    return;
  }

  const tools = toolNames ? TOOLS.filter((t) => toolNames.includes(t.name)) : TOOLS;

  // ÖNEMLİ: kapı SSE başlıklarından ÖNCE. writeHead sonrası status/json yazılamaz.
  if (!(await gate(req, res, "chat")).ok) return;

  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });
  const send = (obj: unknown) => res.write(`data: ${JSON.stringify(obj)}\n\n`);

  let inTok = 0, outTok = 0;
  try {
    const t = resolveTarget(req.userId, "brain", requestedModel);
    if (!t.ok) { send({ type: "error", message: t.error }); return res.end(); }
    const { provider, model, apiKey, pooled, baseUrl } = t.target;
    console.log(`[chat] model=${requestedModel}→${model} provider=${provider.id} pooled=${pooled}`);

    // Bir beyin turu çalıştırır. Council açıkken tool_call'lar tamponlanır (satır içi gönderilmez).
    // Bu isteğin harcayabileceği üst sınır. Yerel modda veya kullanıcının kendi
    // anahtarında sonsuz döner, yani akış hiç kesilmez.
    const maxUsd = await budgetUsd(req.userId, pooled);
    let budgetExceeded = false;

    const runBrain = async (msgs: ChatMessage[]) => {
      let text = "";
      const toolCalls: ToolCall[] = [];
      // Modelin <think> bloklarını cevaptan ayır: düşünce "reasoning" olarak canlı akar,
      // ama sohbet geçmişine ve nihai cevaba KARIŞMAZ.
      const split = createThinkSplitter(
        (s) => send({ type: "reasoning", text: s }),
        (s) => { text += s; send({ type: "token", text: s }); },
      );
      for await (const ev of provider.chat({ model, messages: msgs, tools, apiKey, baseUrl })) {
        if (ev.type === "token") { split.push(ev.text); }
        else if (ev.type === "tool_call") { toolCalls.push({ id: ev.id, name: ev.name, args: ev.args }); if (!council) send(ev); }
        else if (ev.type === "usage") {
          inTok += ev.inputTokens; outTok += ev.outputTokens; send(ev);
          // AKIŞ ORTASINDA BÜTÇE KONTROLÜ.
          // Kapı yalnızca istek BAŞINDA bakiyeye bakıyordu; 1 kredisi olan kullanıcı
          // uzun bir akış başlatıp bakiyesinin kat kat üstünde harcayabiliyordu.
          // Bütçe aşılırsa akışı burada kesiyoruz — harcanan kadarı yine faturalanır.
          if (estimateUsd(model, inTok, outTok, pooled) > maxUsd) {
            send({ type: "error", message: "Kredi sınırına ulaşıldı — yanıt burada kesildi." });
            budgetExceeded = true;
            break;
          }
        }
        else if (ev.type === "error") { send(ev); throw new Error(ev.message); }
        else if (ev.type === "done") break;
      }
      split.flush();
      return { text: text.trim(), toolCalls };
    };

    // ---- GÖRSEL ADIMI ----
    // Beyin (araç çağırabilen model) her zaman görsel anlamayabilir. Bu yüzden görselleri
    // ÖNCE vision modeline okutup metne çeviriyoruz; beyin metni görür, araçları bozulmaz.
    const msgs = messages as ChatMessage[];
    for (const m of msgs) {
      if (m.role !== "user" || !m.images?.length) continue;
      send({ type: "vision", text: `🖼 ${m.images.length} görsel inceleniyor...` });
      const desc = await describeImages(req.userId, m.images, m.content);
      m.content = `${m.content}\n\n[Kullanıcının eklediği görselin içeriği]\n${desc}`;
      delete m.images;
      send({ type: "vision", text: `🖼 Görsel okundu.` });
    }

    let brain = await runBrain(msgs);
    if (!brain.text && brain.toolCalls.length === 0) {
      console.warn(`[chat] BOŞ yanıt: model=${model} provider=${provider.id} — sağlayıcı içerik akıtmadı.`);
      send({ type: "token", text: "(Model boş yanıt döndürdü — backend terminalindeki [chat] satırını kontrol et. Model emekliye ayrılmış olabilir; .env'e NOVA_FLASH=<güncel-groq-modeli> yazarak değiştirebilirsin.)" });
    }

    // Bütçe kesildiyse ikinci tur (denetçi + yeniden üretim) YAPILMAZ; aksi halde
    // kesme işe yaramaz, model ikinci kez çalışıp harcamaya devam ederdi.
    if (council && !budgetExceeded && brain.toolCalls.length > 0) {
      const userText = [...messages].reverse().find((m) => m.role === "user")?.content ?? "";
      const review = await reviewProposal(req.userId, userText, brain.toolCalls, model);
      send({ type: "council", verdict: review.verdict, notes: review.notes });

      if (review.verdict === "revise") {
        const revised: ChatMessage[] = [
          ...(messages as ChatMessage[]),
          { role: "assistant", content: brain.text, toolCalls: brain.toolCalls },
          { role: "user", content: `Denetçi geri bildirimi: ${review.notes}\nLütfen buna göre düzelt ve araçları yeniden öner.` },
        ];
        brain = await runBrain(revised);
        send({ type: "council", verdict: "final", notes: "Düzeltildi." });
      }
      for (const tc of brain.toolCalls) send({ type: "tool_call", id: tc.id, name: tc.name, args: tc.args });
    }

    send({ type: "done" });
    const usage = recordUsage({ userId: req.userId, model, inputTokens: inTok, outputTokens: outTok, pooled });
    await charge(req.userId, usage.totalUsd, pooled);
    send({ type: "billing", ...usage });
  } catch (err) {
    send({ type: "error", message: err instanceof Error ? err.message : String(err) });
  } finally {
    res.end();
  }
});

/**
 * Görselleri vision modeline okutup METNE çevirir.
 * Böylece araç çağırabilen ana beyin, görsel desteği olmasa bile içeriği "görür".
 */
async function describeImages(userId: string, images: string[], userText: string): Promise<string> {
  try {
    const t = resolveTarget(userId, "vision");
    if (!t.ok) return "(görsel okunamadı: " + t.error + ")";
    const { provider, model, apiKey, baseUrl } = t.target;

    const msgs: ChatMessage[] = [
      {
        role: "system",
        content:
          "Sen bir Unity geliştiricisinin ekranını inceleyen görsel analistsin. Görseli TARAFSIZ ve " +
          "SOMUT betimle: görünen nesneler, malzeme/renk sorunları, ölçek tutarsızlıkları, hatalı " +
          "yerleşimler; ekranda yazı varsa (konsol hatası, Inspector alanı, dosya adı) OKU ve aynen aktar. " +
          "KURAL: sadece NET GÖRDÜĞÜNÜ yaz. Bulanık/okunamayan yeri 'okunamıyor' diye belirt. " +
          "Dosya adı, script adı, sürüm numarası, hata metni UYDURMA — emin değilsen yazma. " +
          "Yorum katma, gördüğünü yaz. En fazla 200 kelime.",
      },
      { role: "user", content: userText || "Bu görselde ne var?", images },
    ];

    let out = "";
    for await (const ev of provider.chat({ model, messages: msgs, tools: [], apiKey, baseUrl })) {
      if (ev.type === "token") out += ev.text;
      else if (ev.type === "error") return `(görsel okunamadı: ${ev.message})`;
      else if (ev.type === "done") break;
    }
    return out.replace(/<think>[\s\S]*?<\/think>/g, "").trim() || "(görsel boş yorumlandı)";
  } catch (e) {
    return `(görsel okunamadı: ${e instanceof Error ? e.message : String(e)})`;
  }
}

/**
 * Akış içindeki <think>...</think> bloklarını cevaptan ayırır.
 * Etiketler parça sınırına denk gelebileceği için sondaki olası yarım etiket tamponlanır.
 */
function createThinkSplitter(onReasoning: (s: string) => void, onText: (s: string) => void) {
  const OPEN = "<think>", CLOSE = "</think>";
  const maxTag = Math.max(OPEN.length, CLOSE.length);
  let buf = "";
  let inThink = false;

  const drain = (flush: boolean) => {
    for (;;) {
      const tag = inThink ? CLOSE : OPEN;
      const i = buf.indexOf(tag);
      if (i >= 0) {
        const chunk = buf.slice(0, i);
        if (chunk) (inThink ? onReasoning : onText)(chunk);
        buf = buf.slice(i + tag.length);
        inThink = !inThink;
        continue;
      }
      const keep = flush ? 0 : Math.min(buf.length, maxTag - 1);
      const emit = buf.slice(0, buf.length - keep);
      if (emit) (inThink ? onReasoning : onText)(emit);
      buf = buf.slice(buf.length - keep);
      return;
    }
  };

  return {
    push: (s: string) => { buf += s; drain(false); },
    flush: () => drain(true),
  };
}

// Denetçi: beyin önerisini inceler, {verdict, notes} döner. Ucuz model (Nova Flash) kullanır.
async function reviewProposal(
  userId: string,
  userText: string,
  toolCalls: ToolCall[],
  fallbackModel: string,
): Promise<{ verdict: "approve" | "revise"; notes: string }> {
  try {
    // Denetçi: COUNCIL_AUDITOR ayarlıysa onu, değilse beynin modelini kullan
    // (böylece Ollama ile test ederken bulut anahtarı gerekmez).
    const t = resolveTarget(userId, "brain", process.env.COUNCIL_AUDITOR ?? fallbackModel);
    if (!t.ok) return { verdict: "approve" as const, notes: "Denetçi atlandı: " + t.error };
    const { provider, model: auditorModel, apiKey, baseUrl } = t.target;

    const proposal = JSON.stringify(toolCalls.map((t) => ({ name: t.name, args: t.args })), null, 2);
    const reviewMsgs: ChatMessage[] = [
      {
        role: "system",
        content:
          "Sen bir denetçisin. Kullanıcının isteğini ve asistanın önerdiği Unity araç çağrılarını incele; " +
          "güvenlik, doğruluk ve isteğe uygunluk açısından değerlendir. SADECE şu JSON ile yanıtla: " +
          '{"verdict":"approve"|"revise","notes":"kısa gerekçe"}',
      },
      { role: "user", content: `İstek: ${userText}\n\nÖnerilen araç çağrıları:\n${proposal}` },
    ];

    let out = "";
    for await (const ev of provider.chat({ model: auditorModel, messages: reviewMsgs, tools: [], apiKey, baseUrl })) {
      if (ev.type === "token") out += ev.text;
      else if (ev.type === "done" || ev.type === "error") break;
    }
    const m = out.match(/\{[\s\S]*\}/);
    if (!m) return { verdict: "approve", notes: out.slice(0, 200) };
    const j = JSON.parse(m[0]);
    return { verdict: j.verdict === "revise" ? "revise" : "approve", notes: String(j.notes ?? "") };
  } catch {
    return { verdict: "approve", notes: "(denetçi atlandı)" };
  }
}
