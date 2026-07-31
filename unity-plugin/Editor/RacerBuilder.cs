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
    /// YARIŞ / DRIFT kurucu. Prosedürel kapalı pist + arcade araç kontrolcüsü kurar.
    /// Araç modeli katalogdan (car/truck rolü), asfalt dokusu textures-raw'dan gelir.
    /// </summary>
    public static class RacerBuilder
    {
        public const string RootName = "NovaRacerGame";
        private static bool _busy;

        public static async void Build(Action<string> log, bool enterPlay = false)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return;
            if (_busy) { log?.Invoke("Pist zaten kuruluyor."); return; }
            _busy = true;
            NovaAssetLibrary.WarnIfMissing(log); // kütüphane yoksa engellemez, sebebini söyler
            bool shaderSync = NovaEditorGuard.BeginSyncShaders();
            try
            {
                var old = GameObject.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old);

                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Nova: Yarış");

                // Zemin (pist dışı çimen) — arazi yoksa
                if (UnityEngine.Object.FindAnyObjectByType<Terrain>() == null)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    ground.name = "RaceGround";
                    ground.transform.SetParent(root.transform);
                    ground.transform.localScale = new Vector3(40f, 1f, 40f); // 400 m
                    ground.transform.position = Vector3.down * 0.15f;
                    var gm = MakeMaterial("grass00|grass|ground0", new Color(0.26f, 0.36f, 0.24f), 0.4f);
                    ground.GetComponent<Renderer>().sharedMaterial = gm;
                }

                // Işık
                if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
                {
                    var sun = new GameObject("Sun");
                    sun.transform.SetParent(root.transform);
                    var l = sun.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.transform.rotation = Quaternion.Euler(52f, -20f, 0f);
                    l.intensity = 1.1f;
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

                // Araç + kontrolcü (pisti kendisi üretir)
                var car = new GameObject("RaceCar");
                car.transform.SetParent(root.transform);
                var racer = car.AddComponent<NovaRacer>();
                racer.trackMaterial = MakeMaterial("asphalt|road|pavement|concrete|gravel", new Color(0.21f, 0.22f, 0.25f), 1f);

                log?.Invoke("Araç modeli aranıyor...");
                racer.carModel = await LoadCar(root.transform);

                log?.Invoke(racer.carModel != null
                    ? "Yarış pisti hazır (katalog aracı ✓) — Play'e bas: WASD sür, Space drift, R piste dön."
                    : "Yarış pisti hazır (kutu araç) — Play'e bas: WASD sür, Space drift, R piste dön.");

                Selection.activeGameObject = root;
                if (enterPlay && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { log?.Invoke("Pist kurulamadı: " + e.Message); Debug.LogException(e); }
            finally { _busy = false; NovaEditorGuard.EndSyncShaders(shaderSync); }
        }

        private static Material MakeMaterial(string pattern, Color fallback, float smooth)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "NovaRace_" + pattern.Split('|')[0] };
            void SetCol(Color c) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth * 0.2f);
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

        // Katalogdan bir araç (car rolü; yoksa truck; yoksa isim deseni)
        private static async Task<GameObject> LoadCar(Transform parent)
        {
#if GLTFAST_INSTALLED
            try
            {
                AssetCatalog.Load(null, true);
                var pool = AssetCatalog.FilterRoles(new[] { "car" }, "any")
                    .Where(e => e != null && e.triangles >= 0 && e.triangles <= 80000).ToList();
                if (pool.Count == 0)
                    pool = AssetCatalog.FilterRoles(new[] { "truck" }, "any")
                        .Where(e => e != null && e.triangles >= 0 && e.triangles <= 80000).ToList();
                var rnd = new System.Random();
                foreach (var e in pool.OrderBy(_ => rnd.Next()).Take(8))
                {
                    var go = await Import(e);
                    if (go == null) continue;
                    go.name = "CarTemplate_" + e.name;
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
