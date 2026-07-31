using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NovaWorld;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// 3D SONSUZ KOŞU şablonu kurucu. Tek tıkla oynanabilir bir runner sahnesi kurar:
    /// oyuncu + takip kamerası + ışık + NovaRunner kontrolcüsü. Engel şablonlarını
    /// katalogdan (rock/prop/barrel) alır; bulunamazsa NovaRunner primitive'lere düşer.
    /// Play'e basınca oyun başlar (A/D şerit, Space zıpla, R yeniden başla).
    /// </summary>
    public static class RunnerBuilder
    {
        public const string RootName = "NovaRunnerGame";
        private static bool _busy;

        public static async void Build(Action<string> log, bool enterPlay = false)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return; // derleme/import sırasında GPU çökmesini önle
            if (_busy) { log?.Invoke("Koşu sahnesi zaten kuruluyor."); return; }
            _busy = true;
            NovaAssetLibrary.WarnIfMissing(log); // kütüphane yoksa engellemez, sebebini söyler
            bool _shaderSync = NovaEditorGuard.BeginSyncShaders(); // DX12 çökme önlemi
            try
            {
                var old = GameObject.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old);

                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Nova: Sonsuz Koşu");

                // ---- Oyuncu ----
                var player = new GameObject("Runner");
                player.transform.SetParent(root.transform);
                player.transform.position = new Vector3(0f, 1f, 0f);
                var runner = player.AddComponent<NovaRunner>();

                // ---- Işık (sahne karanlık kalmasın) ----
                if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
                {
                    var sun = new GameObject("Sun");
                    sun.transform.SetParent(root.transform);
                    var l = sun.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                    l.intensity = 1.1f;
                }

                // ---- Kamera ----
                if (Camera.main == null)
                {
                    var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                    camGo.transform.SetParent(root.transform);
                    camGo.AddComponent<Camera>();
                    camGo.AddComponent<AudioListener>();
                    // çift AudioListener uyarısını önle
                    foreach (var al in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                        if (al.gameObject != camGo) al.enabled = false;
                }

                // ---- Gerçek zemin/ray dokusu (textures-raw'dan) ----
                runner.groundMaterial = MakeMaterial("road|asphalt|pavement|concrete|ground0|gravel", new Color(0.32f, 0.33f, 0.36f));
                runner.railMaterial = MakeMaterial("metal|rust|concrete|rock", new Color(0.2f, 0.22f, 0.26f));

                // ---- Engel + coin + oyuncu şablonları (katalogdan) ----
                log?.Invoke("Gerçek modeller hazırlanıyor...");
                var obstacles = await LoadObstacleTemplates(root.transform, log);
                if (obstacles.Count > 0) runner.obstacles = obstacles.ToArray();

                runner.coin = await LoadOne(root.transform, new[] { "coin", "gem", "star", "ring", "token", "gold", "crystal" }, null);
                runner.playerModel = await LoadOne(root.transform, new[] { "robot", "droid", "knight", "astronaut", "player", "hero", "mech" }, "character");

                string extras = (runner.groundMaterial != null ? "zemin dokusu ✓ " : "")
                              + (runner.coin != null ? "coin ✓ " : "")
                              + (runner.playerModel != null ? "karakter ✓ " : "");
                log?.Invoke(obstacles.Count > 0
                    ? $"Sonsuz koşu hazır: {obstacles.Count} engel · {extras}— Play'e bas (A/D şerit, Space zıpla, S kay)."
                    : "Sonsuz koşu hazır (engeller primitive — katalog/glTFast yok). Play'e bas.");

                Selection.activeGameObject = root;
                if (enterPlay && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { log?.Invoke("Koşu kurulamadı: " + e.Message); Debug.LogException(e); }
            finally { _busy = false; NovaEditorGuard.EndSyncShaders(_shaderSync); }
        }

        // Katalogdan birkaç engel çeşidi import edip pasif şablon olarak sahnede tutar.
        private static async Task<List<GameObject>> LoadObstacleTemplates(Transform parent, Action<string> log)
        {
            var list = new List<GameObject>();
#if GLTFAST_INSTALLED
            try
            {
                AssetCatalog.Load(null, true);
                var pool = new List<AssetCatalog.Entry>();
                foreach (var role in new[] { "rock", "prop", "bush" })
                    pool.AddRange(AssetCatalog.FilterRoles(new[] { role }, "any"));
                // makul boyutlu, düşük poli engeller
                pool.RemoveAll(e => e == null || e.triangles < 0 || e.triangles > 40000);
                var rnd = new System.Random();
                int want = 5, tries = 0;
                foreach (var e in Shuffle(pool, rnd))
                {
                    if (list.Count >= want || tries > 20) break;
                    tries++;
                    var go = await Import(e);
                    if (go == null) continue;
                    go.name = "obs_" + e.name;
                    go.transform.SetParent(parent);
                    go.SetActive(false);
                    go.hideFlags = HideFlags.None;
                    list.Add(go);
                }
            }
            catch (Exception e) { log?.Invoke("Şablon yükleme atlandı: " + e.Message); }
#else
            await Task.CompletedTask;
#endif
            return list;
        }

        // textures-raw'dan desene uyan ilk _Color(+Normal) dokusundan URP/Lit materyali kur.
        private static Material MakeMaterial(string pattern, Color fallback)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "NovaRunner_" + pattern.Split('|')[0] };
            void SetCol(Color c) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); }
            void SetTex(Texture2D t) { if (t == null) return; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            try
            {
                var texRoot = Path.Combine(Path.GetDirectoryName(UnityAIConfig.CatalogPath) ?? "", "textures-raw");
                if (Directory.Exists(texRoot))
                {
                    var re = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (var dir in Directory.GetDirectories(texRoot))
                    {
                        if (!re.IsMatch(Path.GetFileName(dir))) continue;
                        var col = Directory.GetFiles(dir).FirstOrDefault(f => f.Contains("_Color"));
                        if (col == null) continue;
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
                        tex.LoadImage(File.ReadAllBytes(col));
                        SetTex(tex); SetCol(Color.white);
                        return m;
                    }
                }
            }
            catch { }
            SetCol(fallback); // doku yoksa düz renk
            return m;
        }

        // Katalogda önce isim desenlerinden, sonra (varsa) rolden bir asset bulup import eder.
        private static async Task<GameObject> LoadOne(Transform parent, string[] namePatterns, string role)
        {
#if GLTFAST_INSTALLED
            try
            {
                AssetCatalog.Load(null, true);
                var all = new List<AssetCatalog.Entry>();
                if (!string.IsNullOrEmpty(role)) all.AddRange(AssetCatalog.FilterRoles(new[] { role }, "any"));
                var re = new System.Text.RegularExpressions.Regex(string.Join("|", namePatterns), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var byName = AssetCatalog.Load().Where(e => e != null && !string.IsNullOrEmpty(e.name) && re.IsMatch(e.name)
                                                            && e.triangles >= 0 && e.triangles <= 60000).ToList();
                foreach (var e in byName.Concat(all))
                {
                    var go = await Import(e);
                    if (go == null) continue;
                    go.transform.SetParent(parent);
                    go.SetActive(false);
                    return go;
                }
            }
            catch { }
#else
            await Task.CompletedTask;
#endif
            return null;
        }

        private static IEnumerable<AssetCatalog.Entry> Shuffle(List<AssetCatalog.Entry> src, System.Random rnd)
        {
            var a = new List<AssetCatalog.Entry>(src);
            for (int i = a.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
            return a;
        }

#if GLTFAST_INSTALLED
        private static async Task<GameObject> Import(AssetCatalog.Entry e)
        {
            try
            {
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var settings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                // Model yerelde yoksa buluttan indir (lazy dağıtım); indirilemezse atla
                var uri = await NovaAssetDownloader.EnsureUri(e);
                if (string.IsNullOrEmpty(uri)) return null;
                if (!await gltf.Load(uri, settings)) return null;
                var go = new GameObject(e.name);
                var inst = new GLTFast.GameObjectInstantiator(gltf, go.transform, null,
                    new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh });
                if (!await gltf.InstantiateMainSceneAsync(inst)) { UnityEngine.Object.DestroyImmediate(go); return null; }
                NovaMeshFix.Repair(go);
                return go;
            }
            catch { return null; }
        }
#endif
    }
}
