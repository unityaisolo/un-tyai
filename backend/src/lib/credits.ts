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
  a.credits = Math.max(0, a.credits - c);
  a.spent += c;
  persist();
  return { ...a };
}

export function creditsFile(): string { return FILE; }
