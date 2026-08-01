import { Router, type Request, type Response } from "express";
import { z } from "zod";
import crypto from "node:crypto";
import { getAccount, addCredits, setMembership, CREDITS_PER_USD, creditsFile } from "../lib/credits.js";
import { logTx, readTx, summarize } from "../lib/usagelog.js";

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

accountRouter.get("/account", async (req: Request, res: Response) => {
  const a = await getAccount(req.userId, req.emailVerified === true);
  res.json({
    cloudMode: CLOUD_MODE,
    authed: req.authed === true,
    emailVerified: req.emailVerified === true,
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

accountRouter.post("/account/grant", async (req: Request, res: Response) => {
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

  let a = await getAccount(userId);
  if (credits) {
    a = await addCredits(userId, credits);
    // Yükleme de deftere girer: kullanıcı sadece harcamayı değil, gelen krediyi de
    // görebilmeli. Tek taraflı bir döküm güven vermez.
    await logTx({ userId, kind: "topup", usd: credits / CREDITS_PER_USD, credits, note: "yönetici/ödeme" });
  }
  if (membershipDays) {
    a = await setMembership(userId, membershipDays);
    await logTx({ userId, kind: "membership", usd: 0, credits: 0, note: `${membershipDays} gün üyelik` });
  }

  res.json({ ok: true, userId, plan: a.plan, credits: a.credits, until: a.until ?? null });
});

/**
 * İŞLEM DÖKÜMÜ — kullanıcı ne için ne kadar ödediğini buradan görür.
 * Şeffaflık, "haksız kesinti yapıldı" iddiasına verilebilecek tek somut cevap.
 */
accountRouter.get("/account/usage", async (req: Request, res: Response) => {
  const limit = Math.min(500, Math.max(1, Number(req.query.limit ?? 100)));
  const txs = await readTx(req.userId, limit);
  res.json({ transactions: txs, summary: summarize(txs), creditsPerUsd: CREDITS_PER_USD });
});

/** Teşhis: kredi defterinin yeri (yalnız bulut yöneticisi için anlamlı). */
accountRouter.get("/account/where", async (_req: Request, res: Response) => {
  res.json({ file: CLOUD_MODE ? creditsFile() : null, cloudMode: CLOUD_MODE });
});
