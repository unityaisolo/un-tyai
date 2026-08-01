// Token kullanımını ölçer, maliyet + komisyon hesaplar.
// MVP: in-memory + console. Faz 2'de Postgres + Stripe usage records.

const COMMISSION_RATE = Number(process.env.COMMISSION_RATE ?? "0.20");

// Model başına yaklaşık maliyet (USD / 1M token) — Faz 5'te dinamik tablo.
// NOT: Sağlayıcı fiyatları değişir; buradaki değerler YAKLAŞIK metering içindir.
const PRICING: Record<string, { input: number; output: number }> = {
  // OpenAI / Anthropic (premium)
  "gpt-4.1": { input: 2.0, output: 8.0 },
  "claude-4-sonnet": { input: 3.0, output: 15.0 },
  // Gemini
  "gemini-2.5-pro": { input: 1.25, output: 10.0 },
  "gemini-2.5-flash": { input: 0.3, output: 2.5 },
  "gemini-3.1-flash-lite": { input: 0.1, output: 0.4 }, // flash-lite sınıfı, yaklaşık
  // DeepSeek
  "deepseek-chat": { input: 0.27, output: 1.1 },
  "deepseek-reasoner": { input: 0.55, output: 2.19 },
  // Groq (varsayılan beyin + vision)
  "llama-3.3-70b-versatile": { input: 0.59, output: 0.79 },
  "llama-3.1-8b-instant": { input: 0.05, output: 0.08 },
  "deepseek-r1-distill-llama-70b": { input: 0.75, output: 0.99 },
  "qwen/qwen3.6-27b": { input: 0.29, output: 0.39 }, // yaklaşık
};

// Bilinmeyen modelleri bir kez logla — fiyat tablosu sessizce eskimesin.
const warned = new Set<string>();

/**
 * Tabloda olmayan model için TEMKİNLİ varsayılan (USD / 1M token).
 *
 * NEDEN 0 DEĞİL: eskiden bilinmeyen model $0 sayılıyordu. Bu, tablo eskidiğinde
 * sessizce BİZİM ZARARIMIZA çalışıyordu. Dahası, otomatik model seçimi eklendikten
 * sonra artık modeli kullanıcının sağlayıcısı belirliyor — yani "tabloda olmayan
 * model" istisna değil, NORMAL durum. $0 varsayımı sistematik gelir kaybı demek.
 *
 * Değer büyük modellerin üst bandına yakın seçildi: hata yaparsak kendi lehimize
 * değil, temkinli tarafa yapalım. Gerçek fiyat öğrenilince PRICING'e eklenir.
 * NOVA_FALLBACK_PRICE_IN / _OUT ile ayarlanabilir.
 */
const FALLBACK_PRICE = {
  input: Number(process.env.NOVA_FALLBACK_PRICE_IN ?? "3.0"),
  output: Number(process.env.NOVA_FALLBACK_PRICE_OUT ?? "15.0"),
};

/** Model için 1M-token fiyatı. Önce tam eşleşme, sonra desen kuralları. */
function priceFor(model: string): { input: number; output: number } {
  const exact = PRICING[model];
  if (exact) return exact;
  // Yerel ve ücretsiz modeller GERÇEKTEN $0 — bunlar bizim kaynağımızı harcamıyor.
  if (model.startsWith("ollama/") || model.startsWith("ollama:")) return { input: 0, output: 0 };
  if (model.endsWith(":free")) return { input: 0, output: 0 };
  if (!warned.has(model)) {
    warned.add(model);
    console.warn(
      `[metering] '${model}' PRICING tablosunda yok — temkinli varsayılan ` +
      `($${FALLBACK_PRICE.input}/$${FALLBACK_PRICE.output} per 1M) uygulanıyor. Tabloya ekle.`,
    );
  }
  return FALLBACK_PRICE;
}

/**
 * Akış SÜRERKEN anlık maliyet tahmini (USD). Ücretlendirme kaydı oluşturmaz.
 * Akış ortasında bütçe aşımını yakalamak için kullanılır.
 */
export function estimateUsd(model: string, inputTokens: number, outputTokens: number, pooled: boolean): number {
  const price = priceFor(model);
  const base = (inputTokens / 1e6) * price.input + (outputTokens / 1e6) * price.output;
  return pooled ? base * (1 + COMMISSION_RATE) : base;
}

export interface UsageRecord {
  userId: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  baseCostUsd: number;
  commissionUsd: number;
  totalUsd: number;
  pooled: boolean;
  at: string;
}

const ledger: UsageRecord[] = [];

export function recordUsage(params: {
  userId: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  pooled: boolean;
}): UsageRecord {
  const price = priceFor(params.model);
  const baseCostUsd =
    (params.inputTokens / 1e6) * price.input +
    (params.outputTokens / 1e6) * price.output;
  // Komisyon yalnızca bizim havuz anahtarımız kullanıldığında uygulanır.
  const commissionUsd = params.pooled ? baseCostUsd * COMMISSION_RATE : 0;
  const rec: UsageRecord = {
    ...params,
    baseCostUsd,
    commissionUsd,
    totalUsd: baseCostUsd + commissionUsd,
    at: new Date().toISOString(),
  };
  ledger.push(rec);
  return rec;
}

export function getLedger(userId: string): UsageRecord[] {
  return ledger.filter((r) => r.userId === userId);
}


// Üretim (3D/görsel) — iş başına fiyat + komisyon.
const GEN_PRICING: Record<string, number> = {
  // Tripo v2.5 standard doku ~$0.30/iş. FAL_3D_COST ile override edilebilir.
  "3d": Number(process.env.FAL_3D_COST ?? "0.30"),
  rig: Number(process.env.FAL_RIG_COST ?? "0.20"),
  animation: Number(process.env.FAL_ANIM_COST ?? "0.12"),
  image: Number(process.env.FAL_IMAGE_COST ?? "0.01"),
  texture: 0.1,
};

export function recordGeneration(params: {
  userId: string;
  kind: string;
  model: string;
  pooled: boolean;
}): UsageRecord {
  const baseCostUsd = GEN_PRICING[params.kind] ?? 0.3;
  const commissionUsd = params.pooled ? baseCostUsd * COMMISSION_RATE : 0;
  const rec: UsageRecord = {
    userId: params.userId,
    model: `${params.kind}:${params.model}`,
    inputTokens: 0,
    outputTokens: 0,
    baseCostUsd,
    commissionUsd,
    totalUsd: baseCostUsd + commissionUsd,
    pooled: params.pooled,
    at: new Date().toISOString(),
  };
  ledger.push(rec);
  return rec;
}
