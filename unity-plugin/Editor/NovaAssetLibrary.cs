using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// ASSET KÜTÜPHANESİ YOLU ÇÖZÜCÜ (beta için kritik).
    ///
    /// Eskiden katalog yolu sabit kodluydu (E:\...\catalog.json) — başka hiçbir bilgisayarda
    /// çalışmıyordu. Artık şu sırayla otomatik bulunur:
    ///   1) Kullanıcının EditorPrefs'te kaydettiği yol (elle seçtiyse)
    ///   2) Projenin İÇİ:  &lt;Proje&gt;/NovaAssets/catalog.json  (indirilen kütüphanenin yeri)
    ///   3) Assets altında: Assets/**/NovaAssets/catalog.json  (kullanıcı buraya koyduysa)
    ///   4) Projenin YANI: &lt;Proje ebeveyni&gt;/nova-assets|asset-pipeline/catalog.json (geliştirici düzeni)
    ///   5) Paketin yanı:  &lt;paket kökü&gt;/../asset-pipeline/catalog.json (repo klonu)
    /// Bulunamazsa kullanıcıya "klasör seç" penceresi açılır ve seçim kalıcı kaydedilir.
    ///
    /// UPM (git URL) paketleri SALT OKUNURDUR — bu yüzden indirilen assetler asla paket
    /// klasörüne değil, DownloadRoot (proje içi NovaAssets) altına yazılır.
    /// </summary>
    public static class NovaAssetLibrary
    {
        private const string PrefKey = "UnityAI.CatalogPath";
        public const string FolderName = "NovaAssets";

        /// <summary>İndirilen kütüphanenin yazılabilir hedefi: &lt;Proje&gt;/NovaAssets</summary>
        public static string DownloadRoot
        {
            get
            {
                try { return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, FolderName); }
                catch { return Path.Combine(Application.dataPath, FolderName); }  // teorik; asla boş dönme
            }
        }

        /// <summary>Kayıtlı yol (yoksa boş). Ayarlar panelinden değiştirilebilir.</summary>
        public static string SavedPath
        {
            get => EditorPrefs.GetString(PrefKey, "");
            set => EditorPrefs.SetString(PrefKey, value ?? "");
        }

        /// <summary>
        /// catalog.json'un tam yolu. Bulunamazsa null döner (çağıran kullanıcıya haber verir).
        /// prompt=true ise bulunamadığında klasör seçme penceresi açar.
        /// </summary>
        // Arama sonucu belleği: CatalogPath her AssetCatalog.Load ve her materyal kurulumunda
        // okunuyor; kütüphane yokken her seferinde AssetDatabase taraması yapmak pahalı.
        private static bool _missCached;

        public static string ResolveCatalog(bool prompt = false)
        {
            // 1) Kullanıcının kaydettiği yol
            var saved = SavedPath;
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

            if (!_missCached)
            {
                foreach (var c in Candidates())
                    if (File.Exists(c)) { SavedPath = c; _missCached = false; return c; }  // bulundu → kalıcı kaydet
                _missCached = true;   // bu seansta bir daha tarama yapma
            }

            if (!prompt) return null;
            return AskUserForFolder();
        }

        /// <summary>Aramayı sıfırlar (indirme/klasör seçimi sonrası).</summary>
        public static void ForgetSearch() => _missCached = false;

        /// <summary>Aranacak olası konumlar (sırayla).</summary>
        private static System.Collections.Generic.IEnumerable<string> Candidates()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

            // 2) Proje içi indirme klasörü (beta akışının hedefi)
            yield return Path.Combine(DownloadRoot, "catalog.json");

            // 3) Assets altında NovaAssets klasörü (kullanıcı elle koyduysa)
            yield return Path.Combine(Application.dataPath, FolderName, "catalog.json");

            // 4) Geliştirici düzeni: proje kökünden yukarı 4 seviye tara.
            //    Unity projesi deponun yanında/içinde/altında olabilir; sabit tek seviye yetmez.
            var dir = projectRoot;
            for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir); up++)
            {
                yield return Path.Combine(dir, "nova-assets", "catalog.json");
                yield return Path.Combine(dir, "asset-pipeline", "catalog.json");
                yield return Path.Combine(dir, "nominal-agent", "asset-pipeline", "catalog.json");
                dir = Directory.GetParent(dir)?.FullName;
            }

            // 5) Paketin yanı (repo klonu: <repo>/unity-plugin + <repo>/asset-pipeline)
            string pkg = PackageRoot();
            if (!string.IsNullOrEmpty(pkg))
            {
                var upDir = Directory.GetParent(pkg)?.FullName;
                if (!string.IsNullOrEmpty(upDir))
                    yield return Path.Combine(upDir, "asset-pipeline", "catalog.json");
            }
        }

        /// <summary>Bu paketin disk üzerindeki kökü (UPM cache veya yerel klasör).</summary>
        private static string PackageRoot()
        {
            try
            {
                // Bu dosyanın asset yolundan paket kökünü türet
                var guids = AssetDatabase.FindAssets("NovaAssetLibrary t:Script");
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (!p.EndsWith("NovaAssetLibrary.cs")) continue;
                    var full = Path.GetFullPath(p);                  // Packages/... → gerçek yol
                    var dir = Path.GetDirectoryName(full);            // .../Editor
                    return Directory.GetParent(dir)?.FullName;        // paket kökü
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Kütüphane yoksa kullanıcıya iki yol sunar: buluttan indir (beta akışı) veya
        /// diskteki klasörü seç (geliştirici / çevrimdışı akış).
        /// </summary>
        public static string AskUserForFolder()
        {
            // 0 = Buluttan indir, 1 = Vazgeç, 2 = Klasör seç
            int choice = EditorUtility.DisplayDialogComplex(
                NovaLocale.T("lib.missing.title"),
                NovaLocale.T("lib.missing.body", FolderName),
                NovaLocale.T("lib.cloudBtn"), NovaLocale.T("dialog.cancel"), NovaLocale.T("lib.missing.pick"));

            if (choice == 1) return null;
            if (choice == 0)
            {
                // İndirme asenkron: bitince katalog kalıcı kaydedilir, kullanıcı işlemi yineler.
                _ = DownloadThenReport();
                Debug.Log("[Nova] " + NovaLocale.T("lib.downloading"));
                return null;
            }

            return PickFolder();
        }

        /// <summary>
        /// Buluttan indirmeyi başlatır ve SONUCU KULLANICIYA BİLDİRİR.
        ///
        /// NEDEN AYRI METOT: indirme "ateşle-unut" çağrılıyordu. Başarısız olduğunda
        /// kullanıcıya yalnızca konsola bir satır düşüyordu; ne dosya vardı ne de yeni
        /// bir seçim şansı — ve manifest hatası seans boyunca önbelleğe alındığı için
        /// "tekrar dene" de sessizce hiçbir şey yapmıyordu. Beta testinde tam olarak
        /// bu çıkmaz yaşandı. Artık: başarı da başarısızlık da diyalogla bildirilir,
        /// hata durumunda önbellek temizlenir ve elle klasör seçme yolu açık kalır.
        /// </summary>
        private static async Task DownloadThenReport()
        {
            string reason = null;
            string path = null;
            try
            {
                path = await NovaAssetDownloader.DownloadCatalog(s =>
                {
                    reason = s;                       // son mesaj = başarısızlık sebebi
                    Debug.Log("[Nova] " + s);
                });
            }
            catch (Exception e) { reason = e.Message; }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _missCached = false;                  // arama önbelleğini aç, yol artık var
                EditorUtility.DisplayDialog(NovaLocale.T("lib.dl.okTitle"), NovaLocale.T("lib.dl.okBody"), "OK");
                return;
            }

            // Başarısız: aynı seansta yeniden denenebilsin diye manifest önbelleğini temizle.
            NovaAssetDownloader.ResetCache();
            _missCached = false;

            bool pick = EditorUtility.DisplayDialog(
                NovaLocale.T("lib.dl.failTitle"),
                NovaLocale.T("lib.dl.failBody", string.IsNullOrEmpty(reason) ? "?" : reason),
                NovaLocale.T("lib.missing.pick"), NovaLocale.T("dialog.cancel"));
            if (pick) PickFolder();
        }

        /// <summary>Diskteki kütüphane klasörünü seçtirir; catalog.json'u bulursa kaydeder.</summary>
        private static string PickFolder()
        {
            string dir = EditorUtility.OpenFolderPanel(NovaLocale.T("lib.pick.title"), "", "");
            if (string.IsNullOrEmpty(dir)) return null;

            // Seçilen klasörde ya da bir alt seviyede catalog.json ara
            string direct = Path.Combine(dir, "catalog.json");
            if (File.Exists(direct)) { SavedPath = direct; _missCached = false; return direct; }
            try
            {
                var found = Directory.GetFiles(dir, "catalog.json", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) { SavedPath = found; _missCached = false; return found; }
            }
            catch { }

            EditorUtility.DisplayDialog(NovaLocale.T("lib.notfound.title"),
                NovaLocale.T("lib.notfound.body"), "OK");
            return null;
        }

        /// <summary>
        /// Kütüphane hazır mı? Değilse sebebi log'a yazar (kullanıcı ne yapacağını bilsin).
        /// Yalnızca catalog.json şarttır: GLB'ler yerelde yoksa NovaAssetDownloader talep
        /// üzerine buluttan indirir, bu yüzden 'assets-raw' yokluğu hata değil bilgidir.
        /// </summary>
        public static bool EnsureReady(Action<string> log, bool prompt = true)
        {
            // Bu metot kurucuların try bloğundan ÖNCE de çağrılıyor; buradan sızan bir
            // istisna _busy bayrağını kilitli bırakır. Bu yüzden tamamen istisna-güvenli.
            try
            {
                var path = ResolveCatalog(prompt);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var root = Path.Combine(Path.GetDirectoryName(path) ?? "", "assets-raw");
                    if (!Directory.Exists(root)) log?.Invoke(NovaLocale.T("lib.lazyModels"));
                    return true;
                }
                log?.Invoke(NovaLocale.T("lib.notReady"));
                Debug.LogWarning("[Nova] Asset kütüphanesi bulunamadı. 'UnityAI > Kütüphaneyi Buluttan İndir' ya da " +
                                 "'UnityAI > Asset Kütüphanesi…' ile klasör seçebilirsin. Beklenen konum: " + DownloadRoot);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nova] Kütüphane kontrolü başarısız: " + e.Message);
                return false;
            }
        }

        private static bool _promptedThisSession;

        /// <summary>
        /// Oyun/şablon kurucuları için YUMUŞAK kontrol: kütüphane yoksa kurulumu ENGELLEMEZ
        /// (sahne basit geometriyle kurulur) ama kullanıcıya sebebi bir kez söylenir.
        /// Seans başına en fazla bir kez klasör seçme penceresi açılır.
        /// </summary>
        public static bool WarnIfMissing(Action<string> log)
        {
            // Kurucularda _busy=true'dan SONRA, try'dan ÖNCE çağrılıyor → sızan istisna
            // kurucuyu kalıcı kilitler. Hiçbir koşulda fırlatmamalı.
            try
            {
                bool ready = EnsureReady(log, prompt: !_promptedThisSession);
                if (ready) return true;
                _promptedThisSession = true;
                log?.Invoke(NovaLocale.T("lib.simpleGeometry"));
            }
            catch (Exception e) { Debug.LogWarning("[Nova] " + e.Message); }
            return false;
        }

        /// <summary>
        /// Kayıtlı yolu ve arama belleğini siler — bir sonraki kullanımda sıfırdan aranır.
        ///
        /// NEDEN GEREKLİ: EditorPrefs proje bazlı DEĞİL, kullanıcı+Unity sürümü bazlıdır.
        /// Bir projede bulunan katalog yolu diğer TÜM projelere sızar. Bu yüzden "temiz
        /// kurulum" (beta kullanıcısı) senaryosu ancak bu sıfırlamayla test edilebilir.
        /// </summary>
        [MenuItem("UnityAI/Asset Kütüphanesini Sıfırla (temiz kurulum testi)", false, 202)]
        private static void MenuReset()
        {
            string cur = string.IsNullOrEmpty(SavedPath) ? "—" : SavedPath;
            if (!EditorUtility.DisplayDialog(
                    NovaLocale.T("lib.reset.title"),
                    NovaLocale.T("lib.reset.body", cur),
                    NovaLocale.T("lib.reset.ok"), NovaLocale.T("dialog.cancel")))
                return;

            SavedPath = "";
            ForgetSearch();
            _promptedThisSession = false;
            NovaAssetDownloader.ResetCache();
            Debug.Log("[Nova] Asset kütüphanesi kaydı silindi. Sonraki kurulumda yol yeniden aranacak. " +
                      "Yerel klasör bulunmazsa bulut akışı devreye girer. İndirme hedefi: " + DownloadRoot);
        }

        [MenuItem("UnityAI/Asset Kütüphanesi…", false, 200)]
        private static void MenuPick()
        {
            ForgetSearch();                       // menüden bakılıyorsa taze tara
            var cur = ResolveCatalog(false);
            int count = 0;
            if (!string.IsNullOrEmpty(cur))
                try { count = AssetCatalog.Load(cur, true).Count; } catch { }
            string msg = string.IsNullOrEmpty(cur)
                ? NovaLocale.T("lib.status.none", DownloadRoot)
                : NovaLocale.T("lib.status.ok", cur, count);
            if (EditorUtility.DisplayDialog(NovaLocale.T("lib.menu.title"), msg,
                    NovaLocale.T("lib.missing.pick"), NovaLocale.T("dialog.cancel")))
                AskUserForFolder();
        }
    }
}
