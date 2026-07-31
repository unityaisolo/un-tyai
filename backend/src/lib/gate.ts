import type { Request, Response } from "express";
import { checkAccess, chargeUsd, isPaidFeature, type Feature } from "./credits.js";
import { getKey } from "./keyvault.js";

/**
 * ÖZELLİK KAPISI — bulut kaynaklarını kullanan uçların ÖNÜNDE çağrılır.
 *
 * ÜRÜN KURALI:
 *   • world / studio / material → SADECE Nova kredisi (bizim havuz anahtarlarımız)
 *   • chat                      → kullanıcının kendi anahtarı varsa bedava,
 *                                 yoksa kredi harcar
 *
 * Yerel kurulumda (NOVA_CLOUD != true) kapı AÇIKTIR: kullanıcı kendi makinesinde
 * kendi anahtarıyla çalışır, kredi kavramı yoktur.
 */

const CLOUD_MODE = String(process.env.NOVA_CLOUD ?? "").toLowerCase() === "true";

/** Kullanıcının bu istek için kendi anahtarı var mı? */
function hasOwnKey(userId: string): boolean {
  for (const p of ["groq", "openrouter", "openai", "anthropic", "gemini", "deepseek", "custom"])
    if (getKey(userId, p)) return true;
  return false;
}

export interface GateResult { ok: boolean; usingOwnKey: boolean }

/**
 * Erişimi denetler. Reddedilirse yanıtı KENDİSİ yazar (402) ve ok:false döner.
 */
export function gate(req: Request, res: Response, feature: Feature): GateResult {
  const usingOwnKey = hasOwnKey(req.userId);

  // Yerel mod: kredi yok, kapı açık.
  if (!CLOUD_MODE) return { ok: true, usingOwnKey };

  // Bulutta kimlik doğrulanmış olmalı — anonim kullanıcıya kaynak vermiyoruz.
  if (req.authed !== true) {
    res.status(401).json({
      error: "Bu özellik için Nova hesabına giriş yapmalısın.",
      needsLogin: true,
    });
    return { ok: false, usingOwnKey };
  }

  const g = checkAccess(req.userId, feature, usingOwnKey);
  if (!g.allowed) {
    res.status(402).json({
      error: g.reason,
      needsCredits: true,
      needsMembership: isPaidFeature(feature),
      credits: g.account.credits,
      plan: g.account.plan,
    });
    return { ok: false, usingOwnKey };
  }
  return { ok: true, usingOwnKey };
}

/**
 * Kullanım sonrası ücretlendirir. Kullanıcı kendi anahtarını kullandıysa
 * (pooled=false) hiçbir şey düşülmez — bizim kaynağımız harcanmadı.
 */
export function charge(userId: string, usd: number, pooled: boolean): void {
  if (!CLOUD_MODE || !pooled) return;
  try { chargeUsd(userId, usd); } catch { /* ücretlendirme isteği bozmasın */ }
}

export function cloudMode(): boolean { return CLOUD_MODE; }
