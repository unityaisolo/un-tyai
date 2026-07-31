import type { Request, Response, NextFunction } from "express";
import { verifyIdToken } from "../lib/idtoken.js";

// MVP+ auth. Gerçek JWT/OAuth (Firebase) Faz 5'te. Şimdilik iki mod:
//
// 1) Açık dev modu (API_TOKENS boş — varsayılan):
//    Authorization header'daki token userId sayılır, yoksa "demo-user".
//    UYARI: Kimlik taklidi mümkündür — yalnız yerel geliştirme için.
//
// 2) Kilitli mod (API_TOKENS dolu — kapalı beta / küçük ekip):
//    Yalnız listedeki Bearer token'lar kabul edilir, eşleşen userId atanır; aksi 401.
//    Biçim: "token1:kullanici1,token2:kullanici2" (userId verilmezse token userId olur).
//
// Ek: RATE_LIMIT_PER_MIN > 0 ise kullanıcı+IP başına dakikalık in-memory istek limiti.

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Express {
    interface Request {
      userId: string;
      /** true = kimliği Firebase ile DOĞRULANMIŞ (üyelik/kredi bu şarta bağlı) */
      authed?: boolean;
    }
  }
}

function parseTokens(s: string | undefined): Map<string, string> | null {
  if (!s || !s.trim()) return null;
  const m = new Map<string, string>();
  for (const part of s.split(",")) {
    const [tok, uid] = part.trim().split(":");
    if (tok) m.set(tok, uid || tok);
  }
  return m.size > 0 ? m : null;
}

const TOKENS = parseTokens(process.env.API_TOKENS);
const RATE = Number(process.env.RATE_LIMIT_PER_MIN ?? "0");

const buckets = new Map<string, { count: number; reset: number }>();

const FIREBASE_PROJECT = process.env.FIREBASE_PROJECT_ID ?? "";

/** Firebase ID token'ı 3 noktalı bir JWT'dir; düz token'dan böyle ayırt ediyoruz. */
function looksLikeJwt(t: string): boolean {
  return t.split(".").length === 3 && t.length > 100;
}

export async function auth(req: Request, res: Response, next: NextFunction): Promise<void> {
  // Sağlık kontrolü her zaman açık (izleme/load balancer için)
  if (req.path === "/health") { req.userId = "health"; next(); return; }

  const header = req.header("authorization");
  const token = header?.replace(/^Bearer\s+/i, "").trim() ?? "";

  // ── MOD 3: Firebase kimliği (bulut/üyelik senaryosu)
  // FIREBASE_PROJECT_ID tanımlıysa ve gelen değer JWT ise doğrulanır.
  // Doğrulanmış uid gerçek kullanıcı kimliği olur — kimlik taklidi mümkün değildir.
  if (FIREBASE_PROJECT && looksLikeJwt(token)) {
    try {
      const u = await verifyIdToken(token, FIREBASE_PROJECT);
      req.userId = u.uid;
      req.authed = true;
      applyRateLimit(req, res, next);
      return;
    } catch (e) {
      res.status(401).json({ error: "Oturum geçersiz veya süresi geçmiş — tekrar giriş yap." });
      return;
    }
  }

  if (TOKENS) {
    const uid = token ? TOKENS.get(token) : undefined;
    if (!uid) {
      res.status(401).json({ error: "Geçersiz veya eksik API token (API_TOKENS kilidi açık)" });
      return;
    }
    req.userId = uid;
  } else {
    req.userId = token.length > 0 ? token : "demo-user";
  }

  applyRateLimit(req, res, next);
}

/** Dakikalık istek limiti (kullanıcı+IP). Auth modlarının hepsi bunu kullanır. */
function applyRateLimit(req: Request, res: Response, next: NextFunction): void {
  if (RATE > 0) {
    const now = Date.now();
    const key = `${req.userId}|${req.ip ?? ""}`;
    const b = buckets.get(key);
    if (!b || now > b.reset) {
      buckets.set(key, { count: 1, reset: now + 60_000 });
      // Kova haritası sınırsız büyümesin
      if (buckets.size > 10_000)
        for (const [k, v] of buckets) if (now > v.reset) buckets.delete(k);
    } else if (++b.count > RATE) {
      res.status(429).json({ error: "Rate limit aşıldı — bir dakika sonra tekrar dene" });
      return;
    }
  }
  next();
}
