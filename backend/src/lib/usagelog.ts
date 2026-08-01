import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import crypto from "node:crypto";
import { store } from "./creditstore.js";

/**
 * İŞLEM DÖKÜMÜ — kullanıcının "ne için ne kadar ödedim?" sorusunun cevabı.
 *
 * NEDEN VAR: kullanım kayıtları bellekte tutuluyordu (`metering.ts` içindeki dizi).
 * Sunucu yeniden başlayınca siliniyorlardı. Yani bir kullanıcı "benden fazla kesildi"
 * dese elimizde HİÇBİR kanıt olmayacaktı ve o kişiye ne olduğunu gösteremeyecektik.
 * Ücretli bir üründe bu, teknik bir eksiklikten çok bir güven sorunudur.
 *
 * TASARIM KURALI — TEK YAZMA NOKTASI: kayıt, paranın hesaptan çıktığı yerde
 * (`gate.charge`) yazılır. Ayrı yerlerde yazılsaydı döküm ile bakiye zamanla
 * birbirini tutmazdı; itiraz anında hangisinin doğru olduğunu bilemezdik.
 *
 * EKLE-ONLY: kayıtlar asla güncellenmez/silinmez. İade bile ayrı bir satırdır.
 * Geçmişi değiştirebilen bir defter, defter değildir.
 */

export type TxKind = "usage" | "topup" | "membership" | "refund";

export interface Tx {
  id: string;
  at: string;           // ISO
  userId: string;
  kind: TxKind;
  /** Neyin kullanıldığı: chat / world / studio / material / 3d … */
  feature?: string;
  model?: string;
  inputTokens?: number;
  outputTokens?: number;
  /** İşlemin tutarı USD (harcamada pozitif, yüklemede pozitif — yön `kind`'den okunur). */
  usd: number;
  /** Kredi karşılığı (1/1000 USD). */
  credits: number;
  /** Bizim havuz anahtarımız mı kullanıldı? */
  pooled?: boolean;
  note?: string;
}

const CREDITS_PER_USD = 1000;

function dataDir(): string {
  const c = process.env.NOVA_DATA_DIR;
  const d = c && c.trim() ? c.trim() : path.join(os.homedir(), ".nova");
  fs.mkdirSync(d, { recursive: true, mode: 0o700 });
  return d;
}

const FILE = path.join(dataDir(), "usage.jsonl");

/** Dosyaya ekle. JSONL: her satır bağımsız, kısmi yazma tüm dosyayı bozmaz. */
function appendFile(tx: Tx): void {
  try {
    fs.appendFileSync(FILE, JSON.stringify(tx) + "\n", { mode: 0o600 });
  } catch (e) {
    console.error("[BILLING] işlem kaydı yazılamadı:", e);
  }
}

/**
 * İşlemi deftere yazar. HATA FIRLATMAZ — kayıt tutulamıyorsa bile kullanıcının
 * isteği bozulmamalı; ama sorun mutlaka log'a düşer.
 */
export async function logTx(input: Omit<Tx, "id" | "at" | "credits"> & { credits?: number }): Promise<Tx> {
  const tx: Tx = {
    id: crypto.randomUUID(),
    at: new Date().toISOString(),
    credits: input.credits ?? Math.round(Math.abs(input.usd) * CREDITS_PER_USD),
    ...input,
  } as Tx;
  try {
    const s = await store();
    if (s.appendTx) await s.appendTx(tx.userId, tx as any);
    else appendFile(tx);
  } catch (e) {
    // Sürücü yazamadıysa kaybetmektense yerele yaz — defterde boşluk olmamalı.
    console.error("[BILLING] işlem kaydı sürücüye yazılamadı, yerele düşülüyor:", e);
    appendFile(tx);
  }
  return tx;
}

/** Kullanıcının son işlemleri (yeniden eskiye). */
export async function readTx(userId: string, limit = 100): Promise<Tx[]> {
  const s = await store();
  if (s.readTx) {
    try { return (await s.readTx(userId, limit)) as Tx[]; }
    catch (e) { console.error("[BILLING] işlem dökümü okunamadı:", e); return []; }
  }
  try {
    if (!fs.existsSync(FILE)) return [];
    // Dosya büyüyebilir; sondan okumak yerine tamamını okuyup filtreliyoruz.
    // Yerel kurulumda hacim küçük olduğu için yeterli; bulutta Firestore kullanılıyor.
    const lines = fs.readFileSync(FILE, "utf8").split("\n").filter(Boolean);
    const out: Tx[] = [];
    for (let i = lines.length - 1; i >= 0 && out.length < limit; i--) {
      try {
        const t = JSON.parse(lines[i]) as Tx;
        if (t.userId === userId) out.push(t);
      } catch { /* bozuk satır tüm dökümü bozmasın */ }
    }
    return out;
  } catch (e) {
    console.error("[BILLING] işlem dökümü okunamadı:", e);
    return [];
  }
}

/** Dökümün özeti — Unity'de tek satırda göstermek için. */
export function summarize(txs: Tx[]): { spentUsd: number; toppedUpUsd: number; count: number } {
  let spentUsd = 0, toppedUpUsd = 0;
  for (const t of txs) {
    if (t.kind === "usage") spentUsd += t.usd;
    else if (t.kind === "topup") toppedUpUsd += t.usd;
    else if (t.kind === "refund") toppedUpUsd += t.usd;
  }
  return { spentUsd: +spentUsd.toFixed(6), toppedUpUsd: +toppedUpUsd.toFixed(6), count: txs.length };
}

export function usageLogLocation(): string { return FILE; }
