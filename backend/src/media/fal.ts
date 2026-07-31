// fal.ai medya istemcisi. text/image-to-3D (GLB). Anahtar + metering backend'de kalır.
//
// Model seçimi modaliteye göre yapılır:
//   - metin -> FAL_3D_TEXT_MODEL   (varsayılan: Tripo v2.5 text-to-3d)
//   - görsel -> FAL_3D_IMAGE_MODEL (varsayılan: Tripo v2.5 image-to-3d)
// Tek bir endpoint'e zorlamak istenirse FAL_3D_MODEL ayarlanır (ör. Rodin ikisini de destekler).
// fal sync endpoint: https://fal.run/{model}

export interface Generate3DResult {
  glbUrl: string;
  model: string;
  raw: unknown;
}

const DEFAULT_TEXT_MODEL = "tripo3d/tripo/v2.5/text-to-3d";
const DEFAULT_IMAGE_MODEL = "tripo3d/tripo/v2.5/image-to-3d";

// Not: Prompt'a otomatik ek YAPILMAZ — kullanıcı ne yazdıysa aynen üreticiye gider.
// (Denendi; "full body" gibi ekler nesne isteklerinde yanlış sonuç veriyordu.)

// Hangi endpoint kullanılacak? Görsel varsa görsel-modeli, yoksa metin-modeli.
export function pickModel(opts: { prompt?: string; imageUrl?: string; model?: string }): string {
  if (opts.model) return opts.model;
  const forced = process.env.FAL_3D_MODEL?.trim();
  if (forced) return forced; // tek endpoint'e zorla (geriye dönük uyumluluk)
  if (opts.imageUrl) return process.env.FAL_3D_IMAGE_MODEL?.trim() || DEFAULT_IMAGE_MODEL;
  return process.env.FAL_3D_TEXT_MODEL?.trim() || DEFAULT_TEXT_MODEL;
}

export async function generate3D(opts: {
  apiKey: string;
  prompt?: string;
  imageUrl?: string;
  model?: string;
  faceLimit?: number;
}): Promise<Generate3DResult> {
  const model = pickModel(opts);
  const isRodin = /rodin/i.test(model);
  const isTripo = /tripo/i.test(model);
  const isHunyuan = /hunyuan/i.test(model);

  const input: Record<string, unknown> = {};
  if (opts.prompt) input.prompt = opts.prompt;

  if (opts.imageUrl) {
    // Rodin çoklu görsel alanı kullanır; çoğu model tekil image_url ister.
    if (isRodin) input.input_image_urls = [opts.imageUrl];
    else input.image_url = opts.imageUrl;
  }

  // Çıktı GLB olsun ve makul bir doku ayarı ver.
  if (isRodin) {
    input.geometry_file_format = "glb";
  } else if (isTripo) {
    // Tripo: texture "no" | "standard" | "HD". Varsayılan standard (ucuz + iyi görünüm).
    input.texture = process.env.FAL_3D_TEXTURE?.trim() || "standard";
    // Poligon sınırı (düşük-poli/mobil). Verilmezse Tripo adaptif seçer.
    if (opts.faceLimit && opts.faceLimit > 0) input.face_limit = Math.round(opts.faceLimit);
    // Kalite/rig-dostu: bozulma ve eksik uzuv önleme.
    // Nesne-bağımsız kalite ipuçları (insana özel terim YOK; araba/bina/prop hepsinde güvenli).
    input.negative_prompt =
      process.env.FAL_3D_NEG ??
      "low quality, blurry, distorted, deformed, broken geometry, floating parts, holes, artifacts, duplicated parts, background objects, watermark, text";
  } else if (isHunyuan) {
    // Hunyuan çıktı zaten GLB; ekstra ayar gerekmez.
  }

  const res = await fetch(`https://fal.run/${model}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Key ${opts.apiKey}`,
    },
    body: JSON.stringify(input),
  });

  if (!res.ok) throw new Error(`fal ${res.status}: ${await res.text()}`);
  const data: any = await res.json();

  const glbUrl =
    data?.model_mesh?.url ??
    data?.pbr_model?.url ??
    data?.mesh?.url ??
    data?.model_glb?.url ??
    data?.glb?.url ??
    findGlbUrl(data);

  if (!glbUrl) throw new Error("fal yanıtında GLB URL bulunamadı");
  return { glbUrl, model, raw: data };
}

