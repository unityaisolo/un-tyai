import crypto from "node:crypto";

/**
 * FIREBASE ID TOKEN DOĞRULAMA — sıfır yeni bağımlılık.
 *
 * NEDEN firebase-admin YOK: paket eklemek kullanıcının `npm install` çalıştırmasını
 * zorunlu kılar ve paket ~50 MB bağımlılık getirir. Firebase ID token'ı standart bir
 * RS256 JWT'dir; Google imza sertifikalarını herkese açık bir adreste yayınlar.
 * Bu yüzden doğrulamayı node:crypto ile kendimiz yapıyoruz.
 *
 * DOĞRULANAN ŞEYLER (hepsi zorunlu — biri eksikse token reddedilir):
 *   1) İmza  — Google'ın x509 sertifikalarıyla RS256
 *   2) alg   — RS256 (alg=none / HS256 saldırısına kapalı)
 *   3) iss   — https://securetoken.google.com/<projectId>
 *   4) aud   — <projectId>
 *   5) exp   — süresi geçmemiş
 *   6) iat   — gelecekte değil (60 sn tolerans)
 *   7) sub   — boş olmayan kullanıcı kimliği
 */

const CERT_URL =
  "https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com";

let certs: Record<string, string> = {};
let certsExpire = 0;

async function getCerts(): Promise<Record<string, string>> {
  if (Date.now() < certsExpire && Object.keys(certs).length > 0) return certs;

  const ctl = new AbortController();
  const t = setTimeout(() => ctl.abort(), 10_000);
  try {
    const r = await fetch(CERT_URL, { signal: ctl.signal });
    if (!r.ok) throw new Error("sertifika alınamadı: " + r.status);
    certs = (await r.json()) as Record<string, string>;

    // Cache-Control: max-age'e uy; yoksa 1 saat.
    const cc = r.headers.get("cache-control") ?? "";
    const m = cc.match(/max-age=(\d+)/);
    certsExpire = Date.now() + (m ? Number(m[1]) : 3600) * 1000;
    return certs;
  } finally { clearTimeout(t); }
}

function b64urlToBuf(s: string): Buffer {
  return Buffer.from(s.replace(/-/g, "+").replace(/_/g, "/"), "base64");
}

export interface VerifiedUser {
  uid: string;
  email?: string;
  emailVerified: boolean;
}

/**
 * Token'ı doğrular. Geçersizse hata FIRLATIR (çağıran 401 döner).
 * @param projectId Firebase proje kimliği (env: FIREBASE_PROJECT_ID)
 */
export async function verifyIdToken(token: string, projectId: string): Promise<VerifiedUser> {
  if (!projectId) throw new Error("FIREBASE_PROJECT_ID tanımlı değil");

  const parts = token.split(".");
  if (parts.length !== 3) throw new Error("token biçimi geçersiz");

  const header = JSON.parse(b64urlToBuf(parts[0]).toString("utf8"));
  const payload = JSON.parse(b64urlToBuf(parts[1]).toString("utf8"));

  // 2) Algoritma sabit: "none"/HS256 ile imza atlatma denemesini engeller
  if (header.alg !== "RS256") throw new Error("beklenmeyen imza algoritması");
  if (!header.kid) throw new Error("kid yok");

  const all = await getCerts();
  const cert = all[header.kid];
  if (!cert) throw new Error("imza anahtarı bulunamadı (kid)");

  // 1) İmza doğrulama
  const ok = crypto
    .createVerify("RSA-SHA256")
    .update(parts[0] + "." + parts[1])
    .verify(cert, b64urlToBuf(parts[2]));
  if (!ok) throw new Error("imza doğrulanamadı");

  // 3-4) Kime ve kim tarafından verildiği
  if (payload.iss !== `https://securetoken.google.com/${projectId}`) throw new Error("iss uyuşmuyor");
  if (payload.aud !== projectId) throw new Error("aud uyuşmuyor");

  // 5-6) Zaman
  const now = Math.floor(Date.now() / 1000);
  if (typeof payload.exp !== "number" || payload.exp <= now) throw new Error("token süresi geçmiş");
  if (typeof payload.iat !== "number" || payload.iat > now + 60) throw new Error("iat gelecekte");

  // 7) Kimlik
  const uid = String(payload.sub ?? payload.user_id ?? "");
  if (!uid) throw new Error("kullanıcı kimliği yok");

  return {
    uid,
    email: typeof payload.email === "string" ? payload.email : undefined,
    emailVerified: payload.email_verified === true,
  };
}
