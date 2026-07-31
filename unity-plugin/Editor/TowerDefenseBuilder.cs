using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NovaWorld;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// KULE SAVUNMA kurucu. Kıvrımlı düşman yolu + üs + izometrik kamera kurar;
    /// düşman ve kule modelleri katalogdan (character / tower-civic rolleri) gelir.
    /// </summary>
    public static class TowerDefenseBuilder
    {
        public const string RootName = "NovaTowerDefenseGame";
        private static bool _busy;

        public static async void Build(Action<string> log, bool enterPlay = false)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return;
            if (_busy) { log?.Invoke("Kule savunma zaten kuruluyor."); return; }
            _busy = true;
            NovaAssetLibrary.WarnIfMissing(log); // kütüphane yoksa engellemez, sebebini söyler
            bool shaderSync = NovaEditorGuard.BeginSyncShaders();
            try
            {
                var old = GameObject.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old);

                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Nova: Kule Savunma");

                // Işık
                if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
                {
                    var sun = new GameObject("Sun");
                    sun.transform.SetParent(root.transform);
                    var l = sun.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                    l.intensity = 1.15f;
                }

                // Kamera
                if (Camera.main == null)
                {
                    var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                    camGo.transform.SetParent(root.transform);
                    camGo.AddComponent<Camera>();
                    camGo.AddComponent<AudioListener>();
                    foreach (var al in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                        if (al.gameObject != camGo) al.enabled = false;
                }

                // Oyun yöneticisi (yolu kendisi üretir)
                var mgr = new GameObject("TowerDefense");
                mgr.transform.SetParent(root.transform);
                var td = mgr.AddComponent<NovaTowerDefense>();
                td.pathMaterial = MakeMaterial("dirt|ground0|gravel|sand|road", new Color(0.45f, 0.38f, 0.28f));

                log?.Invoke("Düşman ve kule modelleri aranıyor...");
                td.enemyModel = await LoadOne(root.transform, new[] { "character" },
                    "zombie|skeleton|goblin|orc|robot|monster|enemy|slime|knight");
                td.towerModel = await LoadOne(root.transform, new[] { "tower" },
                    "tower|turret|watchtower|cannon|ballista");

                string extras = (td.enemyModel != null ? "düşman ✓ " : "") + (td.towerModel != null ? "kule ✓" : "");
                log?.Invoke($"Kule savunma hazır{(extras.Length > 0 ? " (" + extras.Trim() + ")" : "")} — "
                          + "Play'e bas: yol KENARINA tıklayarak kule kur, dalgaları durdur.");

                Selection.activeGameObject = root;
                if (enterPlay && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { log?.Invoke("Kule savunma kurulamadı: " + e.Message); Debug.LogException(e); }
            finally { _busy = false; NovaEditorGuard.EndSyncShaders(shaderSync); }
        }

        private static Material MakeMaterial(string pattern, Color fallback)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "NovaTD_path" };
            void SetCol(Color c) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); }
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
                        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                        SetCol(Color.white);
                        return m;
                    }
                }
            }
            catch { }
            SetCol(fallback);
            return m;
        }

        // Önce rol havuzu, bulunamazsa isim deseni
        private static async Task<GameObject> LoadOne(Transform parent, string[] roles, string namePattern)
        {
#if GLTFAST_INSTALLED
            try
            {
                AssetCatalog.Load(null, true);
                var pool = AssetCatalog.FilterRoles(roles, "any")
                    .Where(e => e != null && e.triangles >= 0 && e.triangles <= 60000).ToList();
                if (pool.Count == 0 && !string.IsNullOrEmpty(namePattern))
                {
                    var re = new System.Text.RegularExpressions.Regex(namePattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    pool = AssetCatalog.Load().Where(e => e != null && !string.IsNullOrEmpty(e.name)
                        && re.IsMatch(e.name) && e.triangles >= 0 && e.triangles <= 60000).ToList();
                }
                var rnd = new System.Random();
                foreach (var e in pool.OrderBy(_ => rnd.Next()).Take(6))
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
