// Tripo direct API istemcisi (openapi v2). Karakter hattı: üret → rig → animasyon.
// Tripo async çalışır: create task -> poll status -> output URL.
// Rig, üretim task'ına bağlıdır (original_model_task_id), bu yüzden karakter
// üretimini de burada (fal değil) yaparız ki zincir kurulabilsin.
// Docs: https://platform.tripo3d.ai/docs

const BASE = process.env.TRIPO_BASE?.trim() || "https://api.tripo3d.ai/v2/openapi";

function sleep(ms: number) {
  return new Promise((r) => setTimeout(r, ms));
}

interface TaskData {
  task_id: string;
  status: string;
  progress?: number;
  output?: Record<string, any>;
  input?: Record<string, any>;
}

async function createTask(apiKey: string, body: Record<string, unknown>): Promise<string> {
  const res = await fetch(`${BASE}/task`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`Tripo create ${res.status}: ${await res.text()}`);
  const j: any = await res.json();
  const id = j?.data?.task_id ?? j?.task_id;
  if (!id) throw new Error(`Tripo yanıtında task_id yok: ${JSON.stringify(j).slice(0, 200)}`);
  return String(id);
}

export async function pollTask(
  apiKey: string,
  taskId: string,
  opts: { timeoutMs?: number; intervalMs?: number } = {},
): Promise<TaskData> {
  const timeoutMs = opts.timeoutMs ?? 180_000;
  const intervalMs = opts.intervalMs ?? 3_000;
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const res = await fetch(`${BASE}/task/${taskId}`, {
      headers: { Authorization: `Bearer ${apiKey}` },
    });
    if (!res.ok) throw new Error(`Tripo poll ${res.status}: ${await res.text()}`);
    const j: any = await res.json();
    const d: TaskData = j?.data ?? j;
    const status = String(d?.status ?? "").toLowerCase();
    if (status === "success" || status === "succeeded") return d;
    if (["failed", "cancelled", "canceled", "banned", "expired", "error"].includes(status))
      throw new Error(`Tripo task '${status}' (${taskId})`);
    await sleep(intervalMs);
  }
  throw new Error(`Tripo task zaman aşımı (${taskId})`);
}

// Çıktıdan GLB/model URL'sini çıkarır. Tripo alanları sürüme göre değişebilir.
export function extractModelUrl(d: TaskData): string | undefined {
  const o: any = d?.output ?? {};
  const cand =
    o.pbr_model ?? o.model ?? o.rigged_model ?? o.base_model ?? o.animated_model ?? o.result;
  const url = typeof cand === "string" ? cand : cand?.url;
  if (url) return url;
  return findUrl(d, /\.(glb|fbx)(\?|$)/i);
}

function findUrl(obj: unknown, re: RegExp): string | undefined {
  let found: string | undefined;
  const visit = (v: unknown) => {
    if (found) return;
    if (typeof v === "string") { if (re.test(v)) found = v; }
    else if (Array.isArray(v)) v.forEach(visit);
    else if (v && typeof v === "object") Object.values(v as any).forEach(visit);
  };
  visit(obj);
  return found;
}

export interface TripoStageResult {
  taskId: string;
  url?: string;
  raw: TaskData;
}

// 1) Metinden model üret (task_id döner; rig için gerekli).
export async function tripoTextToModel(
  apiKey: string,
  prompt: string,
  opts: { texture?: boolean; style?: string } = {},
): Promise<TripoStageResult> {
  const body: Record<string, unknown> = {
    type: "text_to_model",
    prompt,
    texture: opts.texture ?? true,
  };
  if (opts.style) body.style = opts.style;
  const taskId = await createTask(apiKey, body);
  const raw = await pollTask(apiKey, taskId);
  return { taskId, url: extractModelUrl(raw), raw };
}

// 2) Modeli rigle (Humanoid iskelet). original_model_task_id = üretim task'ı.
export async function tripoRig(
  apiKey: string,
  modelTaskId: string,
  opts: { outFormat?: string; spec?: string } = {},
): Promise<TripoStageResult> {
  const taskId = await createTask(apiKey, {
    type: "animate_rig",
    original_model_task_id: modelTaskId,
    out_format: opts.outFormat ?? "glb",
    spec: opts.spec ?? "tripo",
  });
  const raw = await pollTask(apiKey, taskId);
  return { taskId, url: extractModelUrl(raw), raw };
}

// 3) Animasyon uygula (retarget). original_model_task_id = rig task'ı.
export async function tripoAnimate(
  apiKey: string,
  rigTaskId: string,
  animation: string,
  opts: { outFormat?: string; bake?: boolean } = {},
): Promise<TripoStageResult> {
  const taskId = await createTask(apiKey, {
    type: "animate_retarget",
    original_model_task_id: rigTaskId,
    animation,
    out_format: opts.outFormat ?? "glb",
    bake_animation: opts.bake ?? true,
  });
  const raw = await pollTask(apiKey, taskId);
  return { taskId, url: extractModelUrl(raw), raw };
}

// Tripo yerleşik animasyon preset'leri (retarget için). Kısa, oyunlarda en sık kullanılanlar.
export const TRIPO_ANIMATIONS: { id: string; label: string }[] = [
  { id: "preset:idle", label: "Idle (bekleme)" },
  { id: "preset:walk", label: "Walk (yürüme)" },
  { id: "preset:run", label: "Run (koşma)" },
  { id: "preset:jump", label: "Jump (zıplama)" },
  { id: "preset:slash", label: "Slash (kılıç saldırısı)" },
  { id: "preset:shoot", label: "Shoot (ateş etme)" },
  { id: "preset:hurt", label: "Hurt (hasar)" },
  { id: "preset:fall", label: "Fall (düşme)" },
  { id: "preset:turn", label: "Turn (dönme)" },
  { id: "preset:climb", label: "Climb (tırmanma)" },
];
