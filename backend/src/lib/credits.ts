import { store, storeInfo, type Account } from "./creditstore.js";

/**
 * ÜYELİK + KREDİ MANTIĞI.
 *
 * ÜRÜN KURALI (2026-07-30 kararı):
 *   • Kod Ajanı  → kullanıcı KENDİ anahtarını bağlarsa bedava; bağlamazsa kredi harcar.
 *   • 3D Stüdyo / Malzeme / Dünya → SADECE Nova kredisiyle (bizim havuz anahtarlarımız).
 *
 * Kredi birimi: 1 kredi = 0.001 USD ("mil"). Tam sayı tutuluyor ki kayan nokta
 * yuvarlama hatası bakiyeyi aşındırmasın.
 *
 * SAKLAMA bu dosyada DEĞİL: creditstore.ts sürücüsünde (dosya veya Firestore).
 * Tüm bakiye değişiklikleri `update()` içinden geçer; bu, Firestore transaction'ı
 * sayesinde çok örnekli çalışmada da atomiktir (bkz. docs/KREDI-DENETIMI.md madde 5).
 *
 * API ASENKRON: Firestore ağ üzerinden çalışıyor. Senkron bir imza sürdürülemezdi —
 * sessizce eski değeri okumak, ücretlendirmeyi kaybetmenin en kolay yoluydu.
 */

export const CREDITS_PER_USD = 1000;

export type Feature = "chat" | "world" | "studio" | "material";
export type { Account };

/** Kredi gerektiren (bizim anahtarlarımızı kullanan) özellikler. */
const PAID_FEATURES: Feature[] = ["world", "studio", "material"];

export function isPaidFeature(f: string): boolean {
  return (PAID_FEATURES as string[]).includes(f);
}

/**
 * ÜYELİK AYLIK TAVANI (kredi). 0 = sınırsız.
 *
 * NEDEN VAR: denetimde çıktı — üye, bakiyesi 0 olsa bile geçiyordu ve düşüm sıfırda
 * duruyordu. Yani "üye = SINIRSIZ kullanım": tek bir ağır kullanıcı üyelik ücretinin
 * kat kat üstünde maliyet çıkarabilirdi.
 */
const MEMBER_MONTHLY_CAP = Math.max(0, Math.floor(Number(process.env.MEMBER_MONTHLY_CREDITS ?? "20000")));
const PERIOD_MS = 30 * 86_400_000;

/** Yeni kullanıcıya verilen deneme kredisi (0 = kapalı; suistimale açık, bkz. denetim madde 7). */
const SIGNUP_BONUS = Math.max(0, Math.floor(Number(process.env.SIGNUP_BONUS_CREDITS ?? "0")));

const seed = (): Account => ({ credits: SIGNUP_BONUS, plan: "free", spent: 0 });

/** Dönem dolduysa sayacı sıfırlar. 30 günlük kayan pencere (takvim ayı değil). */
function rollPeriod(a: Account): void {
  const started = a.periodStart ? new Date(a.periodStart).getTime() : 0;
  if (!started || Date.now() - started >= PERIOD_MS) {
    a.periodStart = new Date().toISOString();
    a.periodSpent = 0;
  }
}

/** Süresi geçmiş üyeliği düşürür. */
function expireMembership(a: Account): void {
  if (a.plan === "member" && a.until && new Date(a.until).getTime() < Date.now()) {
    a.plan = "free";
    delete a.until;
  }
}

export async function getAccount(userId: string): Promise<Account> {
  const s = await store();
  return await s.update(userId, (a) => { expireMembership(a); rollPeriod(a); }, seed);
}

/** Kredi ekler (ödeme sonrası ya da elle). Negatif verilemez. */
export async function addCredits(userId: string, credits: number): Promise<Account> {
  if (!Number.isFinite(credits) || credits <= 0) throw new Error("Geçersiz kredi miktarı");
  const s = await store();
  return await s.update(userId, (a) => {
    const add = Math.floor(credits);
    // BORÇ MAHSUBU: karşılanamamış kullanım varsa önce ondan düşülür, kalanı bakiyeye
    // yazılır. Aksi halde borç sonsuza dek kayıtta kalır ve hiç tahsil edilmezdi.
    const off = Math.min(a.debt ?? 0, add);
    if (off > 0) a.debt = (a.debt ?? 0) - off;
    a.credits += add - off;
  }, seed);
}

