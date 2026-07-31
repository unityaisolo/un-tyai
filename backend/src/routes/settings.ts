import { Router, type Request, type Response } from "express";
import { z } from "zod";
import {
  saveKey, deleteKey, listKeys, getSettings, saveSettings, vaultLocation,
} from "../lib/keyvault.js";
import { providerLabel, providerKeyUrl } from "../lib/nokey.js";
import { ROLES, ROLE_INFO, effectiveModels, modelSuggestions } from "../aliases.js";
import { resolveTarget } from "../lib/target.js";
import { resolveKey } from "../lib/keyvault.js";
import { listModels } from "../lib/modellist.js";
import { autoSetup } from "../lib/autosetup.js";

export const settingsRouter = Router();

/**
 * AYARLAR / ANAHTAR YÖNETİMİ
 *
 * GÜVENLİK SÖZLEŞMESİ — burada tavizsiz:
 *  1) Hiçbir uç, hiçbir anahtarın TAM DEĞERİNİ döndürmez. Yalnız maske ("sk-ab…9f").
 *  2) Maske bile SADECE kullanıcının kendi kaydettiği anahtar için üretilir.
 *  3) Sunucu sahibinin havuz anahtarları (.env: GROQ_API_KEY vb.) hiçbir yanıtta
 *     yer almaz — ne değeri, ne maskesi. Yalnızca "bu sağlayıcı anahtarsız
 *     çalışabiliyor" anlamına gelen bir boolean döner.
 *  4) Anahtarlar loglanmaz.
 */

// Anahtar kaydedilebilen SAĞLAYICI kimlikleri (yönlendirme id'leri).
const PROVIDERS = ["groq", "openrouter", "openai", "anthropic", "gemini", "deepseek", "custom", "fal"] as const;

/**
 * SERVİS KATALOĞU — Ayarlar ekranındaki açılır liste bunu çizer.
 *
 * Neden gerekli: "Özel endpoint" tek satır olarak yeterli değildi. Kullanıcı
 * (ör. Çin'de Qwen/Kimi kullanan biri) hangi adresi yazacağını bilmiyor. Artık
 * adres HAZIR gelir; kullanıcı yalnızca anahtarını ve model adını girer.
 *
 * `provider`  → anahtarın hangi kimlikle saklanacağı ("custom" = OpenAI-uyumlu)
 * `baseUrl`   → hazır adres (yalnız custom tabanlı servislerde)
 * `model`     → örnek/varsayılan model adı. SADECE bu projede gerçekten kullanılan
 *               adlar yazılır; emin olmadığımız servislerde BOŞ bırakılır ve
 *               kullanıcı servisin panelinden kopyalar (uydurma model adı YOK).
 * `needsKey`  → yerel sunucularda (LM Studio / vLLM / Ollama) false
 */
interface Service {
  id: string;
  label: string;
  provider: (typeof PROVIDERS)[number] | "ollama";
  baseUrl?: string;
  model?: string;
  modelHint?: string;
  needsKey: boolean;
  note?: string;
}

