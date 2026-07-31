using UnityEditor;

namespace UnityAI
{
    /// <summary>
    /// Ağır işlemleri (terrain/dekor/koşu/model üretimi) YALNIZ editör "sakin" ken çalıştırır.
    ///
    /// NEDEN: Yeni/soğuk bir projede Unity açılışta aynı anda asset import eder ve shader
    /// derler (paralel compiler'lar). Bu "fırtına" sırasında biz de düzinelerce GLB import
    /// edip GPU'ya render komutu yollarsak, düşük VRAM'li kartlarda (ör. GTX 1650, 4 GB)
    /// DirectX 12 komut kuyruğu bozulup "Unrecoverable GPU device error" ile editör çöker.
    /// (Kanıt: bir kullanıcı crash log'unda D3D12Fence::Wait + 9 paralel shader compiler.)
    ///
    /// Bu koruma o çökmeyi kaynağında engeller: derleme/import bitene kadar işi reddeder.
    /// </summary>
    public static class NovaEditorGuard
    {
        /// <summary>Editör şu an ağır iş için uygun DEĞİL mi? reason kullanıcıya gösterilir.</summary>
        public static bool IsBusy(out string reason)
        {
            if (EditorApplication.isCompiling)
            {
                reason = "Unity script derliyor — bitmesini bekleyip tekrar dene.";
                return true;
            }
            if (EditorApplication.isUpdating)
            {
                reason = "Unity asset içe aktarıyor (import) — sağ alttaki işlem bitince tekrar dene.";
                return true;
            }
            reason = null;
            return false;
        }

        /// <summary>Kısa yol: meşgulse log'a yazıp true döner (çağıran return eder).</summary>
        public static bool BlockIfBusy(System.Action<string> log)
        {
            if (IsBusy(out var reason))
            {
                log?.Invoke("⏳ " + reason);
                UnityEngine.Debug.LogWarning("[Nova] İşlem ertelendi: " + reason);
                return true;
            }
            return false;
        }

        /// <summary>
        /// KRİTİK ÇÖKME ÖNLEMİ: Ağır GLB import/yerleştirme sırasında ASYNC shader derlemeyi
        /// geçici kapatır. Neden: yeni/soğuk projede bitki materyallerinin URP shader varyantları
        /// derlenmemiştir; async derleme, GPU ÇİZİM ORTASINDA shader'ı yerine koymaya çalışır ve
        /// zayıf DX12 sürücülerinde (ör. GTX 1650) komut listesini bozup "device lost" çökmesi
        /// yaratır. Senkron derlemede shader ÇİZİMDEN ÖNCE hazırlanır → çökme olmaz.
        /// Kullanım: var t = BeginSyncShaders(); try { ... } finally { EndSyncShaders(t); }
        /// </summary>
        public static bool BeginSyncShaders()
        {
            bool prev = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // senkron: çizimden önce derle
            return prev;
        }

        public static void EndSyncShaders(bool previous)
        {
            ShaderUtil.allowAsyncCompilation = previous;
        }
    }
}
