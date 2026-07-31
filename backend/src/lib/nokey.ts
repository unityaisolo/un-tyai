/**
 * "Anahtar yok" hatası için TEK metin kaynağı.
 *
 * BYO zorunlu olduğu için kullanıcı bu mesajı sık görecek — ne yapması gerektiğini
 * net söylemeli. Sağlayıcının anahtarını nereden alacağı da yazılı.
 *
 * ÖNEMLİ: Bu mesaj asla anahtar değeri, anahtar maskesi veya havuz anahtarının
 * varlığı hakkında bilgi içermez.
 */

const WHERE: Record<string, string> = {
  openai: "https://platform.openai.com/api-keys",
  anthropic: "https://console.anthropic.com/settings/keys",
  gemini: "https://aistudio.google.com/apikey",
  deepseek: "https://platform.deepseek.com/api_keys",
  groq: "https://console.groq.com/keys",
  openrouter: "https://openrouter.ai/keys",
  custom: "kendi sağlayıcının panelinden",
  fal: "https://fal.ai/dashboard/keys",
  tripo: "https://platform.tripo3d.ai",
};

const LABEL: Record<string, string> = {
  openai: "OpenAI",
  anthropic: "Anthropic (Claude)",
  gemini: "Google Gemini",
  deepseek: "DeepSeek",
  groq: "Groq",
  openrouter: "OpenRouter",
  custom: "Özel endpoint",
  fal: "fal.ai (3D üretim)",
  tripo: "Tripo (3D üretim)",
};

export function noKeyMessage(provider: string): string {
  const label = LABEL[provider] ?? provider;
  const where = WHERE[provider];
  return (
    `${label} için API anahtarı yok. Nova penceresinde Ayarlar sekmesini açıp anahtarını ekle.` +
    (where ? ` Anahtarı buradan alabilirsin: ${where}` : "")
  );
}

export function providerLabel(provider: string): string {
  return LABEL[provider] ?? provider;
}

export function providerKeyUrl(provider: string): string {
  return WHERE[provider] ?? "";
}
