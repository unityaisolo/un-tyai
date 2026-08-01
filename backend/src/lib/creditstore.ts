/**
 * KREDİ DEPOSU — sürücü katmanı.
 *
 * NEDEN AYRI KATMAN: kredi mantığı ile saklama biçimi birbirine karışmıştı; defter
 * doğrudan dosyaya yazıyordu. Bu tek süreçte çalışıyor ama Cloud Run otomatik
 * ölçeklendiğinde bozuluyor (bkz. docs/KREDI-DENETIMI.md, madde 5).
 *
 * Sözleşmenin can alıcı noktası `update()`: oku-değiştir-yaz tek bir ATOMİK işlem
 * olmalı. Firestore sürücüsü bunu transaction ile sağlar; dosya sürücüsü tek süreç
 * varsayımıyla sağlar.
 *
 * Sürücü seçimi: NOVA_CREDIT_STORE = "file" | "firestore" (varsayılan: bulutta
 * firestore, yerelde file).
 */

export interface Account {
  credits: number;
  plan: "free" | "member";
  until?: string;
  spent: number;
  debt?: number;
  periodStart?: string;
  periodSpent?: number;
}

export interface CreditStore {
  readonly kind: string;
  /** Hesabı okur; yoksa null. */
  get(userId: string): Promise<Account | null>;
  /**
   * Hesabı ATOMİK olarak değiştirir. `mutate` çağrıldığında elindeki nesne o anki
   * gerçek durumdur; dönüşünde yazılır. Firestore'da çakışma olursa yeniden denenir,
   * bu yüzden `mutate` YAN ETKİSİZ olmalı (log/istek atmamalı).
   */
  update(userId: string, mutate: (a: Account) => void, seed: () => Account): Promise<Account>;

  /**
   * İŞLEM DÖKÜMÜ (opsiyonel). Sürücü destekliyorsa kayıtları kendi deposunda tutar;
   * desteklemiyorsa usagelog.ts yerel JSONL dosyasına düşer.
   * Sürücünün iç alanlarına dışarıdan uzanmamak için sözleşmeye alındı.
   */
  appendTx?(userId: string, tx: unknown & { id: string }): Promise<void>;
  readTx?(userId: string, limit: number): Promise<unknown[]>;
}

// ---------------------------------------------------------------- dosya sürücüsü

import fs from "node:fs";
import path from "node:path";
import os from "node:os";

function dataDir(): string {
  const c = process.env.NOVA_DATA_DIR;
  const d = c && c.trim() ? c.trim() : path.join(os.homedir(), ".nova");
  fs.mkdirSync(d, { recursive: true, mode: 0o700 });
  return d;
}

class FileStore implements CreditStore {
  readonly kind = "file";
  private file = path.join(dataDir(), "credits.json");
  private accounts: Record<string, Account>;

  constructor() {
    this.accounts = this.load();
  }

  private load(): Record<string, Account> {
    try {
      if (!fs.existsSync(this.file)) return {};
      const j = JSON.parse(fs.readFileSync(this.file, "utf8"));
      return j?.accounts && typeof j.accounts === "object" ? j.accounts : {};
    } catch (e) {
      console.warn("[credits] credits.json okunamadı, sıfırdan başlanıyor: " + String(e));
      return {};
    }
  }

  private persist(): void {
    try {
      const tmp = this.file + ".tmp";
      fs.writeFileSync(tmp, JSON.stringify({ version: 1, accounts: this.accounts }, null, 2), { mode: 0o600 });
      fs.renameSync(tmp, this.file);
    } catch (e) { console.warn("[credits] yazılamadı: " + String(e)); }
  }

  async get(userId: string): Promise<Account | null> {
    const a = this.accounts[userId];
    return a ? { ...a } : null;
  }

  /**
   * Tek süreçte atomik: olay döngüsü tek iş parçacıklı ve burada `await` yok, yani
   * oku-değiştir-yaz araya başka isteğin girmesiyle bölünemez.
   */
  async update(userId: string, mutate: (a: Account) => void, seed: () => Account): Promise<Account> {
    let a = this.accounts[userId];
    if (!a) { a = seed(); this.accounts[userId] = a; }
    mutate(a);
    this.persist();
    return { ...a };
  }

  file_(): string { return this.file; }
}

// ------------------------------------------------------------ firestore sürücüsü

/**
 * Firestore sürücüsü. `@google-cloud/firestore` DİNAMİK yüklenir: yerel kurulumdaki
 * kullanıcı bu paketi kurmak zorunda kalmasın (bulutta anlamlı, yerelde gereksiz).
 *
 * Cloud Run'da kimlik doğrulama Application Default Credentials ile otomatiktir;
 * ekstra anahtar dosyası GEREKMEZ (ve olmamalı — anahtar dosyası imaja gömülmemeli).
 */
