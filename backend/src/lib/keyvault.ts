import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

/**
 * BYO API ANAHTAR KASASI — kalıcı + şifreli.
 *
 * Mimari kararı (2026-07): beta'da backend KULLANICININ KENDİ makinesinde çalışır.
 * Bu yüzden anahtarlar kullanıcının kendi diskinde, kendi makinesine özgü bir sırla
 * şifreli durur. Anahtarlar hiçbir zaman ağa çıkmaz, loglanmaz, repoya girmez.
 *
 * Eskiden burada `new Map()` vardı: sunucu her yeniden başladığında tüm anahtarlar
 * siliniyordu ve şifreleme sırrı repoda yazılı "dev-only-insecure-secret" idi.
 *
 * Dosya: <veri dizini>/vault.json   (0600 izinli)
 * Sır:   <veri dizini>/vault.key    (0600 izinli, ilk çalıştırmada rastgele üretilir)
 *
 * Veri dizini sırası:  NOVA_DATA_DIR env  →  ~/.nova
 * KEYVAULT_SECRET env verilmişse dosya sırrı yerine o kullanılır (CI/konteyner için).
 */

// ---------------------------------------------------------------- konum

function dataDir(): string {
  const custom = process.env.NOVA_DATA_DIR;
  const dir = custom && custom.trim() ? custom.trim() : path.join(os.homedir(), ".nova");
  fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
  return dir;
}

const DIR = dataDir();
const VAULT_FILE = path.join(DIR, "vault.json");
const KEY_FILE = path.join(DIR, "vault.key");

// ---------------------------------------------------------------- sır

/** Makineye özgü şifreleme sırrı. Yoksa üretir ve 0600 ile saklar. */
function loadOrCreateSecret(): Buffer {
  const fromEnv = process.env.KEYVAULT_SECRET;
  if (fromEnv && fromEnv.trim().length >= 16)
    return crypto.createHash("sha256").update(fromEnv.trim()).digest();

  if (fromEnv && fromEnv.trim().length > 0)
    console.warn("[keyvault] KEYVAULT_SECRET çok kısa (<16 karakter) — yok sayıldı, dosya sırrı kullanılıyor.");

  try {
    if (fs.existsSync(KEY_FILE)) {
      const raw = fs.readFileSync(KEY_FILE, "utf8").trim();
      if (raw.length >= 32) return Buffer.from(raw, "base64");
    }
  } catch { /* düşer → yeniden üret */ }

  const fresh = crypto.randomBytes(32);
  try {
    fs.writeFileSync(KEY_FILE, fresh.toString("base64"), { mode: 0o600 });
    console.log("[keyvault] Yeni şifreleme sırrı üretildi: " + KEY_FILE);
  } catch (e) {
    console.warn("[keyvault] Sır dosyaya yazılamadı, bu oturumluk bellekte tutuluyor: " + String(e));
  }
  return fresh;
}

const SECRET = loadOrCreateSecret();

// ---------------------------------------------------------------- depo

interface StoredKey { iv: string; tag: string; data: string }

/** Kullanıcı ayarları — anahtar DIŞI, şifresiz saklanır (hassas değil). */
export interface UserSettings {
  /** rol → model adı (ör. { brain: "llama-3.3-70b-versatile", code: "deepseek-chat" }) */
  models?: Record<string, string>;
  /** özel OpenAI-uyumlu endpoint tabanı (ör. "https://api.together.xyz/v1") */
  customBaseUrl?: string;
}

interface VaultFile {
  version: number;
  keys: Record<string, StoredKey>;      // "<userId>:<provider>"
  settings: Record<string, UserSettings>; // "<userId>"
}

function emptyVault(): VaultFile { return { version: 1, keys: {}, settings: {} }; }

let vault: VaultFile = load();

function load(): VaultFile {
  try {
    if (!fs.existsSync(VAULT_FILE)) return emptyVault();
    const j = JSON.parse(fs.readFileSync(VAULT_FILE, "utf8"));
    return {
      version: 1,
      keys: j?.keys && typeof j.keys === "object" ? j.keys : {},
      settings: j?.settings && typeof j.settings === "object" ? j.settings : {},
    };
  } catch (e) {
    console.warn("[keyvault] vault.json okunamadı, sıfırdan başlanıyor: " + String(e));
    return emptyVault();
  }
}

function persist(): void {
  try {
    // Atomik yazım: yarı yazılmış dosya kalmasın
    const tmp = VAULT_FILE + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(vault, null, 2), { mode: 0o600 });
    fs.renameSync(tmp, VAULT_FILE);
  } catch (e) {
    console.warn("[keyvault] vault.json yazılamadı: " + String(e));
  }
}

// ---------------------------------------------------------------- anahtarlar

