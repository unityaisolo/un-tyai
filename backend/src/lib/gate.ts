import type { Request, Response } from "express";
import { checkAccess, chargeUsd, isPaidFeature, requestBudgetUsd, type Feature } from "./credits.js";
import { getKey } from "./keyvault.js";
import { logTx } from "./usagelog.js";

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

/**
 * Kullanıcının bu istek için kendi anahtarı var mı?
 *
 * ⚠ BU YALNIZCA BİR ÖN TAHMİNDİR, muhasebe kararı DEĞİLDİR.
 *
 * AÇIK (denetimde bulundu): eskiden `gate()` bu tahmine bakıp "kendi anahtarı var →
 * bedava" diyordu. Ama kullanıcının kayıtlı anahtarı isteğin GERÇEKTEN kullandığı
 * sağlayıcıya ait olmayabilir. Örnek: kullanıcı bir `custom` anahtar kaydeder, sonra
 * bizim havuzumuza düşen bir modele istek atar → kapı "bedava" der, istek BİZİM
 * anahtarımızla çalışır. Bakiye `Math.max(0, …)` ile sıfırda durduğu için borç da
 * birikmez: sınırsız bedava kullanım.
 *
 * Doğrusu: ücretlendirme kararı, isteği çalıştıran katmanın döndürdüğü `pooled`
 * bilgisine dayanır (bkz. resolveTarget / charge). Kapı yalnızca "hiç anahtarı yok ve
 * kredisi de yok" durumunu erkenden eler.
 */
function hasAnyOwnKey(userId: string): boolean {
  for (const p of ["groq", "openrouter", "openai", "anthropic", "gemini", "deepseek", "custom"])
    if (getKey(userId, p)) return true;
  return false;
}

export interface GateResult { ok: boolean; usingOwnKey: boolean }

/**
 * Erişimi denetler. Reddedilirse yanıtı KENDİSİ yazar (402) ve ok:false döner.
 */
export async function gate(req: Request, res: Response, feature: Feature): Promise<GateResult> {
  const usingOwnKey = hasAnyOwnKey(req.userId);

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

  const g = await checkAccess(req.userId, feature, usingOwnKey);
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
export interface ChargeDetail {
  feature: string;
  model?: string;
  inputTokens?: number;
  outputTokens?: number;
}

/**
 * Ücretlendirir VE işlem dökümüne yazar.
 *
 * İkisi TEK YERDE: ayrı çağrılsalardı biri başarılı diğeri başarısız olduğunda döküm
 * ile bakiye birbirini tutmazdı ve itiraz anında hangisinin doğru olduğunu bilemezdik.
 */
export async function charge(userId: string, usd: number, pooled: boolean, detail?: ChargeDetail): Promise<void> {
  if (!CLOUD_MODE || !pooled) return;
  try {
    await chargeUsd(userId, usd);
    await logTx({ userId, kind: "usage", usd, pooled, ...(detail ?? { feature: "unknown" }) });
  } catch (e) {
    // SESSİZ YUTMA YOK: ücretlendirme başarısızsa hizmet bedavaya gitmiş demektir.
    // İstek bozulmamalı ama bu MUTLAKA görünür olmalı, yoksa kaybı hiç fark etmeyiz.
    console.error(`[BILLING] ücretlendirilemedi user=${userId} usd=${usd}:`, e);
  }
}

/**
 * Bu isteğin harcayabileceği üst sınır (USD). Akış ortasında kesme için.
 * Ücretlendirme geçerli değilse (yerel mod veya kullanıcının kendi anahtarı) sonsuz.
 */
export async function budgetUsd(userId: string, pooled: boolean): Promise<number> {
  if (!CLOUD_MODE || !pooled) return Number.POSITIVE_INFINITY;
  return await requestBudgetUsd(userId);
}

export function cloudMode(): boolean { return CLOUD_MODE; }
