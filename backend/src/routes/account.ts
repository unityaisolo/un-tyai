import { Router, type Request, type Response } from "express";
import { z } from "zod";
import crypto from "node:crypto";
import { getAccount, addCredits, setMembership, CREDITS_PER_USD, creditsFile } from "../lib/credits.js";

export const accountRouter = Router();

/**
 * HESAP / ÜYELİK UÇLARI.
 *
 * Bu uçlar YALNIZCA Nova Cloud'da anlamlıdır. Yerel kurulumda kredi kullanılmaz;
 * kullanıcı kendi anahtarıyla çalışır ve `cloudMode` false döner.
 */

const CLOUD_MODE = String(process.env.NOVA_CLOUD ?? "").toLowerCase() === "true";

/** Kredi eklemek/üyelik vermek için yönetici sırrı (ödeme webhook'u da bunu kullanır). */
const ADMIN_SECRET = process.env.ADMIN_SECRET ?? "";

accountRouter.get("/account", (req: Request, res: Response) => {
  const a = getAccount(req.userId);
  res.json({
    cloudMode: CLOUD_MODE,
    authed: req.authed === true,
    plan: a.plan,
    until: a.until ?? null,
    credits: a.credits,
    creditsUsd: +(a.credits / CREDITS_PER_USD).toFixed(4),
    spentUsd: +(a.spent / CREDITS_PER_USD).toFixed(4),
  });
});

/**
 * Kredi/üyelik verme — ADMIN_SECRET ister.
 *
 * NEDEN AYRI SIR: ödeme sağlayıcısının webhook'u da bu ucu çağırır. Kullanıcı
 * oturumuyla kredi eklenememesi kritik; aksi halde herkes kendine kredi yazar.
 */
const GrantBody = z.object({
  userId: z.string().min(1),
  credits: z.number().int().positive().optional(),
  membershipDays: z.number().int().positive().optional(),
});

accountRouter.post("/account/grant", (req: Request, res: Response) => {
  if (!ADMIN_SECRET) { res.status(503).json({ error: "ADMIN_SECRET tanımlı değil — kredi verme kapalı." }); return; }
  const given = (req.header("x-admin-secret") ?? "").trim();
  // Sabit süreli karşılaştırma: sır uzunluğu/içeriği zamanlamadan sızmasın.
  if (given.length !== ADMIN_SECRET.length ||
      !crypto.timingSafeEqual(Buffer.from(given), Buffer.from(ADMIN_SECRET))) {
    res.status(403).json({ error: "Yetkisiz" });
    return;
  }

  const parsed = GrantBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { userId, credits, membershipDays } = parsed.data;

  let a = getAccount(userId);
  if (credits) a = addCredits(userId, credits);
  if (membershipDays) a = setMembership(userId, membershipDays);

  res.json({ ok: true, userId, plan: a.plan, credits: a.credits, until: a.until ?? null });
});

/** Teşhis: kredi defterinin yeri (yalnız bulut yöneticisi için anlamlı). */
accountRouter.get("/account/where", (_req: Request, res: Response) => {
  res.json({ file: CLOUD_MODE ? creditsFile() : null, cloudMode: CLOUD_MODE });
});
