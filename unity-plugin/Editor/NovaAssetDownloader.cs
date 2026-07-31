using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// BULUT ASSET DAĞITIMI (beta).
    ///
    /// Kullanıcı paketi GitHub URL'i ile ekler; paket SALT OKUNURDUR ve içinde model yoktur.
    /// Modeller buluttan (Firebase Storage vb.) &lt;Proje&gt;/NovaAssets altına indirilir:
    ///
    ///   NovaAssets/catalog.json          ← bir kez indirilir
    ///   NovaAssets/assets-raw/&lt;dosya&gt;    ← TALEP ÜZERİNE (lazy) indirilir
    ///
    /// Neden lazy: kütüphanenin tamamı GB'larca; kullanıcı yalnızca sahnede kullanılan
    /// modelleri indirir. İlk kurulumda sadece catalog.json (birkaç yüz KB) çekilir.
    ///
    /// Adresler backend'in /v1/assets/manifest ucundan gelir — depolama sağlayıcısı
    /// değişse bile yayınlanmış plugin'i güncellemek gerekmez.
    /// </summary>
    public static class NovaAssetDownloader
    {
        [Serializable]
        private class Manifest
        {
            public string version;
            public string catalogUrl;
            public string assetsBaseUrl;
            public string texturesZipUrl;
        }

        private static Manifest _manifest;
        private static bool _manifestFailed;          // aynı seansta tekrar tekrar denemeyi engelle
        private static readonly HashSet<string> _failedFiles = new HashSet<string>();
        private static HttpClient _http;

        // TEŞHİS: modeller nereden geldi? ("yerelde vardı" vs "buluttan indirildi")
        private static int _hitLocal, _hitCache, _hitDownloaded;

        /// <summary>Sayaçları sıfırlar (her kurulum başında çağrılır).</summary>
        public static void ResetStats() { _hitLocal = _hitCache = _hitDownloaded = 0; }

        /// <summary>Son kurulumda modellerin nereden geldiğini tek satırda özetler.</summary>
        public static string StatsLine()
        {
            int total = _hitLocal + _hitCache + _hitDownloaded;
            if (total == 0) return null;
            var parts = new List<string>();
            if (_hitLocal > 0) parts.Add($"{_hitLocal} yerel klasör");
            if (_hitCache > 0) parts.Add($"{_hitCache} indirme önbelleği");
            if (_hitDownloaded > 0) parts.Add($"{_hitDownloaded} BULUTTAN indirildi");
            return "Model kaynağı: " + string.Join(" · ", parts);
        }

        private static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // ---------------------------------------------------------------- manifest

        private static async Task<Manifest> GetManifest(Action<string> log)
        {
            if (_manifest != null) return _manifest;
            if (_manifestFailed) return null;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{UnityAIConfig.BaseUrl}/v1/assets/manifest");
                req.Headers.Add("Authorization", "Bearer " + UnityAIConfig.ApiToken);
                var res = await Http.SendAsync(req);
                string body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    _manifestFailed = true;
                    log?.Invoke(NovaLocale.T("dl.noManifest"));
                    Debug.LogWarning("[Nova] /v1/assets/manifest: " + (int)res.StatusCode + " " + body);
                    return null;
                }
                var m = JsonUtility.FromJson<Manifest>(body);
                if (m == null || string.IsNullOrEmpty(m.catalogUrl) || string.IsNullOrEmpty(m.assetsBaseUrl))
                {
                    _manifestFailed = true;
                    log?.Invoke(NovaLocale.T("dl.noManifest"));
                    return null;
                }
                _manifest = m;
                return m;
            }
            catch (Exception e)
            {
                _manifestFailed = true;
                log?.Invoke(NovaLocale.T("dl.error", e.Message));
                return null;
            }
        }

        /// <summary>Buluttan yeniden okuma (menüden "yenile" için).</summary>
        public static void ResetCache()
        {
            _manifest = null; _manifestFailed = false; _failedFiles.Clear();
        }

        // ---------------------------------------------------------------- catalog

        /// <summary>
        /// catalog.json'u buluttan &lt;Proje&gt;/NovaAssets/catalog.json'a indirir.
        /// Başarılıysa yolu döndürür ve kalıcı olarak kaydeder.
        /// </summary>
        public static async Task<string> DownloadCatalog(Action<string> log)
        {
            var m = await GetManifest(log);
            if (m == null) return null;

            string dest = Path.Combine(NovaAssetLibrary.DownloadRoot, "catalog.json");
            try
            {
                Directory.CreateDirectory(NovaAssetLibrary.DownloadRoot);
                log?.Invoke(NovaLocale.T("dl.catalog"));
                var bytes = await Http.GetByteArrayAsync(m.catalogUrl);
                File.WriteAllBytes(dest, bytes);
                NovaAssetLibrary.SavedPath = dest;
                NovaAssetLibrary.ForgetSearch();
                AssetCatalog.Load(dest, true);
                log?.Invoke(NovaLocale.T("dl.catalogOk", AssetCatalog.Count));
                return dest;
            }
            catch (Exception e)
            {
                log?.Invoke(NovaLocale.T("dl.error", e.Message));
                Debug.LogWarning("[Nova] catalog indirilemedi: " + e);
                return null;
            }
        }

        // ---------------------------------------------------------------- lazy GLB

        /// <summary>
        /// Bir katalog kaydının yerel GLB yolunu döndürür; dosya yoksa buluttan indirir.
        /// Hiçbir şekilde bulunamazsa null döner (çağıran basit geometriye düşer).
        /// </summary>
        public static async Task<string> EnsureFile(AssetCatalog.Entry e, Action<string> log)
        {
            if (e == null || string.IsNullOrEmpty(e.file)) return null;

            // 1) Zaten yerelde mi? (geliştirici düzeni veya daha önce indirilmiş)
            string local = AssetCatalog.AbsolutePath(e);
            if (File.Exists(local)) { _hitLocal++; return local; }

            // 2) İndirme klasöründe mi?
            // Katalog verisi güvenilmez kabul edilir: mutlak yol / ".." ile klasör dışına çıkamasın.
            string rel = e.file.Replace('\\', '/').TrimStart('/', '~');
            if (rel.Contains("..") || Path.IsPathRooted(rel)) return null;
            string cacheRoot = Path.GetFullPath(Path.Combine(NovaAssetLibrary.DownloadRoot, "assets-raw"));
            string cached = Path.GetFullPath(Path.Combine(cacheRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!cached.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(cached)) { _hitCache++; return cached; }

            if (_failedFiles.Contains(e.file)) return null;   // bu seansta zaten başarısız

            // 3) Buluttan indir
            var m = await GetManifest(log);
            if (m == null) return null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                string url = BuildUrl(m.assetsBaseUrl, rel);
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes == null || bytes.Length < 32) throw new Exception("boş yanıt");
                File.WriteAllBytes(cached, bytes);
                _hitDownloaded++;
                return cached;
            }
            catch (Exception ex)
            {
                _failedFiles.Add(e.file);
                // Konsolu boğma: ilk 5 hatayı yaz, sonrasında tek özet satırı
                if (_failedFiles.Count <= 5)
                    Debug.LogWarning($"[Nova] '{e.file}' indirilemedi: {ex.Message}");
                else if (_failedFiles.Count == 6)
                    Debug.LogWarning("[Nova] Birden çok model indirilemedi — bulut kütüphanesi eksik/erişilemez olabilir.");
                return null;
            }
        }

        /// <summary>
        /// Dosya adını indirme adresine çevirir. İki biçimi de destekler:
        ///   • Yol biçimi:  "https://cdn/…/assets-raw/"  → segmentler ayrı ayrı kodlanır
        ///   • Şablon:      "https://…/o/assets-raw%2F{file}?alt=media" (Firebase v0 API)
        ///                  → {file} tümüyle kodlanır ('/' → %2F)
        /// </summary>
        private static string BuildUrl(string baseUrl, string rel)
        {
            if (baseUrl.Contains("{file}"))
                return baseUrl.Replace("{file}", Uri.EscapeDataString(rel));
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return baseUrl + string.Join("/", parts);
        }

        /// <summary>glTFast'in beklediği file:// URI — dosya yoksa indirir, olmazsa null.</summary>
        public static async Task<string> EnsureUri(AssetCatalog.Entry e, Action<string> log = null)
        {
            var path = await EnsureFile(e, log);
            return string.IsNullOrEmpty(path) ? null : new Uri(path).AbsoluteUri;
        }

        // ---------------------------------------------------------------- textures

        /// <summary>
        /// textures-raw paketini (zip) indirip &lt;Proje&gt;/NovaAssets/textures-raw altına açar.
        /// Dokular klasör taramasıyla eşleştiği için tek tek lazy indirilemez.
        /// </summary>
        public static async Task<bool> DownloadTextures(Action<string> log)
        {
            var m = await GetManifest(log);
            if (m == null || string.IsNullOrEmpty(m.texturesZipUrl))
            {
                log?.Invoke(NovaLocale.T("dl.noTextures"));
                return false;
            }
            string root = Path.Combine(NovaAssetLibrary.DownloadRoot, "textures-raw");
            if (Directory.Exists(root) && Directory.GetDirectories(root).Length > 0) return true;

            string zip = Path.Combine(Path.GetTempPath(), "nova-textures.zip");
            try
            {
                log?.Invoke(NovaLocale.T("dl.textures"));
                var bytes = await Http.GetByteArrayAsync(m.texturesZipUrl);
                File.WriteAllBytes(zip, bytes);
                Directory.CreateDirectory(root);
                // ZipFile.ExtractToDirectory bazı Unity profillerinde yok — ZipArchive her yerde var.
                using (var fs = File.OpenRead(zip))
                using (var arc = new System.IO.Compression.ZipArchive(fs,
                           System.IO.Compression.ZipArchiveMode.Read))
                {
                    string full = Path.GetFullPath(root);
                    foreach (var entry in arc.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;   // klasör kaydı
                        string outPath = Path.GetFullPath(Path.Combine(full, entry.FullName));
                        // Zip-slip koruması: hedef klasörün dışına yazma
                        if (!outPath.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                        using var src = entry.Open();
                        using var dst = File.Create(outPath);
                        await src.CopyToAsync(dst);
                    }
                }
                log?.Invoke(NovaLocale.T("dl.texturesOk"));
                return true;
            }
            catch (Exception e)
            {
                log?.Invoke(NovaLocale.T("dl.error", e.Message));
                return false;
            }
            finally { try { if (File.Exists(zip)) File.Delete(zip); } catch { } }
        }

        // ---------------------------------------------------------------- menü

        [MenuItem("UnityAI/Kütüphaneyi Buluttan İndir", false, 201)]
        private static async void MenuDownload()
        {
            void Log(string s) => Debug.Log("[Nova] " + s);
            ResetCache();
            var path = await DownloadCatalog(Log);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog(NovaLocale.T("lib.menu.title"),
                    NovaLocale.T("dl.failDialog"), "OK");
                return;
            }
            await DownloadTextures(Log);
            EditorUtility.DisplayDialog(NovaLocale.T("lib.menu.title"),
                NovaLocale.T("dl.okDialog", AssetCatalog.Count, NovaAssetLibrary.DownloadRoot), "OK");
        }
    }
}
