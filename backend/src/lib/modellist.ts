/**
 * SERVİSİN GERÇEK MODEL LİSTESİNİ ÇEKER.
 *
 * NEDEN VAR: kullanıcıya model adını ELLE yazdırmak çalışmıyor. Bir harf hata
 * (ya da eskimiş bir ad) yazınca istek 404 dönüyor ve kullanıcı neyin yanlış
 * olduğunu anlamıyor — bu hatayı bir kez yaşadık. Artık model adları doğrudan
 * sağlayıcıdan gelir; kullanıcı listeden seçer, yazım hatası imkânsız.
 *
 * Neredeyse tüm OpenAI-uyumlu servisler `GET {baseUrl}/models` sunar.
 * Anthropic, Gemini ve Ollama farklı biçim kullanır — ayrı dallar var.
 */

const TIMEOUT_MS = 15_000;

/** Sohbet dışı modelleri (ses/görsel üretim/embedding) listeden ayıklar. */
const NON_CHAT = /(whisper|tts|dall-?e|embed|moderation|rerank|stable-?diffusion|flux|audio|transcribe|image-gen|guard)/i;

async function getJson(url: string, headers: Record<string, string>): Promise<any> {
  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), TIMEOUT_MS);
  try {
    const r = await fetch(url, { headers, signal: ctl.signal });
    const txt = await r.text();
    if (!r.ok) {
      // Sağlayıcı hata metninde anahtar geçebilir — maskele.
      const safe = txt.replace(/\b(sk|gsk|xai|key)[-_][A-Za-z0-9_\-]{8,}/gi, "***").slice(0, 200);
      throw new Error(`${r.status} ${safe}`);
    }
    return JSON.parse(txt);
  } finally { clearTimeout(t); }
}

function dedupeSort(ids: string[]): string[] {
  return [...new Set(ids.filter(Boolean))]
    .filter((m) => !NON_CHAT.test(m))
    .sort((a, b) => a.localeCompare(b));
}

export interface ModelListResult {
  models: string[];
  /** Liste çekilemediyse sebebi (kullanıcıya gösterilir) */
  error?: string;
}

/**
 * @param provider  yönlendirme kimliği (openai/anthropic/gemini/ollama/custom/...)
 * @param baseUrl   OpenAI-uyumlu servisler için taban adres
 * @param apiKey    kullanıcının anahtarı (yerel servislerde boş olabilir)
 */
export async function listModels(
  provider: string,
  baseUrl: string,
  apiKey: string,
): Promise<ModelListResult> {
  try {
    // ---- Ollama: kendi ucu, anahtar yok
    if (provider === "ollama") {
      const host = (process.env.OLLAMA_HOST ?? "http://localhost:11434").replace(/\/+$/, "");
      const j = await getJson(`${host}/api/tags`, {});
      const ids = (j?.models ?? []).map((m: any) => "ollama/" + String(m?.name ?? "")).filter((s: string) => s !== "ollama/");
      return { models: dedupeSort(ids) };
    }

    // ---- Anthropic: x-api-key + sürüm başlığı
    if (provider === "anthropic") {
      const j = await getJson("https://api.anthropic.com/v1/models", {
        "x-api-key": apiKey,
        "anthropic-version": "2023-06-01",
      });
      return { models: dedupeSort((j?.data ?? []).map((m: any) => String(m?.id ?? ""))) };
    }

    // ---- Gemini: anahtar sorgu parametresinde, ad "models/..." ile gelir
    if (provider === "gemini") {
      const j = await getJson(
        `https://generativelanguage.googleapis.com/v1beta/models?key=${encodeURIComponent(apiKey)}`, {});
      const ids = (j?.models ?? [])
        .filter((m: any) => (m?.supportedGenerationMethods ?? []).includes("generateContent"))
        .map((m: any) => String(m?.name ?? "").replace(/^models\//, ""));
      return { models: dedupeSort(ids) };
    }

    // ---- OpenAI-uyumlu (openai / groq / openrouter / deepseek / custom / yerel)
    const base = (baseUrl || "").replace(/\/+$/, "");
    if (!base) return { models: [], error: "Bu servis için adres bilinmiyor." };
    const j = await getJson(`${base}/models`, apiKey ? { Authorization: `Bearer ${apiKey}` } : {});
    const arr = Array.isArray(j?.data) ? j.data : Array.isArray(j) ? j : [];
    const ids = arr.map((m: any) => String(m?.id ?? m?.name ?? ""));
    return { models: dedupeSort(ids) };
  } catch (e: any) {
    const msg = String(e?.message ?? e);
    if (/abort/i.test(msg)) return { models: [], error: "Sağlayıcı zamanında yanıt vermedi." };
    if (/401|403|invalid|unauthor/i.test(msg)) return { models: [], error: "Anahtar kabul edilmedi — doğru anahtarı girdiğinden emin ol." };
    if (/ECONNREFUSED|fetch failed|ENOTFOUND/i.test(msg)) return { models: [], error: "Adrese bağlanılamadı. Yerel sunucu çalışıyor mu / adres doğru mu?" };
    return { models: [], error: msg.slice(0, 160) };
  }
}
