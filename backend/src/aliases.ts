// White-label model isimleri. Kullanıcı "Nova ..." görür; backend gerçek modele çevirir.
// Böylece arkadaki modeli istediğimiz an değiştirebiliriz.

const ALIASES: Record<string, string> = {
  "nova-flash": process.env.NOVA_FLASH ?? "llama-3.3-70b-versatile", // Groq — .env'den NOVA_FLASH ile değiştirilebilir
  "nova-vision": process.env.NOVA_VISION ?? "qwen/qwen3.6-27b", // Groq vision (görsel destekli; hesapta scout yoksa qwen3.6 var)
  "nova-openrouter": "meta-llama/llama-3.3-70b-instruct:free", // OpenRouter yedek
  "nova-gemini": "gemini-3.1-flash-lite", // Gemini isteyenler için
  "nova-code": "deepseek-chat", // kod motoru (DeepSeek V3)
  "nova-pro": "claude-4-sonnet", // premium (zor işler)
  "nova-local": "ollama/llama3.1", // ücretsiz yerel
};

/** Nova takma adını gerçek model id'sine çevirir; zaten gerçekse aynen döner. */
export function resolveModel(name: string): string {
  return ALIASES[name] ?? name;
}

export function isAlias(name: string): boolean {
  return name in ALIASES;
}

// ─────────────────────────────────────────────────────────────────────────────
// ROL BAZLI MODEL SEÇİMİ
//
// Kullanıcı her iş için ayrı model seçebilir: pahalı modeli sadece "beyin"e,
// ucuzunu küratöre verir. Seçim kullanıcı başına kasada (vault.json) saklanır.
// Seçim yoksa rolün varsayılan takma adı kullanılır.
// ─────────────────────────────────────────────────────────────────────────────

export type ModelRole = "brain" | "code" | "vision" | "curator";

export const ROLES: ModelRole[] = ["brain", "code", "vision", "curator"];

const ROLE_DEFAULT_ALIAS: Record<ModelRole, string> = {
  brain: "nova-flash",   // araç çağıran ana model
  code: "nova-code",     // kod yazma/okuma
  vision: "nova-vision", // görsel okuma
  curator: "nova-flash", // asset paleti — çok çağrılır, ucuz olmalı
};

/** Ayarlar ekranının gösterdiği rol açıklamaları. */
export const ROLE_INFO: Record<ModelRole, { label: string; hint: string }> = {
  brain:   { label: "Beyin (sohbet + araçlar)", hint: "Sahneyi değiştiren ana model. Araç çağırabilmeli." },
  code:    { label: "Kod",     hint: "Script yazma/okuma. Kod modelleri burada daha iyi." },
  vision:  { label: "Görsel",  hint: "Eklediğin görselleri okur. Görsel destekli model olmalı." },
  curator: { label: "Küratör", hint: "Arazi/şehir asset paletini seçer. Çok çağrılır — ucuz model seç." },
};

/**
 * Bir rol için kullanılacak GERÇEK model adı.
 *   1) İstekte somut model verilmişse (takma ad değil) → o
 *   2) Kullanıcının o role kaydettiği model → o
 *   3) Rolün varsayılanı
 *
 * NOT: getSettings burada tembel (lazy) import edilir — aliases.ts ile keyvault.ts
 * arasında döngüsel import oluşmasın.
 */
export function modelFor(userId: string, role: ModelRole, requested?: string): string {
  if (requested && requested.trim() && !isAlias(requested)) return requested.trim();

  // eslint-disable-next-line @typescript-eslint/no-var-requires
  let chosen: string | undefined;
  try {
    chosen = settingsReader?.(userId)?.models?.[role];
  } catch { /* kasa okunamazsa varsayılana düş */ }

  if (chosen && chosen.trim()) return resolveModel(chosen.trim());
  return resolveModel(ROLE_DEFAULT_ALIAS[role]);
}

/** Döngüsel import olmasın: index.ts açılışta kasa okuyucusunu buraya bağlar. */
type SettingsReader = (userId: string) => { models?: Record<string, string> } | undefined;
let settingsReader: SettingsReader | null = null;
export function bindSettingsReader(fn: SettingsReader): void { settingsReader = fn; }

/**
 * Ayarlar ekranındaki model ÖNERİLERİ.
 *
 * Bilerek YALNIZCA bu projede gerçekten kullanılan takma adlardan üretilir —
 * uydurma/eskimiş model adı listelemeyiz. Kullanıcı istediği model adını elle de
 * yazabilir; bu liste sadece hızlı seçim kolaylığı.
 */
export function modelSuggestions(): { value: string; label: string }[] {
  const LABEL: Record<string, string> = {
    "nova-flash": "Hızlı / ücretsiz (Groq)",
    "nova-vision": "Görsel okuyabilen (Groq)",
    "nova-openrouter": "Ücretsiz (OpenRouter)",
    "nova-gemini": "Google Gemini",
    "nova-code": "Kod (DeepSeek)",
    "nova-pro": "Premium (Claude)",
    "nova-local": "Yerel (Ollama)",
  };
  return Object.keys(ALIASES).map((a) => ({
    value: a,
    label: `${LABEL[a] ?? a} — ${ALIASES[a]}`,
  }));
}

/** Rollerin şu an hangi modele çözüldüğü (Ayarlar ekranı "şu an: ..." için). */
export function effectiveModels(userId: string): Record<ModelRole, string> {
  const out = {} as Record<ModelRole, string>;
  for (const r of ROLES) out[r] = modelFor(userId, r);
  return out;
}
