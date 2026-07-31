import { Router, type Request, type Response } from "express";
import { z } from "zod";
import { generate3D, generateImage } from "../media/fal.js";
import { resolveKey } from "../lib/keyvault.js";
import { noKeyMessage } from "../lib/nokey.js";
import { gate } from "../lib/gate.js";
import { recordGeneration } from "../billing/metering.js";

export const generateRouter = Router();

const Body3D = z.object({
  prompt: z.string().optional(),
  imageUrl: z.string().url().optional(),
  model: z.string().optional(),
  faceLimit: z.number().int().positive().optional(),
});

// POST /v1/generate/3d -> { glbUrl, model, cost }
// Unity, hem 3D Stüdyo (önizleme) hem Generate3DModel aracı için burayı çağırır.
generateRouter.post("/generate/3d", async (req: Request, res: Response) => {
  const parsed = Body3D.safeParse(req.body);
  if (!parsed.success) return res.status(400).json({ error: parsed.error.flatten() });
  if (!parsed.data.prompt && !parsed.data.imageUrl)
    return res.status(400).json({ error: "prompt veya imageUrl gerekli" });

  if (!gate(req, res, "studio").ok) return;  const key = resolveKey(req.userId, "fal");
  if (!key)
    return res.status(400).json({ error: noKeyMessage("fal") });

  try {
    const result = await generate3D({ apiKey: key.apiKey, ...parsed.data });
    const usage = recordGeneration({
      userId: req.userId,
      kind: "3d",
      model: result.model,
      pooled: key.pooled,
    });
    res.json({ glbUrl: result.glbUrl, model: result.model, cost: usage.totalUsd });
  } catch (err) {
    res.status(502).json({ error: err instanceof Error ? err.message : String(err) });
  }
});

// POST /v1/generate/image -> { imageUrl, cost }  (metinden görsel; görselden-3D için kaynak)
const BodyImage = z.object({ prompt: z.string().min(1), model: z.string().optional() });
generateRouter.post("/generate/image", async (req: Request, res: Response) => {
  const parsed = BodyImage.safeParse(req.body);
  if (!parsed.success) return res.status(400).json({ error: parsed.error.flatten() });
  if (!gate(req, res, "studio").ok) return;  const key = resolveKey(req.userId, "fal");
  if (!key)
    return res.status(400).json({ error: noKeyMessage("fal") });
  try {
    const result = await generateImage({ apiKey: key.apiKey, prompt: parsed.data.prompt, model: parsed.data.model });
    const usage = recordGeneration({ userId: req.userId, kind: "image", model: result.model, pooled: key.pooled });
    res.json({ imageUrl: result.imageUrl, cost: usage.totalUsd });
  } catch (err) {
    res.status(502).json({ error: err instanceof Error ? err.message : String(err) });
  }
});