const SERVICES: Service[] = [
  // ——— Ücretsiz başlangıç ———
  { id: "groq", label: "Groq — ücretsiz kota, çok hızlı", provider: "groq",
    model: "llama-3.3-70b-versatile", needsKey: true },
  { id: "openrouter", label: "OpenRouter — ücretsiz modeller", provider: "openrouter",
    model: "meta-llama/llama-3.3-70b-instruct:free", needsKey: true },

  // ——— Büyük sağlayıcılar (kendi protokolleri) ———
  { id: "openai", label: "OpenAI", provider: "openai", model: "gpt-4o-mini", needsKey: true },
  { id: "anthropic", label: "Anthropic (Claude)", provider: "anthropic", model: "claude-4-sonnet", needsKey: true },
  { id: "gemini", label: "Google Gemini", provider: "gemini", model: "gemini-3.1-flash-lite", needsKey: true },
  { id: "deepseek", label: "DeepSeek", provider: "deepseek", model: "deepseek-chat", needsKey: true },

  // ——— Açık kaynak model sunucuları (OpenAI-uyumlu) ———
  { id: "together", label: "Together AI — açık kaynak modeller", provider: "custom",
    baseUrl: "https://api.together.xyz/v1", needsKey: true,
    modelHint: "Together panelinden model adını kopyala" },
  { id: "fireworks", label: "Fireworks AI — açık kaynak modeller", provider: "custom",
    baseUrl: "https://api.fireworks.ai/inference/v1", needsKey: true,
    modelHint: "Fireworks panelinden model adını kopyala" },
  { id: "cerebras", label: "Cerebras — çok hızlı", provider: "custom",
    baseUrl: "https://api.cerebras.ai/v1", needsKey: true,
    modelHint: "Cerebras panelinden model adını kopyala" },
  { id: "deepinfra", label: "DeepInfra — açık kaynak modeller", provider: "custom",
    baseUrl: "https://api.deepinfra.com/v1/openai", needsKey: true,
    modelHint: "DeepInfra panelinden model adını kopyala" },

  // ——— Çin / Asya sağlayıcıları (OpenAI-uyumlu) ———
  { id: "qwen-intl", label: "Alibaba Qwen (uluslararası)", provider: "custom",
    baseUrl: "https://dashscope-intl.aliyuncs.com/compatible-mode/v1", needsKey: true,
    modelHint: "Model Studio panelinden model adını kopyala" },
  { id: "qwen-cn", label: "Alibaba Qwen (Çin / 中国)", provider: "custom",
    baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1", needsKey: true,
    modelHint: "百炼 panelinden model adını kopyala" },
  { id: "moonshot", label: "Moonshot / Kimi", provider: "custom",
    baseUrl: "https://api.moonshot.ai/v1", needsKey: true,
    modelHint: "Kimi panelinden model adını kopyala" },

  // ——— Yerel (ücretsiz, veri dışarı çıkmaz) ———
  { id: "ollama", label: "Ollama — yerel, anahtar gerekmez", provider: "ollama",
    model: "ollama/llama3.1", needsKey: false,
    note: "Ollama kurulu ve model indirilmiş olmalı (ollama pull llama3.1)." },
  { id: "lmstudio", label: "LM Studio — yerel", provider: "custom",
    baseUrl: "http://localhost:1234/v1", needsKey: false,
    modelHint: "LM Studio'da yüklü model adını yaz",
    note: "LM Studio'da 'Local Server'ı başlat. Adres farklıysa düzelt." },
  { id: "vllm", label: "vLLM — yerel/kendi sunucun", provider: "custom",
    baseUrl: "http://localhost:8000/v1", needsKey: false,
    modelHint: "vLLM'e verdiğin model adını yaz",
    note: "Adres farklıysa düzelt." },

  // ——— Elle ———
  { id: "other", label: "Diğer — OpenAI uyumlu (adresi elle gir)", provider: "custom",
    needsKey: true, modelHint: "servisin verdiği model adını yaz",
    note: "OpenAI uyumlu her servis çalışır. Adres genelde /v1 ile biter." },

  // ——— 3D üretim ———
  { id: "fal", label: "fal.ai — 3D model üretimi (opsiyonel)", provider: "fal", needsKey: true,
    note: "Yalnızca 3D Stüdyo için. Dünya ve sohbet bu anahtar olmadan çalışır." },
];

const POOL_ENABLED = String(process.env.ALLOW_POOL_KEYS ?? "").toLowerCase() === "true";

/** Havuzda o sağlayıcı için anahtar var mı? SADECE boolean — değer/maske asla. */
function poolAvailable(provider: string): boolean {
  if (!POOL_ENABLED) return false;
  const map: Record<string, string | undefined> = {
    groq: process.env.GROQ_API_KEY,
    openrouter: process.env.OPENROUTER_API_KEY,
    openai: process.env.OPENAI_API_KEY,
    anthropic: process.env.ANTHROPIC_API_KEY,
    gemini: process.env.GEMINI_API_KEY,
    deepseek: process.env.DEEPSEEK_API_KEY,
    custom: process.env.CUSTOM_API_KEY,
    fal: process.env.FAL_KEY,
  };
  return Boolean(map[provider]);
}

// ---------------------------------------------------------------- durum

