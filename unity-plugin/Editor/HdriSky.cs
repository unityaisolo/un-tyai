using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// T3 — HDRI GÖKYÜZÜ. Poly Haven'dan inen .hdr panoramalarını gerçek gökyüzü olarak uygular.
    /// KRİTİK: .hdr bellekte LoadImage ile açılamaz → projeye import edilir (Assets/Nova/Skies).
    /// KUSURSUZLUK: HDR'ın en parlak noktası bulunup GÜNEŞ O YÖNE döndürülür; böylece
    /// gölgeler gökyüzündeki güneşle hizalanır (Unity AI dahil kimse bunu otomatik yapmıyor).
    /// </summary>
    public static class HdriSky
    {
        private const string SkyFolder = "Assets/Nova/Skies";

        public class Entry
        {
            public string Id, Title, Mood, File, Dir;
            public string Label => $"{Mood} · {Title}";
        }

        private static List<Entry> _cache;

        /// <summary>skies-raw klasöründeki HDRI'ları listeler (hava durumuna göre sıralı).</summary>
        public static List<Entry> Available(bool force = false)
        {
            if (_cache != null && !force) return _cache;
            var list = new List<Entry>();
            try
            {
                string root = Path.Combine(Path.GetDirectoryName(UnityAIConfig.CatalogPath) ?? "", "skies-raw");
                if (Directory.Exists(root))
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        string metaPath = Path.Combine(dir, "meta.json");
                        if (!File.Exists(metaPath)) continue;
                        var m = JsonUtility.FromJson<SkyMeta>(File.ReadAllText(metaPath));
                        if (m == null || string.IsNullOrEmpty(m.file)) continue;
                        string hdr = Path.Combine(dir, m.file);
                        if (!File.Exists(hdr)) continue;
                        string title = string.IsNullOrEmpty(m.title) ? Path.GetFileName(dir) : m.title;
                        string id = string.IsNullOrEmpty(m.id) ? Path.GetFileName(dir) : m.id;
                        // SADECE GÖKYÜZÜ: "Pure Sky" panoramalarında zemin yoktur. Diğerleri
                        // 360° fotoğraf olduğu için sahnenin altına çimen/asfalt zemin basıyordu.
                        bool pureSky = title.IndexOf("pure sky", StringComparison.OrdinalIgnoreCase) >= 0
                                       || id.IndexOf("puresky", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!pureSky) continue;

                        list.Add(new Entry
                        {
                            Id = id,
                            Title = title.Replace(" (Pure Sky)", "").Replace("(Pure Sky)", "").Trim(),
                            Mood = string.IsNullOrEmpty(m.mood) ? "Gündüz" : m.mood,
                            File = hdr, Dir = dir,
                        });
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Nova Gökyüzü] Liste okunamadı: " + e.Message); }

            // Hava durumu sırası: gündüz → gün batımı → bulutlu → gece
            var order = new List<string> { "Açık gündüz", "Gündüz", "Gün batımı", "Bulutlu", "Gece" };
            _cache = list.OrderBy(e => { int i = order.IndexOf(e.Mood); return i < 0 ? 99 : i; })
                         .ThenBy(e => e.Title).ToList();
            return _cache;
        }

        [Serializable] private class SkyMeta { public string id, title, mood, file; }

        public static void Apply(int index, Action<string> log)
        {
            var list = Available();
            if (list.Count == 0)
            {
                log?.Invoke(NovaLocale.T("sky.hdriNotFound"));
                return;
            }
            Apply(list[Mathf.Clamp(index, 0, list.Count - 1)], log);
        }

        public static void Apply(Entry e, Action<string> log)
        {
            try
            {
                // 1) PROJEYE IMPORT (.hdr bellekte açılamaz)
                string assetPath = ImportHdr(e);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null) { log?.Invoke(NovaLocale.T("sky.hdriImportFailed", e.Title)); return; }

                // 2) PANORAMİK SKYBOX
                var shader = Shader.Find("Skybox/Panoramic");
                if (shader == null) { log?.Invoke(NovaLocale.T("sky.panoramicShaderMissing")); return; }
                var mat = new Material(shader) { name = "NovaSky_" + e.Id };
                mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_Mapping")) mat.SetFloat("_Mapping", 1f);      // Latitude-Longitude
                if (mat.HasProperty("_ImageType")) mat.SetFloat("_ImageType", 0f);  // 360°
                if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", e.Mood == "Gece" ? 1.6f : 1.0f);
                if (mat.HasProperty("_Rotation")) mat.SetFloat("_Rotation", 0f);

                RenderSettings.skybox = mat;
                // Ortam ışığı gökyüzünden gelsin (fotogerçekçiliğin yarısı budur)
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.fog = false;

                // 3) GÖKYÜZÜNÜ ÖLÇ — etiketler (Poly Haven isim/etiketleri) yanıltıcıydı:
                // "Kloppenheim" gece sanılıp aslında gündüz çıkıyordu. Artık gerçek parlaklığa
                // ve güneş yüksekliğine bakıp hava durumunu KENDİMİZ belirliyoruz.
                var sun = FindOrCreateSun();
                string sunInfo = NovaLocale.T("sky.sunFixed");
                var measured = Measure(tex);
                if (measured.mood != e.Mood && !string.IsNullOrEmpty(measured.mood))
                {
                    Debug.Log($"[Nova Gökyüzü] Etiket düzeltildi: '{e.Mood}' → '{measured.mood}' ({e.Title})");
                    e.Mood = measured.mood;
                    PersistMood(e); // meta.json'a yaz → menü bir dahakine doğru gösterir
                }

                if (measured.hasSun)
                {
                    sun.transform.rotation = Quaternion.LookRotation(-measured.dir);
                    sun.color = measured.color;
                    sunInfo = NovaLocale.T("sky.sunAligned", Mathf.Round(90f - Vector3.Angle(Vector3.up, measured.dir)));
                }

                switch (e.Mood)
                {
                    case "Gece":
                        sun.intensity = 0.06f;               // ay ışığı
                        sun.color = new Color(0.55f, 0.62f, 0.9f);
                        sun.shadows = LightShadows.None;      // gece keskin gölge olmaz
                        if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.8f);
                        break;
                    case "Bulutlu":
                        sun.intensity = 0.35f;
                        sun.shadows = LightShadows.None;      // bulutlu havada gölge yumuşak/yok
                        break;
                    case "Gün batımı":
                        sun.intensity = 0.85f; sun.shadows = LightShadows.Soft; break;
                    default:
                        sun.intensity = 1.2f; sun.shadows = LightShadows.Soft; break;
                }
                RenderSettings.sun = sun;

                DynamicGI.UpdateEnvironment();
                log?.Invoke(NovaLocale.T("sky.applied", e.Label, sunInfo));
                Debug.Log($"[Nova Gökyüzü] {e.Label} → {assetPath} · {sunInfo}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                log?.Invoke(NovaLocale.T("sky.applyFailed", ex.Message));
            }
        }

        private static string ImportHdr(Entry e)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Nova")) AssetDatabase.CreateFolder("Assets", "Nova");
            if (!AssetDatabase.IsValidFolder(SkyFolder)) AssetDatabase.CreateFolder("Assets/Nova", "Skies");

            string dest = $"{SkyFolder}/{Path.GetFileName(e.File)}";
            if (!File.Exists(dest))
            {
                File.Copy(e.File, dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            }
            // Güneş analizi için okunabilir olmalı
            var imp = AssetImporter.GetAtPath(dest) as TextureImporter;
            if (imp != null && (!imp.isReadable || imp.textureShape != TextureImporterShape.Texture2D))
            {
                imp.textureShape = TextureImporterShape.Texture2D;
                imp.isReadable = true;
                imp.mipmapEnabled = false;
                imp.maxTextureSize = 2048;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.SaveAndReimport();
            }
            return dest;
        }

        public struct SkyMeasure
        {
            public bool hasSun; public Vector3 dir; public Color color;
            public float avgLum, peakLum, elevation; public string mood;
        }

        /// <summary>
        /// HDR'ı OKUYARAK hava durumunu belirler: ortalama parlaklık (gece/gündüz),
        /// tepe/ortalama oranı (açık ↔ bulutlu) ve güneş yüksekliği (gün batımı).
        /// İsim/etiket tahmininden çok daha güvenilir.
        /// </summary>
        public static SkyMeasure Measure(Texture2D tex)
        {
            var r = new SkyMeasure { dir = Vector3.up, color = Color.white, mood = null };
            if (!TryFindSunDirection(tex, out var dir, out var col, out float avg, out float peak)) return r;
            r.hasSun = true; r.dir = dir; r.color = col; r.avgLum = avg; r.peakLum = peak;
            r.elevation = 90f - Vector3.Angle(Vector3.up, dir);

            float ratio = avg > 1e-5f ? peak / avg : 999f; // güneş ne kadar baskın?
            if (avg < 0.06f) r.mood = "Gece";
            else if (ratio < 6f) r.mood = "Bulutlu";        // dağınık ışık = bulut örtüsü
            else if (r.elevation < 12f) r.mood = "Gün batımı";
            else r.mood = "Açık gündüz";
            return r;
        }

        /// <summary>Ölçülen hava durumunu meta.json'a yazar (menü etiketleri kalıcı düzelir).</summary>
        private static void PersistMood(Entry e)
        {
            try
            {
                string metaPath = Path.Combine(e.Dir, "meta.json");
                if (!File.Exists(metaPath)) return;
                string json = File.ReadAllText(metaPath);
                json = System.Text.RegularExpressions.Regex.IsMatch(json, "\"mood\"\\s*:")
                    ? System.Text.RegularExpressions.Regex.Replace(json, "\"mood\"\\s*:\\s*\"[^\"]*\"", $"\"mood\": \"{e.Mood}\"")
                    : json.TrimEnd().TrimEnd('}') + $",\n  \"mood\": \"{e.Mood}\"\n}}";
                File.WriteAllText(metaPath, json);
                _cache = null; // liste tazelensin
            }
            catch { /* yazamazsak sorun değil, oturum içinde doğru çalışır */ }
        }

        /// <summary>
        /// Panoramadaki en parlak bölgeyi bulur → güneş yönü + rengi (+ parlaklık istatistikleri).
        /// Lat/Long haritada: u → yatay açı (0..360), v → dikey açı (-90..90).
        /// </summary>
        private static bool TryFindSunDirection(Texture2D tex, out Vector3 dir, out Color color,
            out float avgLum, out float peakLum)
        {
            dir = Vector3.up; color = Color.white; avgLum = 0f; peakLum = 0f;
            try
            {
                int step = Mathf.Max(1, tex.width / 256); // hızlı örnekleme
                float best = -1f; int bx = 0, by = 0;
                double sum = 0; int samples = 0;
                var px = tex.GetPixels(0);
                int w = tex.width, h = tex.height;
                for (int y = h / 2; y < h; y += step)       // yalnız ufkun üstü (gökyüzü)
                for (int x = 0; x < w; x += step)
                {
                    var c = px[y * w + x];
                    float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    sum += lum; samples++;
                    if (lum > best) { best = lum; bx = x; by = y; }
                }
                if (best <= 0f || samples == 0) return false;
                avgLum = (float)(sum / samples);
                peakLum = best;

                float u = (bx + 0.5f) / w;                  // 0..1 yatay
                float v = (by + 0.5f) / h;                  // 0..1 dikey
                float theta = (u - 0.5f) * Mathf.PI * 2f;   // -π..π
                float phi = (v - 0.5f) * Mathf.PI;          // -π/2..π/2 (yükseklik)
                dir = new Vector3(
                    Mathf.Cos(phi) * Mathf.Sin(theta),
                    Mathf.Sin(phi),
                    Mathf.Cos(phi) * Mathf.Cos(theta)).normalized;
                if (dir.y < 0.05f) dir = new Vector3(dir.x, 0.05f, dir.z).normalized; // ufkun altına düşmesin

                var raw = px[by * w + bx];
                float m = Mathf.Max(raw.r, Mathf.Max(raw.g, raw.b));
                color = m > 0.001f ? new Color(raw.r / m, raw.g / m, raw.b / m) : Color.white;
                color = Color.Lerp(color, Color.white, 0.35f); // aşırı renkli güneşi yumuşat
                return true;
            }
            catch { return false; } // doku okunamıyorsa sessizce sabit güneş
        }

        private static Light FindOrCreateSun()
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) return l;
            var go = new GameObject("NovaSun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            Undo.RegisterCreatedObjectUndo(go, "Nova: Güneş");
            return light;
        }
    }
}
