import fs from "node:fs";
import path from "node:path";
import os from "node:os";

/**
 * ÜYELİK + KREDİ DEFTERİ.
 *
 * ÜRÜN KURALI (2026-07-30 kararı):
 *   • Kod Ajanı  → kullanıcı KENDİ anahtarını bağlarsa bedava; bağlamazsa kredi harcar.
 *   • 3D Stüdyo / Malzeme / Dünya → SADECE Nova kredisiyle. Bunlar bizim havuz
 *     anahtarlarımızı kullanır, o yüzden kredisiz erişim yok.
 *
 * Kredi birimi: 1 kredi = 0.001 USD ("mil"). Tam sayı tutuluyor ki kayan nokta
 * yuvarlama hatası bakiyeyi aşındırmasın.
 *
 * Depolama: NOVA_DATA_DIR (varsayılan ~/.nova) altında credits.json, 0600.
 * Bu dosya BULUT sunucusunda anlamlıdır; yerel kurulumda kredi kullanılmaz.
 */

export const CREDITS_PER_USD = 1000;

export type Feature = "chat" | "world" | "studio" | "material";

/** Kredi gerektiren (bizim anahtarlarımızı kullanan) özellikler. */
const PAID_FEATURES: Feature[] = ["world", "studio", "material"];

export function isPaidFeature(f: string): boolean {
  return (PAID_FEATURES as string[]).includes(f);
}

interface Account {
  /** Kalan kredi (tam sayı, 1/1000 USD) */
  credits: number;
  plan: "free" | "member";
  /** ISO tarih — üyelik bitişi (yoksa süresiz/serbest) */
  until?: string;
  /** Toplam harcanan kredi (raporlama) */
  spent: number;
  /** Bakiye yetmediği için karşılanamayan kredi (bkz. chargeUsd). Bir sonraki
   *  yüklemede mahsup edilmeli; şu an yalnızca kaydediliyor ve görünür kılınıyor. */
  debt?: number;
}

interface Ledger { version: number; accounts: Record<string, Account> }

function dir(): string {
  const c = process.env.NOVA_DATA_DIR;
  const d = c && c.trim() ? c.trim() : path.join(os.homedir(), ".nova");
  fs.mkdirSync(d, { recursive: true, mode: 0o700 });
  return d;
}

const FILE = path.join(dir(), "credits.json");

function load(): Ledger {
  try {
    if (!fs.existsSync(FILE)) return { version: 1, accounts: {} };
    const j = JSON.parse(fs.readFileSync(FILE, "utf8"));
    return { version: 1, accounts: j?.accounts && typeof j.accounts === "object" ? j.accounts : {} };
  } catch (e) {
    console.warn("[credits] credits.json okunamadı, sıfırdan başlanıyor: " + String(e));
    return { version: 1, accounts: {} };
  }
}

let ledger: Ledger = load();

/**
 * ⚠ TEK SÜREÇ VARSAYIMI — CANLIYA ÇIKMADAN ÖNCE OKU.
 *
 * Bu defter süreç belleğinde tutulur ve her değişiklikte dosyanın TAMAMI yeniden
 * yazılır. Tek süreçte güvenlidir: JavaScript olay döngüsü tek iş parçacıklı olduğu
 * ve oku-değiştir-yaz arasında `await` bulunmadığı için düşüm atomiktir.
 *
 * ÇOK SÜREÇLİ/ÇOK ÖRNEKLİ ÇALIŞMADA BOZULUR — ve Cloud Run varsayılan olarak
 * otomatik ölçeklenir:
 *   • Her örneğin kendi bellek kopyası olur; biri diğerinin düşümünü görmez.
 *   • persist() dosyanın tamamını yazar → son yazan kazanır, aradaki düşümler yok olur.
 *   • Sonuç: kullanıcı iki örneğe paralel istek atarak ücretlendirmeyi atlatabilir.
 *   • Cloud Run'ın diski kalıcı da değildir; örnek kapanınca defter tamamen kaybolur.
 *
 * Bu yüzden NOVA_CLOUD=true + dosya defteri kombinasyonu üretim için GEÇERSİZDİR.
 * Gereken: transaction destekli gerçek veritabanı (Firestore transaction / Postgres
 * `UPDATE … SET credits = credits - $1 WHERE credits >= $1` gibi atomik düşüm).
 */