settingsRouter.get("/settings", (req: Request, res: Response) => {
  const mine = new Map(listKeys(req.userId).map((k) => [k.provider, k.hint]));
  const s = getSettings(req.userId);

  res.json({
    services: SERVICES.map((v) => ({
      id: v.id,
      label: v.label,
      provider: v.provider,
      baseUrl: v.baseUrl ?? "",
      isCustom: v.provider === "custom",   // adres alanı düzenlenebilir mi
      model: v.model ?? "",
      modelHint: v.modelHint ?? "",
      note: v.note ?? "",
      needsKey: v.needsKey,
      keyUrl: providerKeyUrl(v.provider),
      // Anahtar durumu SAĞLAYICI bazında tutulur (custom tabanlı servisler ortak)
      hasKey: mine.has(v.provider),
      hint: mine.get(v.provider) ?? "",    // yalnız KULLANICININ kendi anahtarının maskesi
      poolAvailable: poolAvailable(v.provider),
    })),
    // Kayıtlı anahtar listesi (sağlayıcı bazında, kompakt gösterim için)
    savedKeys: PROVIDERS.filter((p) => mine.has(p)).map((p) => ({
      provider: p, label: providerLabel(p), hint: mine.get(p) ?? "",
    })),
    roles: ROLES.map((r) => ({
      id: r,
      label: ROLE_INFO[r].label,
      hint: ROLE_INFO[r].hint,
      chosen: s.models?.[r] ?? "",          // kullanıcının yazdığı (boşsa varsayılan)
      effective: effectiveModels(req.userId)[r], // gerçekte kullanılan model
    })),
    suggestions: modelSuggestions(),
    customBaseUrl: s.customBaseUrl ?? "",
    poolMode: POOL_ENABLED,
    vaultPath: vaultLocation(),
  });
});

// ---------------------------------------------------------------- anahtar yaz/sil

const KeyBody = z.object({
  provider: z.string().min(1),
  apiKey: z.string().min(8, "Anahtar çok kısa"),
});

settingsRouter.post("/keys", (req: Request, res: Response) => {
  const parsed = KeyBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: "provider ve apiKey gerekli (anahtar en az 8 karakter)" }); return; }
  const { provider, apiKey } = parsed.data;
  if (!(PROVIDERS as readonly string[]).includes(provider)) { res.status(400).json({ error: "Bilinmeyen sağlayıcı: " + provider }); return; }

  saveKey(req.userId, provider, apiKey.trim());
  res.json({ ok: true, provider, hasKey: true });   // anahtar geri DÖNMEZ
});

settingsRouter.delete("/keys/:provider", (req: Request, res: Response) => {
  const removed = deleteKey(req.userId, req.params.provider);
  res.json({ ok: true, removed });
});

// ---------------------------------------------------------------- rol → model

const ModelsBody = z.object({
  models: z.record(z.string()).optional(),
  customBaseUrl: z.string().optional(),
});

settingsRouter.post("/settings", (req: Request, res: Response) => {
  const parsed = ModelsBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }

  const models: Record<string, string> = {};
  for (const [k, v] of Object.entries(parsed.data.models ?? {}))
    if ((ROLES as string[]).includes(k)) models[k] = String(v).trim();

  let baseUrl = parsed.data.customBaseUrl;
  if (baseUrl !== undefined) {
    baseUrl = baseUrl.trim();
    if (baseUrl && !/^https?:\/\//i.test(baseUrl)) {
      res.status(400).json({ error: "Endpoint adresi http:// veya https:// ile başlamalı" });
      return;
    }
  }

  saveSettings(req.userId, { models, customBaseUrl: baseUrl });
  res.json({ ok: true, effective: effectiveModels(req.userId) });
});

// ---------------------------------------------------------------- OTOMATİK KURULUM

/**
 * TEK ALAN, TEK BUTON: kullanıcı anahtarı yapıştırır, gerisi otomatik.
 * Sağlayıcı tanınır, model listesi çekilir, ARAÇ ÇAĞIRMAYI GERÇEKTEN destekleyen
 * ilk uygun model seçilir ve tüm işlere atanır.
 */
const AutoBody = z.object({
  // Yerel sunucularda (Ollama/LM Studio/vLLM) anahtar YOKTUR — bu yüzden opsiyonel.
  // Adres verilmediyse anahtar zorunlu (bulut servisini anahtardan tanıyoruz).
  apiKey: z.string().optional(),
  baseUrl: z.string().optional(),
});

