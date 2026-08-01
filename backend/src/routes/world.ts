import { Router, type Request, type Response } from "express";
import { z } from "zod";
import type { ChatMessage, ChatProvider } from "../providers/types.js";
import { resolveTarget } from "../lib/target.js";
import { gate, charge } from "../lib/gate.js";
import { recordUsage } from "../billing/metering.js";

export const worldRouter = Router();

/**
 * Muhakeme (reasoning) bloklarını temizler.
 *
 * gpt-oss / qwen / deepseek-r1 gibi modeller cevaptan ÖNCE <think>...</think>
 * bloğu yazar. Bu blok:
 *   1) kullanıcıya "bulgu" diye sızıyordu (İngilizce düşünme metni),
 *   2) içindeki süslü parantezler JSON yakalayan greedy regex'i bozuyordu.
 * Kapanış etiketi yoksa (yanıt kesildiyse) blok sonuna kadar atılır.
 */
function stripReasoning(text: string): string {
  return String(text ?? "")
    .replace(/<think>[\s\S]*?<\/think>/gi, "")
    .replace(/<think>[\s\S]*$/i, "")
    .replace(/<reasoning>[\s\S]*?<\/reasoning>/gi, "")
    .replace(/^\s*(?:analysis|thinking)\s*:[\s\S]*?(?=\{)/i, "")
    .trim();
}

/**
 * Sağlayıcıdan tek seferlik metin yanıtı toplar (<think> ayıklanmış).
 * 429 (rate limit) hatasında sağlayıcının önerdiği süre kadar bekleyip BİR kez daha
 * dener — Groq ücretsiz katman TPM'i anlık doluyor; kısa bekleme isteklerin çoğunu kurtarır.
 */
async function chatTextWithRetry(
  provider: ChatProvider, model: string, messages: ChatMessage[], apiKey: string, baseUrl?: string,
): Promise<{ text: string; inTok: number; outTok: number }> {
  const once = async () => {
    let text = "";
    let inTok = 0, outTok = 0;
    for await (const ev of provider.chat({ model, messages, tools: [], apiKey, baseUrl })) {
      if (ev.type === "token") text += ev.text;
      else if (ev.type === "usage") { inTok += ev.inputTokens; outTok += ev.outputTokens; }
      else if (ev.type === "error") throw new Error(ev.message);
      else if (ev.type === "done") break;
    }
    return { text: text.replace(/<think>[\s\S]*?<\/think>/g, ""), inTok, outTok };
  };
  try { return await once(); }
  catch (e: any) {
    const msg = String(e?.message ?? e);
    if (/429|rate.?limit/i.test(msg)) {
      const m = msg.match(/try again in ([\d.]+)/i);
      const wait = Math.min(m ? parseFloat(m[1]) + 1 : 15, 25);
      console.warn(`[world] 429 rate limit — ${wait.toFixed(0)} sn bekleyip yeniden denenecek`);
      await new Promise((r) => setTimeout(r, wait * 1000));
      return await once(); // ikinci deneme de patlarsa hata üste fırlar (fallback'ler devralır)
    }
    throw e;
  }
}

const Body = z.object({
  prompt: z.string().min(1),
  model: z.string().default("nova-flash"),
  styles: z.array(z.string()).default([]),
  themes: z.array(z.string()).default([]),
});

// Plan şeması — plugin yerleştirme motorunun beklediği alanlar.
type Plan = {
  style: string;
  themes: string[];
  size: number;
  density: number;
  greenery: number;
  vehicles: boolean;
  props: boolean;
  summary: string;
};

function clampPlan(p: Partial<Plan>, styles: string[], themes: string[], prompt: string): Plan {
  const style =
    p.style && (styles.length === 0 || styles.includes(p.style)) ? p.style : (styles[0] ?? "any");
  const valTh = Array.isArray(p.themes)
    ? p.themes.filter((t) => themes.length === 0 || themes.includes(t))
    : [];
  return {
    style,
    themes: valTh,
    size: Math.max(4, Math.min(16, Math.round(p.size ?? 10))),
    density: Math.max(0.1, Math.min(1, p.density ?? 0.6)),
    greenery: Math.max(0, Math.min(1, p.greenery ?? 0.4)),
    vehicles: p.vehicles ?? true,
    props: p.props ?? true,
    summary: (p.summary ?? prompt).slice(0, 200),
  };
}

// AI yoksa/başarısızsa: prompttan kaba plan çıkar (Türkçe + İngilizce ipuçları).
function heuristic(prompt: string, styles: string[], themes: string[]): Plan {
  const t = prompt.toLowerCase();
  const has = (...ws: string[]) => ws.some((w) => t.includes(w));
  const picked: string[] = [];
  const map: Record<string, string[]> = {
    medieval: ["ortaçağ", "orta çağ", "medieval", "köy", "kasaba", "village"],
    fantasy: ["fantazi", "fantasy", "büyülü"],
    city: ["şehir", "city", "modern", "kent"],
    scifi: ["sci-fi", "scifi", "uzay", "gelecek", "future"],
    farm: ["çiftlik", "farm", "köylük"],
    pirate: ["korsan", "pirate", "liman"],
  };
  for (const th of themes) for (const [k, ws] of Object.entries(map)) if (th === k && has(...ws)) picked.push(th);
  const density = has("yoğun", "kalabalık", "dense", "büyük", "big") ? 0.8 : has("seyrek", "az", "sparse", "küçük", "small") ? 0.35 : 0.6;
  const greenery = has("orman", "ağaç", "yeşil", "forest", "park", "doğa") ? 0.7 : 0.35;
  let style = styles[0] ?? "any";
  if (has("gerçekçi", "realistic")) style = styles.includes("realistic") ? "realistic" : style;
  if (has("low-poly", "low poly", "basit", "stilize", "stylized")) style = styles.includes("low-poly") ? "low-poly" : style;
  const size = has("büyük", "geniş", "big", "large") ? 13 : has("küçük", "small") ? 7 : 10;
  return clampPlan({ style, themes: picked, size, density, greenery, vehicles: !has("araçsız"), props: true, summary: prompt }, styles, themes, prompt);
}

function extractJson(text: string): Partial<Plan> | null {
  const m = text.match(/\{[\s\S]*\}/);
  if (!m) return null;
  try { return JSON.parse(m[0]); } catch { return null; }
}

// NOT: /v1/world/review (AI görsel denetim) 2026-07'de KALDIRILDI.
// Vision modeli JSON karar yerine muhakeme metni döndürüyordu; her harita kurulumunda
// boşuna token harcayıp kullanıcıya anlamsız bulgu gösteriyordu. Sahne doğrulaması
// artık yalnızca Unity tarafındaki deterministik SceneLint ile yapılıyor.

// ---- KÜRATÖR BEYİN: haritaya uygun asset'leri LLM SEÇER (kurulumdan önce) ----
// Kullanıcının eleştirisi haklıydı: beyin döngüde yoktu, paletler rastgeleydi.
// Artık aday listesi modele gider; model temaya/ölçeğe/uyuma göre parça seçer.
const CurateBody = z.object({
  mapType: z.string(),
  theme: z.string().default("any"),
  candidates: z.record(z.array(z.object({
    file: z.string(), name: z.string(),
    theme: z.string().nullish(), family: z.string().nullish(), size: z.number().nullish(),
  }))),
  counts: z.record(z.number()),
});

worldRouter.post("/world/curate", async (req: Request, res: Response) => {
  const parsed = CurateBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { mapType, theme, candidates, counts } = parsed.data;

  // Anahtar yoksa: deterministik ilk-N (sistem yine çalışır, sadece beyinsiz)
  const fallback = () => {
    const picks: Record<string, string[]> = {};
    for (const [role, list] of Object.entries(candidates))
      picks[role] = list.slice(0, counts[role] ?? 5).map((c) => c.file);
    return picks;
  };

  try {
    if (!(await gate(req, res, "world")).ok) return;
    const t = resolveTarget(req.userId, "curator");
    if (!t.ok) { res.json({ picks: fallback(), notes: [t.error], source: "fallback" }); return; }
    const { provider, model, apiKey, pooled, baseUrl } = t.target;

    let catalogTxt = "";
    for (const [role, list] of Object.entries(candidates)) {
      catalogTxt += `\n## ${role} (en fazla ${counts[role] ?? 5} seç):\n`;
      for (const c of list)
        catalogTxt += `- ${c.file} | ${c.name} | tema=${c.theme ?? "?"} | aile=${c.family ?? "?"} | ~${(c.size ?? 0).toFixed(1)}m\n`;
    }

    const sys =
      "Sen bir oyun dünyası sanat yönetmenisin. Görev: '" + mapType + "' haritası (tema: " + theme + ") için " +
      "aday listesinden GÖRSEL OLARAK TUTARLI bir asset seti seç.\n" +
      "İLKELER: 1) Tema uyumu şart — modern haritaya kırsal/fantastik parça seçme, tersi de geçerli. " +
      "2) Aynı aileden parçaları tercih et (uyumlu görünüm). 3) Ölçüsü rolüne mantıksız geleni seçme. " +
      "4) İsmi role uymayanı (ör. house rolünde 'Tree House') SEÇME. 5) Çeşitlilik iyi ama uyum önce gelir.\n" +
      'SADECE şu JSON ile yanıt ver: {"picks":{"<rol>":["<file>",...]},"notes":"tek cümle Türkçe gerekçe"}';

    // Sağlayıcıya provider.chat üzerinden git — NOVA_FLASH hangi sağlayıcıya
    // çözülürse çözülsün çalışır (Groq URL hardcode'u kaldırıldı).
    const messages: ChatMessage[] = [
      { role: "system", content: sys },
      { role: "user", content: "ADAYLAR:" + catalogTxt },
    ];
    const { text, inTok, outTok } = await chatTextWithRetry(provider, model, messages, apiKey, baseUrl);
    try { const u = recordUsage({ userId: req.userId, model, inputTokens: inTok, outputTokens: outTok, pooled }); await charge(req.userId, u.totalUsd, pooled); } catch { /* opsiyonel */ }
    const m = stripReasoning(text).match(/\{[\s\S]*\}/);
    if (!m) { res.json({ picks: fallback(), notes: ["Beyin yanıtı çözümlenemedi — varsayılan seçim."], source: "fallback" }); return; }
    const parsedJson: any = JSON.parse(m[0]);
    const picks: Record<string, string[]> = {};
    for (const [role, list] of Object.entries(candidates)) {
      const valid = new Set(list.map((c) => c.file));
      const chosen = Array.isArray(parsedJson?.picks?.[role])
        ? parsedJson.picks[role].filter((f: any) => valid.has(String(f))).map(String)
        : [];
      picks[role] = chosen.length > 0 ? chosen.slice(0, counts[role] ?? 5) : list.slice(0, counts[role] ?? 5).map((c) => c.file);
    }
    res.json({ picks, notes: [String(parsedJson?.notes ?? "")].filter(Boolean), source: "ai" });
  } catch (e: any) {
    res.json({ picks: fallback(), notes: ["Küratör hata: " + String(e?.message ?? e)], source: "fallback" });
  }
});

// ---- E1: DOĞAL DİLDEN DEKOR PLANI ----
// "buraya kamp alanı kur", "yol kenarına çit + lamba döşe" → rol karışımı (mix).
// Unity NovaDecorator bu planı alır, küratörle asset seçer, yerleştirir.
const DecorBody = z.object({
  prompt: z.string().min(1),
  model: z.string().default("nova-flash"),
  roles: z.array(z.string()).default(["tree", "bush", "rock", "flower", "fence", "lamp", "bench", "sign", "fountain", "prop"]),
});

type DecorMix = { role: string; count: number; match?: string; ban?: string };

function clampDecor(raw: any, roles: string[], prompt: string): { name: string; mix: DecorMix[]; notes: string } {
  const mixIn: any[] = Array.isArray(raw?.mix) ? raw.mix : [];
  const seen = new Map<string, DecorMix>();
  for (const m of mixIn) {
    const role = String(m?.role ?? "");
    if (!roles.includes(role)) continue;
    const count = Math.max(1, Math.min(15, Math.round(Number(m?.count) || 0)));
    const cur = seen.get(role);
    if (cur) cur.count = Math.min(15, cur.count + count);
    else seen.set(role, {
      role, count,
      match: typeof m?.match === "string" && m.match.trim() ? m.match.trim().slice(0, 120) : undefined,
      ban: typeof m?.ban === "string" && m.ban.trim() ? m.ban.trim().slice(0, 120) : undefined,
    });
  }
  let mix = [...seen.values()].slice(0, 6);
  // Toplam obje sınırı (sahne şişmesin)
  let total = mix.reduce((s, m) => s + m.count, 0);
  if (total > 40) { const k = 40 / total; mix = mix.map((m) => ({ ...m, count: Math.max(1, Math.round(m.count * k)) })); }
  return {
    name: String(raw?.name ?? prompt).slice(0, 40),
    mix,
    notes: String(raw?.notes ?? "").slice(0, 200),
  };
}

// Anahtar yoksa: anahtar kelimeden hazır karışım (Unity preset'lerinin aynısı)
function heuristicDecor(prompt: string, roles: string[]): { name: string; mix: DecorMix[]; notes: string } {
  const t = prompt.toLowerCase();
  const has = (...ws: string[]) => ws.some((w) => t.includes(w));
  const rockBan = "temple|bridge|mountain|iceberg|crystal|cove|swallow|cliff|walkway|gem";
  let name = "Orman köşesi";
  let mix: DecorMix[] = [
    { role: "tree", count: 10, ban: "palm|xmas|christmas" },
    { role: "bush", count: 6, ban: "hedge" },
    { role: "rock", count: 4, ban: rockBan },
  ];
  if (has("kamp", "camp", "ateş", "çadır", "tent")) {
    name = "Kamp alanı";
    mix = [
      // NOT: "box" deseni mailbox'ı da yakalıyordu — spesifik kelimeler kullan
      { role: "prop", count: 5, match: "barrel|crate|chest|\\blog\\b|firewood" },
      { role: "bench", count: 1, ban: "working" },
      { role: "rock", count: 5, ban: rockBan },
      { role: "tree", count: 3, ban: "palm|xmas|christmas" },
    ];
  } else if (has("bahçe", "garden", "çit", "fence")) {
    name = "Köy bahçesi";
    mix = [
      { role: "fence", count: 6, ban: "barrier|guardrail|traffic|modular" },
      { role: "bush", count: 4 },
      { role: "tree", count: 2, ban: "palm|dead" },
      { role: "prop", count: 2, match: "mailbox|barrel|pot|crate" },
    ];
  } else if (has("kaya", "rock", "taş")) {
    name = "Kayalık";
    mix = [{ role: "rock", count: 12, ban: rockBan }, { role: "bush", count: 2, ban: "hedge" }];
  } else if (has("çiçek", "çayır", "meadow", "flower")) {
    name = "Çiçek çayırı";
    mix = [{ role: "flower", count: 14, ban: "pot|mushroom|mushnub" }, { role: "bush", count: 3, ban: "hedge" }];
  } else if (has("lamba", "lamp", "yol kenarı", "sokak")) {
    name = "Yol kenarı";
    mix = [
      { role: "lamp", count: 4 },
      { role: "fence", count: 5, ban: "traffic" },
      { role: "bench", count: 2 },
    ];
  }
  return { name, mix: mix.filter((m) => roles.includes(m.role)), notes: "kural tabanlı plan (AI yok)" };
}

worldRouter.post("/world/decor", async (req: Request, res: Response) => {
  const parsed = DecorBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { prompt, roles } = parsed.data;
  const requestedModel = parsed.data.model;

  try {
    if (!(await gate(req, res, "world")).ok) return;
    const t = resolveTarget(req.userId, "brain", requestedModel);
    if (!t.ok) {
      res.json({ plan: heuristicDecor(prompt, roles), source: "heuristic", notes: t.error });
      return;
    }
    const { provider, model, apiKey, pooled, baseUrl } = t.target;

    const sys =
      "Sen bir oyun sahnesi dekoratörü planlayıcısısın. Kullanıcının isteğini bir dekor karışımına çevir. " +
      "SADECE geçerli JSON döndür:\n" +
      '{"name":"kısa Türkçe ad","mix":[{"role":"<rol>","count":1-15,"match":"regex?","ban":"regex?"}],"notes":"tek cümle"}\n' +
      `Roller YALNIZ şu listeden: ${JSON.stringify(roles)}. En fazla 6 satır, toplam obje 40'ı geçmesin.\n` +
      "match/ban İNGİLİZCE asset adlarına uygulanan opsiyonel regex'lerdir (ör. match:'barrel|crate', " +
      "ban:'palm|xmas'). Emin değilsen match VERME (boş bırak) — yanlış match hiç asset bulamaz. " +
      "DİKKAT: 'box' gibi geniş kelimeler 'mailbox'ı da yakalar — spesifik kelimeler kullan " +
      "(barrel, crate, chest) ve gerekirse ban'a mail|hydrant gibi alakasızları ekle. " +
      "Tema uyumu: kamp→varil/sandık+kaya, bahçe→çit+çalı, sokak→lamba+bank gibi mantıklı setler kur.";

    const messages: ChatMessage[] = [
      { role: "system", content: sys },
      { role: "user", content: prompt },
    ];

    const { text, inTok, outTok } = await chatTextWithRetry(provider, model, messages, apiKey, baseUrl);
    try { const u = recordUsage({ userId: req.userId, model, inputTokens: inTok, outputTokens: outTok, pooled }); await charge(req.userId, u.totalUsd, pooled); } catch { /* opsiyonel */ }

    const raw = extractJson(text);
    if (!raw) { res.json({ plan: heuristicDecor(prompt, roles), source: "heuristic-fallback" }); return; }
    const plan = clampDecor(raw, roles, prompt);
    if (plan.mix.length === 0) { res.json({ plan: heuristicDecor(prompt, roles), source: "heuristic-empty" }); return; }
    res.json({ plan, source: "ai" });
  } catch (e: any) {
    res.json({ plan: heuristicDecor(prompt, roles), source: "heuristic-error", error: String(e?.message ?? e) });
  }
});

// ---- DOĞAL DİLDEN ARAZİ PLANI ----
// Kullanıcı haritayı cümleyle tarif eder ("kıvrımlı nehirli çam ormanı, hava bulutlu");
// beyin bunu TerrainPlan alanlarına çevirir. Anahtar yoksa TR/EN heuristik devreye girer.
const TerrainBody = z.object({
  prompt: z.string().min(1),
  model: z.string().default("nova-flash"),
  biomes: z.array(z.string()).default(["plains", "forest", "valley", "hills", "coast", "desert", "snow", "swamp", "canyon", "volcanic"]),
  skies: z.array(z.string()).default([]), // Unity'nin gökyüzü seçenek adları (aynen döndürülür)
});

type TerrainPlanJson = {
  biome: string; size: number; density: number;
  river: boolean; lake: boolean; riverCurve: number;
  trees: boolean; rocks: boolean; bushes: boolean;
  sky: string; summary: string;
};

function clampTerrain(p: Partial<TerrainPlanJson>, biomes: string[], skies: string[], prompt: string): TerrainPlanJson {
  const biome = p.biome && biomes.includes(p.biome) ? p.biome : biomes[0] ?? "plains";
  const sky = p.sky && skies.includes(p.sky) ? p.sky : "";
  return {
    biome,
    size: Math.max(150, Math.min(1000, Math.round(p.size ?? 400))),
    density: Math.max(0.1, Math.min(1, p.density ?? 0.6)),
    river: p.river ?? false,
    lake: p.lake ?? false,
    riverCurve: Math.max(0, Math.min(1, p.riverCurve ?? 0.5)),
    trees: p.trees ?? true,
    rocks: p.rocks ?? true,
    bushes: p.bushes ?? true,
    sky,
    summary: (p.summary ?? prompt).slice(0, 200),
  };
}

function heuristicTerrain(prompt: string, biomes: string[], skies: string[]): TerrainPlanJson {
  const t = prompt.toLowerCase();
  const has = (...ws: string[]) => ws.some((w) => t.includes(w));
  let biome = "plains";
  if (has("volkan", "volcano", "lav", "lava", "magma")) biome = "volcanic";
  else if (has("kar", "snow", "buz", "ice", "kış", "winter")) biome = "snow";
  else if (has("bataklık", "swamp", "marsh", "sazlık")) biome = "swamp";
  else if (has("kanyon", "canyon", "mesa", "plato", "plateau")) biome = "canyon";
  else if (has("çöl", "desert", "kum")) biome = "desert";
  else if (has("orman", "forest", "çam", "ağaçlık")) biome = "forest";
  else if (has("vadi", "valley", "dağ", "mountain")) biome = "valley";
  else if (has("tepe", "hill")) biome = "hills";
  else if (has("sahil", "coast", "deniz", "plaj", "beach", "kıyı")) biome = "coast";
  if (!biomes.includes(biome)) biome = biomes[0] ?? "plains";

  const river = has("nehr", "nehir", "ırmak", "ırmağ", "river", "dere", "akarsu"); // "nehri/ırmağı" ekleri için kökler
  const straight = has("düz", "straight", "kanal");
  const curvy = has("kıvrım", "kıvrımlı", "menderes", "winding", "curvy", "dolambaç");
  let sky = "";
  const skyWord = has("bulut", "cloud", "kapalı") ? ["bulut", "cloud", "kapalı", "overcast"]
    : has("gece", "night", "yıldız") ? ["gece", "night", "moon", "ay"]
    : has("günbatımı", "gün batımı", "sunset", "akşam") ? ["günbatım", "sunset", "akşam", "dusk"]
    : has("sis", "fog", "mist") ? ["sis", "fog", "mist"] : null;
  if (skyWord) sky = skies.find((s) => skyWord.some((w) => s.toLowerCase().includes(w))) ?? "";

  return clampTerrain({
    biome,
    river,
    lake: has("göl", "lake"),
    riverCurve: curvy ? 0.9 : straight ? 0.15 : 0.5,
    trees: !has("ağaçsız", "çorak", "barren") && biome !== "desert" && biome !== "canyon" && biome !== "volcanic",
    rocks: true,
    bushes: biome !== "desert" && biome !== "canyon" && biome !== "volcanic" && biome !== "snow",
    density: has("yoğun", "sık", "dense", "gür") ? 0.85 : has("seyrek", "az", "sparse") ? 0.3 : 0.6,
    size: has("büyük", "geniş", "dev", "big", "large", "huge") ? 700 : has("küçük", "small", "mini") ? 220 : 400,
    sky,
    summary: prompt,
  }, biomes, skies, prompt);
}

worldRouter.post("/world/terrain", async (req: Request, res: Response) => {
  const parsed = TerrainBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { prompt, biomes, skies } = parsed.data;
  const requestedModel = parsed.data.model;

  try {
    if (!(await gate(req, res, "world")).ok) return;
    const t = resolveTarget(req.userId, "brain", requestedModel);
    if (!t.ok) {
      res.json({ plan: heuristicTerrain(prompt, biomes, skies), source: "heuristic", notes: t.error });
      return;
    }
    const { provider, model, apiKey, pooled, baseUrl } = t.target;

    const sys =
      "Sen bir oyun dünyası arazi planlayıcısısın. Kullanıcının haritayı anlatan cümlesini " +
      "aşağıdaki JSON şemasına çevir. SADECE geçerli JSON döndür, açıklama yazma:\n" +
      '{"biome": string, "size": number(150-1000, metre), "density": number(0-1, bitki yoğunluğu), ' +
      '"river": boolean, "lake": boolean, "riverCurve": number(0=dümdüz, 0.5=doğal, 1=çok kıvrımlı/menderes), ' +
      '"trees": boolean, "rocks": boolean, "bushes": boolean, "sky": string, "summary": string(kısa Türkçe özet)}.\n' +
      `Geçerli biome değerleri: ${JSON.stringify(biomes)}. ` +
      `sky, şu listeden AYNEN bir eleman olmalı ya da boş string: ${JSON.stringify(skies)}. ` +
      "Kullanıcı hava/atmosfer belirtmediyse sky boş kalsın. Nehir/göl istenmediyse false ver.";

    const messages: ChatMessage[] = [
      { role: "system", content: sys },
      { role: "user", content: prompt },
    ];

    const { text, inTok, outTok } = await chatTextWithRetry(provider, model, messages, apiKey, baseUrl);
    try { const u = recordUsage({ userId: req.userId, model, inputTokens: inTok, outputTokens: outTok, pooled }); await charge(req.userId, u.totalUsd, pooled); } catch { /* opsiyonel */ }

    const raw = extractJson(text) as Partial<TerrainPlanJson> | null;
    if (!raw) { res.json({ plan: heuristicTerrain(prompt, biomes, skies), source: "heuristic-fallback" }); return; }
    res.json({ plan: clampTerrain(raw, biomes, skies, prompt), source: "ai" });
  } catch (e: any) {
    res.json({ plan: heuristicTerrain(prompt, biomes, skies), source: "heuristic-error", error: String(e?.message ?? e) });
  }
});

worldRouter.post("/world/plan", async (req: Request, res: Response) => {
  const parsed = Body.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { prompt, styles, themes } = parsed.data;
  const requestedModel = parsed.data.model;

  // AI denemesi; her hata durumunda heuristik plana düş.
  try {
    if (!(await gate(req, res, "world")).ok) return;
    const t = resolveTarget(req.userId, "brain", requestedModel);
    if (!t.ok) { res.json({ plan: heuristic(prompt, styles, themes), source: "heuristic", notes: t.error }); return; }
    const { provider, model, apiKey, pooled, baseUrl } = t.target;

    const sys =
      "Sen bir oyun dünyası tasarım planlayıcısısın. Kullanıcının isteğini bir 3D şehir/kasaba yerleşim planına çevir. " +
      "SADECE geçerli JSON döndür, açıklama yok. Alanlar: " +
      '{"style": string, "themes": string[], "size": number(4-16), "density": number(0-1), "greenery": number(0-1), "vehicles": boolean, "props": boolean, "summary": string}. ' +
      `Geçerli style değerleri: ${JSON.stringify(styles)}. Geçerli themes değerleri: ${JSON.stringify(themes)}. ` +
      "style bunlardan biri olmalı; themes yalnızca bu listeden seçilmeli. size haritanın ızgara boyutu, density bina yoğunluğu, greenery ağaç miktarı.";

    const messages: ChatMessage[] = [
      { role: "system", content: sys },
      { role: "user", content: prompt },
    ];

    let text = "";
    let inTok = 0, outTok = 0;
    for await (const ev of provider.chat({ model, messages, tools: [], apiKey, baseUrl })) {
      if (ev.type === "token") text += ev.text;
      else if (ev.type === "usage") { inTok += ev.inputTokens; outTok += ev.outputTokens; }
      else if (ev.type === "error") throw new Error(ev.message);
      else if (ev.type === "done") break;
    }
    try { const u = recordUsage({ userId: req.userId, model, inputTokens: inTok, outputTokens: outTok, pooled }); await charge(req.userId, u.totalUsd, pooled); } catch { /* metering opsiyonel */ }

    const raw = extractJson(text);
    if (!raw) { res.json({ plan: heuristic(prompt, styles, themes), source: "heuristic-fallback" }); return; }
    res.json({ plan: clampPlan(raw, styles, themes, prompt), source: "ai" });
  } catch (e: any) {
    res.json({ plan: heuristic(prompt, styles, themes), source: "heuristic-error", error: String(e?.message ?? e) });
  }
});
