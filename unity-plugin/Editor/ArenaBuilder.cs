using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NovaWorld;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// FPS ARENA / DALGA SAVUNMASI kurucu. Sahnede Nova arazisi varsa ONU kullanır
    /// (yoksa basit bir zemin kurar), FPS oyuncusunu doğurur ve NovaArena dalga
    /// yöneticisini bağlar. Düşman modeli katalogdan (character rolü) gelir.
    /// </summary>
    public static class ArenaBuilder
    {
        public const string RootName = "NovaArenaGame";
        private static bool _busy;

        public static async void Build(Action<string> log, bool enterPlay = false)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return;
            if (_busy) { log?.Invoke("Arena zaten kuruluyor."); return; }
            _busy = true;
            NovaAssetLibrary.WarnIfMissing(log); // kütüphane yoksa engellemez, sebebini söyler
            bool shaderSync = NovaEditorGuard.BeginSyncShaders();
            try
            {
                var old = GameObject.Find(RootName);
                if (old != null) Undo.DestroyObjectImmediate(old);

                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Nova: Arena");

                // ---- Zemin: mevcut Nova arazisi varsa onu kullan ----
                bool hasTerrain = UnityEngine.Object.FindAnyObjectByType<Terrain>() != null;
                if (!hasTerrain)
                {
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    ground.name = "ArenaGround";
                    ground.transform.SetParent(root.transform);
                    ground.transform.localScale = new Vector3(12f, 1f, 12f); // 120 m
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.28f, 0.30f, 0.28f));
                    ground.GetComponent<Renderer>().sharedMaterial = mat;
                    log?.Invoke("Arazi bulunamadı — düz arena zemini kuruldu.");
                }
                else log?.Invoke("Mevcut arazi arena olarak kullanılıyor.");

                // ---- Işık ----
                if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
                {
                    var sun = new GameObject("Sun");
                    sun.transform.SetParent(root.transform);
                    var l = sun.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
                    l.intensity = 1.1f;
                }

                // ---- Oyuncu (FPS) ----
                WorldExplorer.FindSpawnPoint(out var spawn, out var look);
                var player = new GameObject(WorldExplorer.PlayerName);
                player.transform.SetParent(root.transform);
                player.transform.position = spawn;
                var dir = look - spawn; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(dir.normalized);

                var cc = player.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);

                var camGo = new GameObject("NovaCamera");
                camGo.transform.SetParent(player.transform, false);
                camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f; cam.depth = 100f;
                foreach (var al in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                    al.enabled = false;
                camGo.AddComponent<AudioListener>();
                player.AddComponent<NovaFirstPerson>();

                // ---- Dalga yöneticisi ----
                var arena = root.AddComponent<NovaArena>();
                log?.Invoke("Düşman modeli hazırlanıyor...");
                arena.enemyModel = await LoadEnemy(root.transform);

                log?.Invoke(arena.enemyModel != null
                    ? "Arena hazır (katalog düşmanı ✓). Play'e bas — WASD hareket, sol tık ateş, R yeniden başla."
                    : "Arena hazır (düşmanlar primitive). Play'e bas — WASD hareket, sol tık ateş.");

                Selection.activeGameObject = root;
                if (enterPlay && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { log?.Invoke("Arena kurulamadı: " + e.Message); Debug.LogException(e); }
            finally { _busy = false; NovaEditorGuard.EndSyncShaders(shaderSync); }
        }

        // Katalogdan bir düşman modeli (character rolü; yoksa isim deseni)
        private static async Task<GameObject> LoadEnemy(Transform parent)
        {
#if GLTFAST_INSTALLED
            try
            {
                AssetCatalog.Load(null, true);
                var pool = AssetCatalog.FilterRoles(new[] { "character" }, "any")
                    .Where(e => e != null && e.triangles >= 0 && e.triangles <= 60000).ToList();
                if (pool.Count == 0)
                {
                    var re = new System.Text.RegularExpressions.Regex(
                        "zombie|skeleton|robot|droid|monster|enemy|goblin|knight|alien|slime",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    pool = AssetCatalog.Load().Where(e => e != null && !string.IsNullOrEmpty(e.name)
                        && re.IsMatch(e.name) && e.triangles >= 0 && e.triangles <= 60000).ToList();
                }
                var rnd = new System.Random();
                foreach (var e in pool.OrderBy(_ => rnd.Next()).Take(6))
                {
                    var go = await Import(e);
                    if (go == null) continue;
                    go.name = "EnemyTemplate_" + e.name;
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