settingsRouter.post("/settings/auto", async (req: Request, res: Response) => {
  const parsed = AutoBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ ok: false, error: "Geçersiz istek." }); return; }

  const custom = (parsed.data.baseUrl ?? "").trim();
  const rawKey = (parsed.data.apiKey ?? "").trim();
  if (!custom && rawKey.length < 8) {
    res.status(400).json({ ok: false, error: "Anahtarı yapıştır (en az 8 karakter). Kendi sunucunu kullanacaksan aşağıdan adresini seç." });
    return;
  }
  if (custom && !/^https?:\/\//i.test(custom)) {
    res.status(400).json({ ok: false, error: "Adres http:// veya https:// ile başlamalı." });
    return;
  }

  const r = await autoSetup(rawKey, custom);
  if (!r.ok) { res.status(400).json(r); return; }

  const provider = r.provider!;
  const model = r.model!;

  // Anahtar yoksa (yerel sunucu) kaydedilecek bir şey yok.
  if (rawKey.length >= 8) saveKey(req.userId, provider === "custom" ? "custom" : (provider as any), rawKey);

  // Yönlendirme öneki: custom servislerde model adının önüne "custom/" gelir.
  const routed = provider === "custom" ? "custom/" + model : model;
  saveSettings(req.userId, {
    models: { brain: routed, code: routed, curator: routed, vision: routed },
    ...(provider === "custom" ? { customBaseUrl: custom } : {}),
  });

  res.json({
    ok: true,
    provider,
    model,
    label: providerLabel(provider),
    rejected: r.rejected ?? [],
    effective: effectiveModels(req.userId),
  });
});

// ---------------------------------------------------------------- model listesi

/**
 * Servisin GERÇEK model listesini döndürür — kullanıcı listeden seçer, elle yazmaz.
 * Anahtar gövdede gelebilir (henüz kaydedilmemişse) ya da kasadan okunur.
 */
const ModelsListBody = z.object({
  serviceId: z.string().min(1),
  apiKey: z.string().optional(),
  baseUrl: z.string().optional(),
});

settingsRouter.post("/settings/models", async (req: Request, res: Response) => {
  const parsed = ModelsListBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }

  const svc = SERVICES.find((s) => s.id === parsed.data.serviceId);
  if (!svc) { res.status(400).json({ error: "Bilinmeyen servis" }); return; }

  // Anahtar: gövdeden (yeni girilmiş) → kasadan (daha önce kaydedilmiş)
  let key = (parsed.data.apiKey ?? "").trim();
  if (!key && svc.provider !== "ollama") {
    const k = resolveKeyForList(req.userId, svc.provider);
    key = k ?? "";
  }
  if (svc.needsKey && !key) { res.json({ models: [], error: "Önce anahtarı gir." }); return; }

  const baseUrl = svc.provider === "custom"
    ? ((parsed.data.baseUrl ?? svc.baseUrl ?? "").trim())
    : NATIVE_BASE[svc.provider] ?? "";

  const out = await listModels(svc.provider, baseUrl, key);
  res.json(out);
});

/** OpenAI-uyumlu yerleşik sağlayıcıların model listesi adresleri. */
const NATIVE_BASE: Record<string, string> = {
  openai: "https://api.openai.com/v1",
  groq: "https://api.groq.com/openai/v1",
  openrouter: "https://openrouter.ai/api/v1",
  deepseek: "https://api.deepseek.com/v1",
};

/** Kasadan anahtar okur (yalnız model listesi için; değer dışa DÖNMEZ). */
function resolveKeyForList(userId: string, provider: string): string | null {
  const r = resolveKey(userId, provider);
  return r ? r.apiKey : null;
}

// ---------------------------------------------------------------- tek adımda kur

/**
 * "SERVİSİ KUR" — kullanıcının tek tıkla çalışan bir kuruluma ulaşmasını sağlar.
 *
 * NEDEN GEREKLİ: kullanıcı OpenAI anahtarını kaydediyordu ama beyin hâlâ varsayılan
 * Groq modelini kullanıyordu; "kaydettim, hiçbir şey değişmedi" durumu oluşuyordu.
 * Bu uç anahtarı + adresi + model seçimini AYNI anda yazar.
 */
const SetupBody = z.object({
  serviceId: z.string().min(1),
  apiKey: z.string().optional(),
  baseUrl: z.string().optional(),
  model: z.string().optional(),
  /** true ise bu servis ana model (Beyin) olarak atanır */
  useAsBrain: z.boolean().default(true),
});

