import { Router, type Request, type Response } from "express";

export const assetsRouter = Router();

/**
 * ASSET DAĞITIMI (beta)
 *
 * Plugin, model kütüphanesini artık geliştiricinin diskinden değil buluttan alır.
 * Bu uç, indirme adreslerini TEK YERDEN bildirir; böylece depolama sağlayıcısı
 * (Firebase Storage / R2 / S3) değişse bile yayınlanmış plugin'i güncellemek gerekmez.
 *
 * Env:
 *   NOVA_ASSET_CATALOG_URL  catalog.json'un tam URL'i
 *   NOVA_ASSET_BASE_URL     GLB kökü. İki biçim desteklenir:
 *                             yol biçimi: "https://cdn/…/assets-raw/"  (dosya adı sona eklenir)
 *                             şablon:     "https://…/o/assets-raw%2F{file}?alt=media"
 *                                         ({file} tümüyle URL-kodlanır — Firebase v0 API biçimi)
 *   NOVA_TEXTURES_ZIP_URL   (opsiyonel) textures-raw paketinin zip URL'i
 *   NOVA_ASSET_VERSION      (opsiyonel) sürüm etiketi; değişince plugin katalogu yeniler
 *
 * Not: GLB'ler talep üzerine (lazy) indirilir — kullanıcı tüm kütüphaneyi çekmez,
 * yalnızca sahnede kullanılan modelleri indirir.
 */
assetsRouter.get("/assets/manifest", (_req: Request, res: Response) => {
  const catalogUrl = process.env.NOVA_ASSET_CATALOG_URL ?? "";
  const baseUrl = process.env.NOVA_ASSET_BASE_URL ?? "";

  if (!catalogUrl || !baseUrl) {
    return res.status(503).json({
      error: "Asset dağıtımı yapılandırılmamış",
      detail:
        "Sunucuda NOVA_ASSET_CATALOG_URL ve NOVA_ASSET_BASE_URL tanımlı değil. " +
        "Kütüphaneyi elle indirip Unity'de UnityAI ▸ Asset Kütüphanesi… ile klasörü seçebilirsin.",
    });
  }

  res.json({
    version: process.env.NOVA_ASSET_VERSION ?? "1",
    catalogUrl,
    // Yol biçiminde sonda / olmasını garantile ({file} şablonuna dokunma)
    assetsBaseUrl: baseUrl.includes("{file}") || baseUrl.endsWith("/") ? baseUrl : baseUrl + "/",
    texturesZipUrl: process.env.NOVA_TEXTURES_ZIP_URL ?? null,
  });
});