class FirestoreStore implements CreditStore {
  readonly kind = "firestore";
  private db: any;
  private col: string;

  private constructor(db: any, col: string) { this.db = db; this.col = col; }

  static async create(): Promise<FirestoreStore> {
    let mod: any;
    try {
      // Değişken üzerinden: paket yalnızca bulutta kurulu olacak, TypeScript yerelde
      // çözmeye çalışıp derlemeyi kırmasın.
      const spec = "@google-cloud/firestore";
      mod = await import(/* @vite-ignore */ spec);
    } catch {
      throw new Error(
        "Firestore sürücüsü seçildi ama '@google-cloud/firestore' kurulu değil.\n" +
        "  npm i @google-cloud/firestore",
      );
    }
    const Firestore = mod.Firestore ?? mod.default?.Firestore ?? mod.default;
    const db = new Firestore({ projectId: process.env.GOOGLE_CLOUD_PROJECT || undefined });
    return new FirestoreStore(db, process.env.NOVA_CREDIT_COLLECTION ?? "novaCredits");
  }

  async get(userId: string): Promise<Account | null> {
    const snap = await this.db.collection(this.col).doc(userId).get();
    return snap.exists ? (snap.data() as Account) : null;
  }

  /**
   * Firestore transaction: okuma ve yazma tek bir atomik birimde. Çakışma olursa
   * Firestore işlemi otomatik yeniden dener — bu yüzden `mutate` saf olmalı.
   * Böylece iki eşzamanlı istek aynı bakiyeyi okuyup üzerine yazamaz.
   */
  async appendTx(userId: string, tx: any): Promise<void> {
    await this.db.collection(this.col).doc(userId).collection("tx").doc(tx.id).set(tx);
  }

  async readTx(userId: string, limit: number): Promise<unknown[]> {
    const snap = await this.db.collection(this.col).doc(userId).collection("tx")
      .orderBy("at", "desc").limit(limit).get();
    return snap.docs.map((d: any) => d.data());
  }

  async update(userId: string, mutate: (a: Account) => void, seed: () => Account): Promise<Account> {
    const ref = this.db.collection(this.col).doc(userId);
    return await this.db.runTransaction(async (tx: any) => {
      const snap = await tx.get(ref);
      const a: Account = snap.exists ? (snap.data() as Account) : seed();
      mutate(a);
      tx.set(ref, a);
      return { ...a };
    });
  }
}

// ---------------------------------------------------------------------- seçim

let _store: CreditStore | null = null;

function wanted(): "file" | "firestore" {
  const v = (process.env.NOVA_CREDIT_STORE ?? "").trim().toLowerCase();
  if (v === "file" || v === "firestore") return v;
  return String(process.env.NOVA_CLOUD ?? "").toLowerCase() === "true" ? "firestore" : "file";
}

export async function store(): Promise<CreditStore> {
  if (_store) return _store;
  const k = wanted();

  // TEHLİKELİ KOMBİNASYON: bulut modu + dosya defteri. Cloud Run otomatik ölçeklenir;
  // her örneğin kendi bellek kopyası olur, persist() son yazan kazanır ve düşümler
  // kaybolur — kullanıcı paralel istekle ücretlendirmeyi atlatabilir. Üstelik Cloud
  // Run diski kalıcı değil, örnek kapanınca defter tamamen gider.
  if (k === "file" && String(process.env.NOVA_CLOUD ?? "").toLowerCase() === "true" &&
      String(process.env.NOVA_ALLOW_FILE_LEDGER ?? "").toLowerCase() !== "true") {
    throw new Error(
      "NOVA_CLOUD=true iken dosya tabanlı kredi defteri kullanılamaz — çok örnekli " +
      "çalışmada düşümler kaybolur. NOVA_CREDIT_STORE=firestore kullan. " +
      "(Tek örnekli denemede: NOVA_ALLOW_FILE_LEDGER=true)",
    );
  }

  _store = k === "firestore" ? await FirestoreStore.create() : new FileStore();
  console.log(`[kredi] defter sürücüsü: ${_store.kind} · ${storeInfo().location}`);
  return _store;
}

/** Teşhis: hangi sürücü etkin ve defter nerede. */
export function storeInfo(): { kind: string; location: string } {
  const k = wanted();
  return k === "firestore"
    ? { kind: "firestore", location: `collection: ${process.env.NOVA_CREDIT_COLLECTION ?? "novaCredits"}` }
    : { kind: "file", location: path.join(dataDir(), "credits.json") };
}
