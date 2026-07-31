import { listModels } from "./modellist.js";

/**
 * OTOMATİK KURULUM — kullanıcı SADECE anahtarı yapıştırır.
 *
 * NEDEN: kullanıcıya model seçtirmek çalışmıyor. Gerçek hatalar:
 *   • "`tool calling` is not supported with this model"  → Nova'nın tüm işi araç
 *     çağırmak; araç desteklemeyen model seçilince hiçbir şey çalışmıyor.
 *   • "413 Request too large … TPM Limit 6000"           → küçük modelin token
 *     limiti Nova'nın istemi için yetersiz.
 * Kullanıcı bunları bilemez. Bu yüzden:
 *   1) anahtardan sağlayıcıyı TANI,
 *   2) model listesini ÇEK,
 *   3) adayları sırala,
 *   4) her adayı GERÇEK bir araç çağrısıyla DENE (probe) — ilk çalışanı seç.
 *
 * Adım 4 kritik: model yeteneğini tahmin etmiyoruz, ölçüyoruz. Böylece
 * "model listesinde vardı ama çalışmadı" durumu ortadan kalkıyor.
 */

export interface AutoResult {
  ok: boolean;
  provider?: string;
  model?: string;
  /** Denenen ve reddedilen modeller (teşhis için) */
  rejected?: { model: string; reason: string }[];
  error?: string;
}

/** OpenAI-uyumlu yerleşik sağlayıcıların adresleri. */
const NATIVE_BASE: Record<string, string> = {
  openai: "https://api.openai.com/v1",
  groq: "https://api.groq.com/openai/v1",
  openrouter: "https://openrouter.ai/api/v1",
  deepseek: "https://api.deepseek.com/v1",
};

/**
 * Anahtar önekinden olası sağlayıcıları sıralar (en olası önce).
 * Önek bilgisi kesin değilse hepsi denenir — tahmin değil, deneme yapıyoruz.
 */
export function candidateProviders(apiKey: string): string[] {
  const k = apiKey.trim();
  if (k.startsWith("gsk_")) return ["groq"];
  if (k.startsWith("sk-ant-")) return ["anthropic"];
  if (k.startsWith("sk-or-")) return ["openrouter"];
  if (k.startsWith("AIza")) return ["gemini"];
  // sk- öneki OpenAI ve DeepSeek'te ortak → ikisini de dene
  if (k.startsWith("sk-")) return ["openai", "deepseek", "openrouter"];
  return ["groq", "openai", "deepseek", "openrouter", "anthropic", "gemini"];
}

/** Sohbet/araç için uygun OLMAYAN modeller. */
const UNSUITABLE = /(embed|whisper|tts|dall-?e|moderation|rerank|guard|audio|transcribe|image|realtime|search|batch)/i;

/**
 * Aday modelleri "Nova için uygunluk" sırasına dizer.
 *
 * Sadece GENEL adlandırma kalıplarına bakar (belirli bir modelin var olduğunu
 * varsaymaz): büyük modeller araç çağırmayı daha iyi destekler ve token limitleri
 * daha geniştir; "8b/mini/instant" gibi küçükler sona atılır çünkü sahadaki iki
 * hata (tool calling yok, TPM 6000) tam olarak onlarda çıktı.
 */
export function rankModels(models: string[]): string[] {
  const score = (m: string): number => {
    const s = m.toLowerCase();
    let p = 0;
    if (/(70b|72b|405b|123b|large|max|pro|opus|sonnet|gpt-4|gpt-5|v3|r1)/.test(s)) p += 4;
    if (/(30b|32b|34b|medium|plus|turbo)/.test(s)) p += 2;
    if (/(instruct|chat|-it\b)/.test(s)) p += 1;
    if (/(8b|7b|4b|3b|1b|mini|nano|tiny|small|instant|lite|haiku|flash)/.test(s)) p -= 4;
    if (/(preview|alpha|beta|exp|deprecat)/.test(s)) p -= 2;
    if (/free/.test(s)) p += 1;   // ücretsiz katman kullanıcı için değerli
    return p;
  };
  return models
    .filter((m) => !UNSUITABLE.test(m))
    .sort((a, b) => score(b) - score(a) || a.localeCompare(b));
}

/**
 * Modeli GERÇEKTEN dener: küçük bir istek + bir araç tanımı gönderir.
 * @returns null = uygun; string = reddetme sebebi
 */