export function saveKey(userId: string, provider: string, apiKey: string): void {
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv("aes-256-gcm", SECRET, iv);
  const enc = Buffer.concat([cipher.update(apiKey, "utf8"), cipher.final()]);
  vault.keys[`${userId}:${provider}`] = {
    iv: iv.toString("base64"),
    tag: cipher.getAuthTag().toString("base64"),
    data: enc.toString("base64"),
  };
  persist();
}

export function getKey(userId: string, provider: string): string | null {
  const rec = vault.keys[`${userId}:${provider}`];
  if (!rec) return null;
  try {
    const decipher = crypto.createDecipheriv("aes-256-gcm", SECRET, Buffer.from(rec.iv, "base64"));
    decipher.setAuthTag(Buffer.from(rec.tag, "base64"));
    return Buffer.concat([
      decipher.update(Buffer.from(rec.data, "base64")),
      decipher.final(),
    ]).toString("utf8");
  } catch {
    // Sır değiştiyse (vault.key silindi/kopyalandı) çözülemez — kayıt işe yaramaz.
    console.warn(`[keyvault] '${provider}' anahtarı çözülemedi (şifreleme sırrı değişmiş olabilir). Yeniden girilmeli.`);
    return null;
  }
}

export function deleteKey(userId: string, provider: string): boolean {
  const k = `${userId}:${provider}`;
  if (!(k in vault.keys)) return false;
  delete vault.keys[k];
  persist();
  return true;
}

/** Hangi sağlayıcılarda anahtar var? Anahtarın KENDİSİ asla dönmez, sadece maskesi. */
export function listKeys(userId: string): { provider: string; hint: string }[] {
  const out: { provider: string; hint: string }[] = [];
  for (const k of Object.keys(vault.keys)) {
    if (!k.startsWith(userId + ":")) continue;
    const provider = k.slice(userId.length + 1);
    const val = getKey(userId, provider);
    out.push({ provider, hint: mask(val) });
  }
  return out.sort((a, b) => a.provider.localeCompare(b.provider));
}

/** "sk-abc…9f" — anahtarı tanımaya yeter, kullanmaya yetmez. */
export function mask(key: string | null): string {
  if (!key) return "";
  if (key.length <= 10) return "•".repeat(key.length);
  return key.slice(0, 4) + "…" + key.slice(-2);
}

// ---------------------------------------------------------------- ayarlar

export function getSettings(userId: string): UserSettings {
  return vault.settings[userId] ?? {};
}

export function saveSettings(userId: string, patch: UserSettings): UserSettings {
  const cur = vault.settings[userId] ?? {};
  const next: UserSettings = {
    models: { ...(cur.models ?? {}), ...(patch.models ?? {}) },
    customBaseUrl: patch.customBaseUrl !== undefined ? patch.customBaseUrl : cur.customBaseUrl,
  };
  // Boş değerleri temizle
  if (next.models && Object.keys(next.models).length === 0) delete next.models;
  if (!next.customBaseUrl) delete next.customBaseUrl;
  vault.settings[userId] = next;
  persist();
  return next;
}

// ---------------------------------------------------------------- çözüm

/**
 * İstek için anahtar çöz.
 *
 * BYO ZORUNLU (2026-07 kararı): kullanıcının anahtarı yoksa NULL döner ve çağıran
 * "Ayarlar'dan anahtar ekle" diyen net bir hata verir. Eskiden burada "mock-key"
 * dönüyordu; bu, sunucu sahibinin havuz anahtarlarını sessizce herkese açıyordu.
 *
 * Havuz (.env anahtarları) yalnızca ALLOW_POOL_KEYS=true ise kullanılır —
 * geliştirme/demo makinesi için bilinçli bir tercih.
 */
export function resolveKey(
  userId: string,
  provider: string,
): { apiKey: string; pooled: boolean } | null {
  const byo = getKey(userId, provider);
  if (byo) return { apiKey: byo, pooled: false };

  if (String(process.env.ALLOW_POOL_KEYS ?? "").toLowerCase() !== "true") return null;

  const POOL_ENV: Record<string, string | undefined> = {
    groq: process.env.GROQ_API_KEY,
    openrouter: process.env.OPENROUTER_API_KEY,
    openai: process.env.OPENAI_API_KEY,
    anthropic: process.env.ANTHROPIC_API_KEY,
    gemini: process.env.GEMINI_API_KEY,
    deepseek: process.env.DEEPSEEK_API_KEY,
    custom: process.env.CUSTOM_API_KEY,
    fal: process.env.FAL_KEY,
    tripo: process.env.TRIPO_API_KEY,
  };
  const pool = POOL_ENV[provider];
  return pool ? { apiKey: pool, pooled: true } : null;
}

/** Kasa dosyalarının yeri — kurulum/teşhis mesajlarında gösterilir. */
export function vaultLocation(): string { return VAULT_FILE; }