/** Üyelik verir/uzatır. */
export async function setMembership(userId: string, days: number): Promise<Account> {
  const s = await store();
  return await s.update(userId, (a) => {
    const base = a.until && new Date(a.until).getTime() > Date.now() ? new Date(a.until).getTime() : Date.now();
    const wasMember = a.plan === "member";
    a.plan = "member";
    a.until = new Date(base + Math.max(1, Math.floor(days)) * 86_400_000).toISOString();
    // Üyelik YENİ başlıyorsa dönemi sıfırla: testte çıktı ki kullanıcının üye olmadan
    // önce harcadıkları da tavana sayılıyordu, ödeme yapan kişi tavanı dolmuş
    // başlıyordu. Uzatmada sıfırlamıyoruz — her uzatma tavanı yenilerse suistimal olur.
    if (!wasMember) { a.periodStart = new Date().toISOString(); a.periodSpent = 0; }
  }, seed);
}

export interface Gate { allowed: boolean; reason?: string; account: Account }

/**
 * Özelliğe erişim kapısı — İSTEKTEN ÖNCE çağrılır.
 *
 * @param usingOwnKey kullanıcının kendi anahtarı olduğuna dair ÖN TAHMİN.
 *        Nihai muhasebe kararı isteği çalıştıran katmanın `pooled` bilgisidir.
 */
export async function checkAccess(userId: string, feature: Feature, usingOwnKey: boolean): Promise<Gate> {
  const account = await getAccount(userId);

  // Kendi anahtarıyla çalışan Kod Ajanı bedava — bizim kaynağımızı kullanmıyor.
  if (!isPaidFeature(feature) && usingOwnKey) return { allowed: true, account };

  // Üyelik sınırsız değil: aylık tavan dolduysa ve ek kredi de yoksa durdur.
  if (account.plan === "member" && MEMBER_MONTHLY_CAP > 0 &&
      (account.periodSpent ?? 0) >= MEMBER_MONTHLY_CAP && account.credits <= 0) {
    return {
      allowed: false,
      reason: "Bu ayki üyelik kullanım tavanına ulaştın. Ek kredi yükleyerek devam edebilirsin.",
      account,
    };
  }

  if (account.plan !== "member" && account.credits <= 0)
    return {
      allowed: false,
      reason: isPaidFeature(feature)
        ? "Bu özellik Nova üyeliği gerektirir (bizim model sunucularımızı kullanır)."
        : "Kredin bitti. Kendi API anahtarını bağlayarak ücretsiz devam edebilirsin.",
      account,
    };

  return { allowed: true, account };
}

/**
 * Kullanım sonrası kredi düşer — ATOMİK.
 *
 * Bakiye eksiye inmez (kullanıcıya eksi bakiye göstermeyiz) ama karşılanamayan kısım
 * `debt` olarak kaydedilir; eskiden sessizce siliniyordu ve zarar ölçülemiyordu.
 */
export async function chargeUsd(userId: string, usd: number): Promise<Account> {
  const c = Math.max(0, Math.round((Number.isFinite(usd) ? usd : 0) * CREDITS_PER_USD));
  const s = await store();
  let shortfall = 0;
  const a = await s.update(userId, (acc) => {
    rollPeriod(acc);
    if (c === 0) return;
    shortfall = Math.max(0, c - acc.credits);
    acc.credits = Math.max(0, acc.credits - c);
    acc.spent += c;
    acc.periodSpent = (acc.periodSpent ?? 0) + c;
    if (shortfall > 0) acc.debt = (acc.debt ?? 0) + shortfall;
  }, seed);
  // Log transaction DIŞINDA: mutate yeniden denenebilir, orada log atmak yanıltır.
  if (shortfall > 0)
    console.warn(`[BILLING] karşılanamayan kullanım user=${userId} eksik=${shortfall} kredi (toplam borç=${a.debt})`);
  return a;
}

/**
 * Bu isteğin harcayabileceği ÜST SINIR (USD) — akış ortasında kesme için.
 *
 * Kapı yalnızca istek BAŞINDA bakıyordu; 1 kredisi olan kullanıcı uzun bir akışla
 * bakiyesinin kat kat üstünde harcayabiliyordu.
 */
export async function requestBudgetUsd(userId: string): Promise<number> {
  const a = await getAccount(userId);
  let credits = a.credits;
  if (a.plan === "member" && MEMBER_MONTHLY_CAP > 0)
    credits += Math.max(0, MEMBER_MONTHLY_CAP - (a.periodSpent ?? 0));
  // Küçük tolerans: son parça yarıda kesilmesin, cümle bitebilsin.
  return (credits / CREDITS_PER_USD) * 1.05;
}

/** Teşhis: defter nerede tutuluyor. */
export function creditsFile(): string { return storeInfo().location; }
export { storeInfo };
