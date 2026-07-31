import { routeProvider } from "../providers/index.js";
import type { ChatProvider } from "../providers/types.js";
import { resolveKey, getSettings } from "./keyvault.js";
import { modelFor, type ModelRole } from "../aliases.js";
import { noKeyMessage } from "./nokey.js";

/**
 * TEK GİRİŞ NOKTASI: "şu kullanıcı, şu rol için hangi model + sağlayıcı + anahtar?"
 *
 * Neden gerekli: bu çözüm 6 ayrı yerde tekrar ediyordu ve her yeni özellik
 * (özel endpoint, rol bazlı model) hepsine ayrı ayrı eklenmek zorunda kalıyordu.
 * Artık tek yerden geçiyor — özel endpoint ve rol seçimi otomatik olarak her
 * akışta (sohbet, küratör, dekor, plan, arazi) geçerli.
 */
export interface ChatTarget {
  provider: ChatProvider;
  model: string;
  apiKey: string;
  pooled: boolean;
  /** Yalnız "custom" sağlayıcıda dolu — kullanıcının kendi endpoint'i. */
  baseUrl?: string;
}

export type TargetResult =
  | { ok: true; target: ChatTarget }
  | { ok: false; error: string };

export function resolveTarget(
  userId: string,
  role: ModelRole,
  requestedModel?: string,
): TargetResult {
  const model = modelFor(userId, role, requestedModel);

  let provider: ChatProvider;
  try {
    provider = routeProvider(model);
  } catch {
    return {
      ok: false,
      error:
        `'${model}' modeli hiçbir sağlayıcıya eşlenemedi. Özel bir servis kullanıyorsan ` +
        `model adını "custom/<model>" biçiminde yaz ve Ayarlar'da endpoint adresini gir.`,
    };
  }

  // Ollama yerelde çalışır — anahtar istemez.
  if (provider.id === "ollama")
    return { ok: true, target: { provider, model, apiKey: "", pooled: false } };

  if (provider.id === "custom") {
    const baseUrl = getSettings(userId).customBaseUrl?.trim();
    if (!baseUrl)
      return {
        ok: false,
        error: "Özel endpoint adresi girilmemiş. Ayarlar → Özel endpoint bölümüne taban adresi yaz (ör. https://api.together.xyz/v1).",
      };
    const key = resolveKey(userId, "custom");
    if (!key) return { ok: false, error: noKeyMessage("custom") };
    return { ok: true, target: { provider, model, apiKey: key.apiKey, pooled: key.pooled, baseUrl } };
  }

  const key = resolveKey(userId, provider.id);
  if (!key) return { ok: false, error: noKeyMessage(provider.id) };

  return { ok: true, target: { provider, model, apiKey: key.apiKey, pooled: key.pooled } };
}