settingsRouter.post("/settings/setup", (req: Request, res: Response) => {
  const parsed = SetupBody.safeParse(req.body);
  if (!parsed.success) { res.status(400).json({ error: parsed.error.flatten() }); return; }
  const { serviceId, apiKey, baseUrl, model, useAsBrain } = parsed.data;

  const svc = SERVICES.find((s) => s.id === serviceId);
  if (!svc) { res.status(400).json({ error: "Bilinmeyen servis: " + serviceId }); return; }

  // 1) Anahtar (gerekiyorsa)
  if (svc.needsKey) {
    const k = (apiKey ?? "").trim();
    if (k.length < 8) { res.status(400).json({ error: "Anahtar eksik veya çok kısa (en az 8 karakter)." }); return; }
    if (svc.provider === "ollama") { res.status(400).json({ error: "Ollama anahtar kullanmaz." }); return; }
    saveKey(req.userId, svc.provider, k);
  }

  // 2) Adres (yalnız OpenAI-uyumlu servisler)
  const patch: { models?: Record<string, string>; customBaseUrl?: string } = {};
  if (svc.provider === "custom") {
    const url = (baseUrl ?? svc.baseUrl ?? "").trim();
    if (!url) { res.status(400).json({ error: "Endpoint adresi gerekli." }); return; }
    if (!/^https?:\/\//i.test(url)) { res.status(400).json({ error: "Adres http:// veya https:// ile başlamalı." }); return; }
    patch.customBaseUrl = url;
  }

  // 3) Model → TÜM rollere ata
  //
  // NEDEN HEPSİ: kullanıcı tek anahtar giriyor. Sadece "beyin"e atarsak küratör
  // varsayılan Groq modelinde kalır ve arazi kurulumu "anahtar yok" hatası verir.
  // Tek servis bağlayan biri her şeyin çalışmasını bekler — rolleri ayırmak
  // isteyen güç kullanıcı için ayrı bir uç (/v1/settings) zaten var.
  if (useAsBrain) {
    let m = (model ?? svc.model ?? "").trim();
    if (!m) { res.status(400).json({ error: "Model seçilmedi. 'Modelleri getir' ile listeyi çekip birini seç." }); return; }
    // Yönlendirme öneki kullanıcıdan istenmez, biz ekleriz.
    if (svc.provider === "custom" && !m.startsWith("custom/")) m = "custom/" + m;
    if (svc.provider === "ollama" && !m.startsWith("ollama/")) m = "ollama/" + m;
    patch.models = { brain: m, code: m, curator: m, vision: m };
  }

  if (patch.models || patch.customBaseUrl !== undefined) saveSettings(req.userId, patch);

  res.json({ ok: true, effective: effectiveModels(req.userId) });
});

// ---------------------------------------------------------------- bağlantı testi

/**
 * Bir rolün gerçekten çalıştığını doğrular: modele "ping" der, ilk token'ı bekler.
 * Kullanıcı "anahtarı doğru mu girdim?" sorusunu tek tıkla yanıtlar.
 */
settingsRouter.post("/settings/test", async (req: Request, res: Response) => {
  const role = String(req.body?.role ?? "brain");
  if (!(ROLES as string[]).includes(role)) { res.status(400).json({ error: "Geçersiz rol" }); return; }

  const t = resolveTarget(req.userId, role as any);
  if (!t.ok) { res.status(400).json({ ok: false, error: t.error }); return; }

  try {
    let got = "";
    for await (const ev of t.target.provider.chat({
      model: t.target.model,
      messages: [{ role: "user", content: "Sadece 'ok' yaz." }],
      tools: [],
      apiKey: t.target.apiKey,
      baseUrl: t.target.baseUrl,
    })) {
      if (ev.type === "token") got += ev.text;
      else if (ev.type === "error") throw new Error(ev.message);
      else if (ev.type === "done") break;
      if (got.length > 40) break;
    }
    res.json({
      ok: true,
      provider: t.target.provider.id,
      model: t.target.model,
      pooled: t.target.pooled,
      sample: got.trim().slice(0, 40),
    });
  } catch (e: any) {
    // Sağlayıcı hata metninde anahtar geçebilir — kullanıcıya ham metin dönmeden kısalt.
    const msg = String(e?.message ?? e).replace(/\b(sk|gsk|xai|key)[-_][A-Za-z0-9_\-]{8,}/gi, "***");
    res.status(502).json({ ok: false, provider: t.target.provider.id, model: t.target.model, error: msg.slice(0, 300) });
  }
});