// Yanıtta .glb ile biten ilk URL'yi arar (model çıktı şeması değişebilir).
function findGlbUrl(obj: unknown): string | undefined {
  let found: string | undefined;
  const visit = (v: unknown) => {
    if (found) return;
    if (typeof v === "string") {
      if (/\.glb(\?|$)/i.test(v)) found = v;
    } else if (Array.isArray(v)) {
      v.forEach(visit);
    } else if (v && typeof v === "object") {
      Object.values(v as Record<string, unknown>).forEach(visit);
    }
  };
  visit(obj);
  return found;
}

// ---- Rig + Animasyon (fal-ai/meshy/rigging) ----
// Girdi: herhangi bir public GLB URL (bizim ürettiğimiz model). Çıktı: riglenmiş GLB
// + hazır yürüme/koşma; istenirse Meshy action_id'leriyle ek klipler. Senkron (fal.run).
export interface RigResult {
  riggedUrl?: string;
  animations: { name: string; actionId?: number; url: string }[];
  raw: unknown;
}

export async function rigAndAnimate(opts: {
  apiKey: string;
  modelUrl: string;
  heightMeters?: number;
  animationActionIds?: number[];
}): Promise<RigResult> {
  const multi = Array.isArray(opts.animationActionIds) && opts.animationActionIds.length > 0;
  const model = multi ? "fal-ai/meshy/rigging/multi-animation" : "fal-ai/meshy/rigging";

  const input: Record<string, unknown> = {
    model_url: opts.modelUrl,
    height_meters: opts.heightMeters ?? 1.7,
  };
  if (multi) input.animation_action_ids = opts.animationActionIds!.slice(0, 10);
  else input.enable_animation = true; // temel yürüme/koşma animasyonları (+$0.12)

  const res = await fetch(`https://fal.run/${model}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Key ${opts.apiKey}` },
    body: JSON.stringify(input),
  });
  if (!res.ok) throw new Error(`fal rig ${res.status}: ${await res.text()}`);
  const data: any = await res.json();

  const riggedUrl = data?.rigged_character_glb?.url ?? findGlbUrl(data);
  const animations: RigResult["animations"] = [];

  const ba = data?.basic_animations;
  if (ba?.walking_glb?.url) animations.push({ name: "walk", url: ba.walking_glb.url });
  if (ba?.running_glb?.url) animations.push({ name: "run", url: ba.running_glb.url });

  if (Array.isArray(data?.animations)) {
    for (const clip of data.animations) {
      const url = clip?.animation_glb?.url;
      if (url) animations.push({ name: `action_${clip.action_id}`, actionId: clip.action_id, url });
    }
  }

  return { riggedUrl, animations, raw: data };
}

// ---- Metinden görsel (fal FLUX schnell) — görselden-3D için kaynak ----
export async function generateImage(opts: {
  apiKey: string;
  prompt: string;
  model?: string;
  imageSize?: string;
}): Promise<{ imageUrl: string; model: string; raw: unknown }> {
  const model = opts.model ?? (process.env.FAL_IMAGE_MODEL?.trim() || "fal-ai/flux/schnell");
  const input: Record<string, unknown> = {
    prompt: opts.prompt,
    image_size: opts.imageSize ?? "square_hd",
    num_images: 1,
  };
  const res = await fetch(`https://fal.run/${model}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Key ${opts.apiKey}` },
    body: JSON.stringify(input),
  });
  if (!res.ok) throw new Error(`fal image ${res.status}: ${await res.text()}`);
  const data: any = await res.json();
  const imageUrl =
    data?.images?.[0]?.url ?? data?.image?.url ?? findImgUrl(data);
  if (!imageUrl) throw new Error("fal yanıtında görsel URL bulunamadı");
  return { imageUrl, model, raw: data };
}

function findImgUrl(obj: unknown): string | undefined {
  let found: string | undefined;
  const visit = (v: unknown) => {
    if (found) return;
    if (typeof v === "string") { if (/\.(png|jpe?g|webp)(\?|$)/i.test(v)) found = v; }
    else if (Array.isArray(v)) v.forEach(visit);
    else if (v && typeof v === "object") Object.values(v as any).forEach(visit);
  };
  visit(obj);
  return found;
}