if (String(process.env.NOVA_CLOUD ?? "").toLowerCase() === "true" &&
    String(process.env.NOVA_ALLOW_FILE_LEDGER ?? "").toLowerCase() !== "true") {
  console.error(
    "\n[KREDİ] ÖLÜMCÜL YAPILANDIRMA: NOVA_CLOUD=true ama kredi defteri dosyada tutuluyor.\n" +
    "  Çok örnekli çalışmada düşümler kaybolur ve ücretlendirme atlatılabilir.\n" +
    "  Üretimde transaction destekli bir veritabanı kullan.\n" +
    "  Yalnızca TEK örnekli denemede: NOVA_ALLOW_FILE_LEDGER=true ile bu kontrolü kapatabilirsin.\n",
  );
  throw new Error("Bulut modunda dosya tabanlı kredi defteri kullanılamaz.");
}

function persist(): void {
  try {
    const tmp = FILE + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(ledger, null, 2), { mode: 0o600 });
    fs.renameSync(tmp, FILE);
  } catch (e) { console.warn("[credits] yazılamadı: " + String(e)); }
}

/** Yeni kullanıcıya verilen deneme kredisi (env ile ayarlanır, 0 = kapalı). */
const SIGNUP_BONUS = Math.max(0, Math.floor(Number(process.env.SIGNUP_BONUS_CREDITS ?? "0")));

function ensure(userId: string): Account {
  let a = ledger.accounts[userId];
  if (!a) {
    a = { credits: SIGNUP_BONUS, plan: "free", spent: 0 };
    ledger.accounts[userId] = a;
    persist();
  }
  return a;
}

export function getAccount(userId: string): Account {
  const a = ensure(userId);
  // Süresi geçmiş üyelik otomatik düşer
  if (a.plan === "member" && a.until && new Date(a.until).getTime() < Date.now()) {
    a.plan = "free";
    delete a.until;
    persist();
  }
  return { ...a };
}

/** Kredi ekler (ödeme sonrası ya da elle). Negatif verilemez. */
export function addCredits(userId: string, credits: number): Account {
  if (!Number.isFinite(credits) || credits <= 0) throw new Error("Geçersiz kredi miktarı");
  const a = ensure(userId);
  a.credits += Math.floor(credits);
  persist();
  return { ...a };
}

/** Üyelik verir/uzatır. */
export function setMembership(userId: string, days: number): Account {
  const a = ensure(userId);
  const base = a.until && new Date(a.until).getTime() > Date.now() ? new Date(a.until).getTime() : Date.now();
  a.plan = "member";
  a.until = new Date(base + Math.max(1, Math.floor(days)) * 86_400_000).toISOString();
  persist();
  return { ...a };
}

export interface Gate { allowed: boolean; reason?: string; account: Account }

/**
 * Özelliğe erişim kapısı — İSTEKTEN ÖNCE çağrılır.
 *
 * @param usingOwnKey kullanıcı kendi API anahtarını mı kullanıyor?
 *        Kod Ajanı'nda kendi anahtarı varsa kredi harcanmaz.
 */
export function checkAccess(userId: string, feature: Feature, usingOwnKey: boolean): Gate {
  const account = getAccount(userId);

  // Kendi anahtarıyla çalışan Kod Ajanı bedava — bizim kaynağımızı kullanmıyor.
  if (!isPaidFeature(feature) && usingOwnKey) return { allowed: true, account };

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
 * Kullanım sonrası kredi düşer. Üyelikte de düşer (adil kullanım için) ama
 * bakiye eksiye inmez — sıfırda durur ve bir sonraki kapı kontrolü engeller.
 */
export function chargeUsd(userId: string, usd: number): Account {
  const a = ensure(userId);
  const c = Math.max(0, Math.round((Number.isFinite(usd) ? usd : 0) * CREDITS_PER_USD));
  if (c === 0) return { ...a };

  // BORÇ GÖRÜNÜR OLSUN: bakiye sıfırda durur (kullanıcıya eksi bakiye göstermeyiz)
  // ama karşılanamayan kısım ayrıca kaydedilir. Eskiden fazlası sessizce siliniyordu;
  // tek bir uzun akış bakiyeden ÇOK fazlasını harcayıp iz bırakmıyordu.
  const short = Math.max(0, c - a.credits);
  a.credits = Math.max(0, a.credits - c);
  a.spent += c;
  if (short > 0) {
    a.debt = (a.debt ?? 0) + short;
    console.warn(`[BILLING] karşılanamayan kullanım user=${userId} eksik=${short} kredi (toplam borç=${a.debt})`);
  }
  persist();
  return { ...a };
}

export function creditsFile(): string { return FILE; }
