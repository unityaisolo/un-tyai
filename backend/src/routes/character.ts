import { Router, type Request, type Response } from "express";
import { z } from "zod";
import { generate3D, rigAndAnimate } from "../media/fal.js";
import { resolveKey } from "../lib/keyvault.js";
import { noKeyMessage } from "../lib/nokey.js";
import { gate } from "../lib/gate.js";
import { recordGeneration } from "../billing/metering.js";

export const characterRouter = Router();

// Rigleme sonrası ücretsiz gelen temel animasyonlar + gelişmiş klip notu.
// Küratörlü animasyon preset'leri (Meshy library action ID -> etiket). Unity dropdown kullanır.
const ANIMATION_PRESETS: { id: number; label: string; category: string }[] = [
  { id: 0, label: "Idle (bekleme)", category: "Temel" },
  { id: 30, label: "Yürüme (Walk)", category: "Temel" },
  { id: 14, label: "Koşma (Run)", category: "Temel" },
  { id: 86, label: "Zıplama (Jump)", category: "Temel" },
  { id: 4, label: "Saldırı (Attack)", category: "Savaş" },
  { id: 90, label: "Karşı saldırı (Punch)", category: "Savaş" },
  { id: 89, label: "Savaş duruşu (Combat)", category: "Savaş" },
  { id: 87, label: "Boks", category: "Savaş" },
];

characterRouter.get("/character/animations", (_req: Request, res: Response) => {
  res.json({
    presets: ANIMATION_PRESETS,
    note: "Rigleme ile walk+run ücretsiz gelir. Preset seçilirse ek klip (Meshy action_id) üretilir.",
  });
});

const PipelineBody = z.object({
  prompt: z.string().min(1).optional(),
  modelUrl: z.string().url().optional(),           // hazır GLB varsa doğrudan rigle
  imageUrl: z.string().url().optional(),           // görselden karakter
  animationActionIds: z.array(z.number().int()).max(10).optional(),
  heightMeters: z.number().positive().optional(),
  texture: z.boolean().optional(),
});

// Karakter hattı: (üret ya da hazır modelUrl) -> rigle + animasyonla. Tek fal anahtarı.
characterRouter.post("/character/pipeline", async (req: Request, res: Response) => {
  const parsed = PipelineBody.safeParse(req.body);
  if (!parsed.success) return res.status(400).json({ error: parsed.error.flatten() });
  const { prompt, modelUrl, imageUrl, animationActionIds, heightMeters, texture } = parsed.data;
  if (!prompt && !modelUrl && !imageUrl)
    return res.status(400).json({ error: "prompt, imageUrl veya modelUrl gerekli" });

  if (!(await gate(req, res, "studio")).ok) return;  const key = resolveKey(req.userId, "fal");
  if (!key)
    return res.status(400).json({ error: noKeyMessage("fal") });

  try {
    // 1) Model: hazır URL yoksa üret (fal / Tripo v2.5)
    let baseModelUrl = modelUrl;
    let genCost = 0;
    if (!baseModelUrl) {
      const gen = await generate3D({ apiKey: key.apiKey, prompt, imageUrl });
      baseModelUrl = gen.glbUrl;
      genCost = recordGeneration({ userId: req.userId, kind: "3d", model: gen.model, pooled: key.pooled }).totalUsd;
    }

    // 2) Rigle (+ opsiyonel ek animasyon klipleri)
    const rig = await rigAndAnimate({
      apiKey: key.apiKey,
      modelUrl: baseModelUrl!,
      heightMeters,
      animationActionIds,
    });
    let rigCost = recordGeneration({ userId: req.userId, kind: "rig", model: "meshy", pooled: key.pooled }).totalUsd;
    if (animationActionIds && animationActionIds.length > 0) {
      for (let i = 0; i < animationActionIds.length; i++)
        rigCost += recordGeneration({ userId: req.userId, kind: "animation", model: "meshy", pooled: key.pooled }).totalUsd;
    }

    res.json({
      modelUrl: baseModelUrl ?? null,
      riggedUrl: rig.riggedUrl ?? null,
      animations: rig.animations,             // [{name, actionId?, url}]
      finalUrl: rig.riggedUrl ?? baseModelUrl ?? null,
      cost: genCost + rigCost,
    });
  } catch (err) {
    res.status(502).json({ error: err instanceof Error ? err.message : String(err) });
  }
});
