import { Router, type Request, type Response } from "express";

export const assetsRouter = Router();

/**
 * ASSET DAĞITIMI (beta)
 *
 * Plugin, model kütüphanesini artık geliştiricinin diskinden değil buluttan alır.
 * Bu uç, indirme adreslerini TEK YERDEN bildirir; böylece depolama sağlayıcısı
 * (Firebase Storage / R2 / S3) değişse bile yayınlanmış plugin'i güncellemek gerekmez.
 *
 * NEDEN VARSAYILANLAR KODDA:
 *   Adresler eskiden yalnızca .env'den okunuyordu. Ama .env gitignore'da (içinde API
 *   anahtarları var) — yani depoyu klonlayan HİÇBİR kullanıcıda bu değişkenler yoktu
 *   ve uç herkese 503 dönüyordu. Kütüphane indirme özelliği yalnızca geliştiricinin
 *   makinesinde çalışıyordu. Beta testinde tam olarak bu çıktı.
 *
 *   Bu adresler GİZLİ DEĞİL: Firebase Storage'ın herkese açık okuma URL'leri, token
 *   içermiyor, zaten her indiren kullanıcının ağ trafiğinde görünüyor. Dolayısıyla
 *   kodda durmaları güvenlik sorunu değil. (Gizli olan tek şey API anahtarları ve
 *   onlar kasada/.env'de kalmaya devam ediyor.)
 *
 * Env ile EZİLEBİLİR — depolama sağlayıcısı değişirse .env yeter, kod değişmez:
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

const BUCKET = "unityai-dd9c1.firebasestorage.app";
const FB = `https://firebasestorage.googleapis.com/v0/b/${BUCKET}/o`;

const DEFAULT_CATALOG_URL = `${FB}/catalog.json?alt=media`;
const DEFAULT_BASE_URL = `${FB}/assets-raw%2F{file}?alt=media`;
const DEFAULT_TEXTURES_ZIP_URL = `${FB}/textures-raw.zip?alt=media`;

/** Boş string'i "tanımsız" say: .env'de `NOVA_ASSET_BASE_URL=` yazması varsayılanı silmemeli. */
const envOr = (name: string, fallback: string): string => {
  const v = (process.env[name] ?? "").trim();
  return v.length > 0 ? v : fallback;
};

assetsRouter.get("/assets/manifest", (_req: Request, res: Response) => {
  const catalogUrl = envOr("NOVA_ASSET_CATALOG_URL", DEFAULT_CATALOG_URL);
  const baseUrl = envOr("NOVA_ASSET_BASE_URL", DEFAULT_BASE_URL);

  res.json({
    version: envOr("NOVA_ASSET_VERSION", "1"),
    catalogUrl,
    // Yol biçiminde sonda / olmasını garantile ({file} şablonuna dokunma)
    assetsBaseUrl: baseUrl.includes("{file}") || baseUrl.endsWith("/") ? baseUrl : baseUrl + "/",
    texturesZipUrl: envOr("NOVA_TEXTURES_ZIP_URL", DEFAULT_TEXTURES_ZIP_URL),
  });
});
