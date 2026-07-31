using UnityEditor;

namespace UnityAI
{
    /// <summary>
    /// Plugin ayarları. Sunucu adresi EditorPrefs'te saklanır; prod'a geçiş için
    /// tek yerden değiştirilir (sunucu odaklı mimari).
    /// </summary>
    public static class UnityAIConfig
    {
        private const string KeyBaseUrl = "UnityAI.BaseUrl";
        private const string Default = "http://localhost:8787";

        public static string BaseUrl
        {
            get => EditorPrefs.GetString(KeyBaseUrl, Default);
            set => EditorPrefs.SetString(KeyBaseUrl,
                string.IsNullOrEmpty(value) ? Default : value.TrimEnd('/'));
        }

        // Backend erişim token'ı. Dev modunda userId olarak kullanılır; backend
        // API_TOKENS kilidi açıksa buradaki token listede olmalı. Faz 5'te gerçek login.
        private const string KeyApiToken = "UnityAI.ApiToken";
        private const string DefaultToken = "demo-user";

        public static string ApiToken
        {
            get => EditorPrefs.GetString(KeyApiToken, DefaultToken);
            set => EditorPrefs.SetString(KeyApiToken,
                string.IsNullOrEmpty(value) ? DefaultToken : value.Trim());
        }

        // World Builder — asset kataloğunun (catalog.json) yolu.
        // ARTIK SABİT DEĞİL: NovaAssetLibrary yolu otomatik bulur (proje içi NovaAssets,
        // Assets altı, projenin yanı, paketin yanı) ve kullanıcı seçimini EditorPrefs'te saklar.
        // Böylece paket her bilgisayarda çalışır (beta şartı).
        // GLB'ler catalog ile aynı klasördeki "assets-raw" altında beklenir.
        public static string CatalogPath
        {
            get
            {
                var p = NovaAssetLibrary.ResolveCatalog(prompt: false);
                // Bulunamadıysa beklenen konumu döndür — hata mesajları anlamlı olsun
                return string.IsNullOrEmpty(p)
                    ? System.IO.Path.Combine(NovaAssetLibrary.DownloadRoot, "catalog.json")
                    : p;
            }
            set => NovaAssetLibrary.SavedPath = value;
        }
    }
}