async function probe(
  providerId: string, model: string, apiKey: string, baseUrl: string,
): Promise<string | null> {
  // Bu modül sağlayıcı sınıflarını kullanmaz (döngüsel import olmasın) —
  // OpenAI-uyumlu protokolü doğrudan konuşur. Anthropic/Gemini için probe
  // atlanır; onların araç desteği zaten sağlayıcı sınıfımızda ele alınıyor.
  if (providerId === "anthropic" || providerId === "gemini") return null;

  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), 20_000);
  try {
    const r = await fetch(`${baseUrl.replace(/\/+$/, "")}/chat/completions`, {
      method: "POST",
      signal: ctl.signal,
      // Yerel sunucularda anahtar yok — boşsa Authorization başlığı hiç gönderilmez.
      headers: apiKey
        ? { "Content-Type": "application/json", Authorization: `Bearer ${apiKey}` }
        : { "Content-Type": "application/json" },
      body: JSON.stringify({
        model,
        max_tokens: 8,
        messages: [{ role: "user", content: "ping" }],
        tools: [{
          type: "function",
          function: {
            name: "nova_probe",
            description: "test",
            parameters: { type: "object", properties: { x: { type: "string" } } },
          },
        }],
      }),
    });
    if (r.ok) return null;

    const txt = (await r.text()).slice(0, 300);
    if (/tool|function.?call/i.test(txt) && /not support|unsupported|invalid/i.test(txt))
      return "araç çağırmayı desteklemiyor";
    if (r.status === 413 || /too large|context length|tokens per minute|TPM/i.test(txt))
      return "token limiti Nova için yetersiz";
    if (r.status === 404 || /not found|does not exist|decommission|deprecat/i.test(txt))
      return "model kullanılamıyor";
    if (r.status === 401 || r.status === 403) return "AUTH";      // anahtar sorunu → üste bildir
    if (r.status === 429) return null;                             // geçici sınır; model uygun say
    return `sağlayıcı ${r.status}`;
  } catch (e: any) {
    if (/abort/i.test(String(e?.message ?? e))) return "yanıt vermedi";
    return "bağlanılamadı";
  } finally { clearTimeout(t); }
}

/**
 * Anahtarı tanır, model listesini çeker, çalışan ilk modeli bulur.
 * @param customBaseUrl kendi sunucusu/özel servis için adres (verilirse yalnız o denenir)
 */
export async function autoSetup(apiKey: string, customBaseUrl?: string): Promise<AutoResult> {
  const key = (apiKey ?? "").trim();
  const custom = (customBaseUrl ?? "").trim();

  // Adres verildiyse yalnız o denenir; verilmediyse anahtardan sağlayıcı tanınır.
  const providers = custom ? ["custom"] : candidateProviders(key);
  const rejected: { model: string; reason: string }[] = [];
  let authFailed = false;

  for (const pid of providers) {
    const base = pid === "custom" ? custom : NATIVE_BASE[pid] ?? "";
    const { models, error } = await listModels(pid, base, key);

    if (models.length === 0) {
      if (error && /kabul edilmedi/i.test(error)) { authFailed = true; continue; }
      continue;   // bu sağlayıcı değil — sıradakini dene
    }

    // En fazla 6 adayı dene: fazlası kullanıcıyı bekletir.
    for (const m of rankModels(models).slice(0, 6)) {
      const why = await probe(pid, m, key, base);
      if (why === null) return { ok: true, provider: pid, model: m, rejected };
      if (why === "AUTH") { authFailed = true; break; }
      rejected.push({ model: m, reason: why });
    }
  }

  if (authFailed)
    return { ok: false, error: "Anahtar kabul edilmedi. Tamamını kopyaladığından ve doğru servise ait olduğundan emin ol.", rejected };
  if (rejected.length > 0)
    return { ok: false, error: "Bu anahtarın modelleri Nova ile çalışmıyor (araç çağırma/limit). Ayrıntı için aşağıya bak.", rejected };
  return {
    ok: false,
    error: custom
      ? "Bu adrese bağlanılamadı. Sunucu çalışıyor mu ve adres doğru mu? (genelde /v1 ile biter)"
      : "Anahtar hiçbir bilinen servise uymadı. Kendi sunucunu kullanıyorsan aşağıdan adresini seç.",
  };
}
