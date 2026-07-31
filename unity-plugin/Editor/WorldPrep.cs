using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// T7 — OYUNA HAZIRLIK. "Gezilebilir harita"yı "oynanabilir dünya"ya çevirir tek tıkla:
    ///  1) NavMesh bake (arazi + statik nesneler navigation-static işaretlenir, legacy bake)
    ///  2) Oyuncu spawn noktası (NovaSpawn işareti — güvenli, suya/dik yamaca doğmaz)
    ///  3) Üstten minimap PNG'si (Assets/Nova/Minimaps altına)
    /// Hepsi Undo/temiz; NavMesh yoksa (paket eksik) diğer adımlar yine çalışır.
    /// </summary>
    public static class WorldPrep
    {
        public const string SpawnName = "NovaSpawn";

        public static void PrepareForPlay(bool navmesh, bool spawn, bool minimap, Action<string> log)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return;
            var root = FindWorldRoot();
            if (root == null) { log?.Invoke("Önce bir harita/arazi kur (NovaTerra/NovaCity bulunamadı)."); return; }

            int steps = 0;
            var summary = new System.Text.StringBuilder("Oyuna hazırlık:");

            // 1) SPAWN — önce, çünkü NavMesh ve minimap merkezini de kullanışlı kılar
            if (spawn)
            {
                var go = PlaceSpawn(root, log);
                summary.Append(go != null ? $" ✓ spawn ({go.transform.position.x:0},{go.transform.position.z:0})" : " ✗ spawn");
                if (go != null) steps++;
            }

            // 2) NAVMESH
            if (navmesh)
            {
                bool ok = BakeNavMesh(root, log);
                summary.Append(ok ? " ✓ NavMesh" : " ✗ NavMesh (AI Navigation paketi gerekebilir)");
                if (ok) steps++;
            }

            // 3) MINIMAP
            if (minimap)
            {
                string path = RenderMinimap(root, log);
                summary.Append(path != null ? " ✓ minimap" : " ✗ minimap");
                if (path != null) steps++;
            }

            log?.Invoke(steps > 0
                ? summary.ToString() + $" · {steps} adım tamam."
                : "Hiçbir adım uygulanmadı — seçenekleri kontrol et.");
        }

        // ---- Spawn ----
        private static GameObject PlaceSpawn(GameObject root, Action<string> log)
        {
            var old = GameObject.Find(SpawnName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            WorldExplorer.FindSpawnPoint(out var pos, out var look);
            var go = new GameObject(SpawnName);
            go.transform.position = pos;
            var dir = look - pos; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(dir.normalized);
            go.transform.SetParent(root.transform);
            // Sahnede görünür bir ikon ver (yalnız editörde)
            try { EditorGUIUtility.SetIconForObject(go, EditorGUIUtility.IconContent("d_Occlusion").image as Texture2D); } catch { }
            Undo.RegisterCreatedObjectUndo(go, "Nova: Spawn noktası");
            log?.Invoke($"Spawn noktası: {pos}");
            return go;
        }

        // ---- NavMesh (legacy editor bake; AI Navigation paketi varsa NavMeshSurface tercih edilir) ----
        private static bool BakeNavMesh(GameObject root, Action<string> log)
        {
            try
            {
                // Arazi + statik zemin/prop'ları navigation-static işaretle
                int flagged = 0;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = tr.gameObject;
                    bool isGround = go.GetComponent<Terrain>() != null || go.name == "Ground" || go.name == "Water";
                    bool isProp = go.GetComponent<MeshRenderer>() != null;
                    if (!isGround && !isProp) continue;
                    var flags = GameObjectUtility.GetStaticEditorFlags(go);
                    // NavigationStatic "obsolete" işaretli AMA aşağıda bilerek LEGACY
                    // NavMeshBuilder.BuildNavMesh() kullanıyoruz ve o hâlâ bu bayrağı okuyor.
                    // Yeni API (NavMeshSurface) ayrı bir pakete (com.unity.ai.navigation) bağlı;
                    // beta kullanıcısına zorunlu bağımlılık eklememek için legacy yolda kalıyoruz.
                    // Uyarıyı bastırıyoruz ki konsol beta'da temiz görünsün.
#pragma warning disable 618
                    GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.NavigationStatic);
#pragma warning restore 618
                    flagged++;
                }

                // Legacy editor bake: UnityEditor.AI.NavMeshBuilder.BuildNavMesh()
                var t = Type.GetType("UnityEditor.AI.NavMeshBuilder, UnityEditor");
                var build = t?.GetMethod("BuildNavMesh", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (build == null)
                {
                    log?.Invoke("NavMesh bake API'si bulunamadı (Window > AI > Navigation ile elle bake edebilirsin).");
                    return false;
                }
                build.Invoke(null, null);
                log?.Invoke($"NavMesh bake edildi ({flagged} nesne navigation-static).");
                return true;
            }
            catch (Exception e)
            {
                log?.Invoke("NavMesh bake hatası: " + e.Message);
                return false;
            }
        }

        // ---- Minimap: üstten ortografik render → PNG ----
        private static string RenderMinimap(GameObject root, Action<string> log)
        {
            try
            {
                Bounds b;
                var terr = root.GetComponentInChildren<Terrain>();
                if (terr != null) b = new Bounds(terr.transform.position + terr.terrainData.size * 0.5f, terr.terrainData.size);
                else
                {
                    var rs = root.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { log?.Invoke("Minimap: render edilecek nesne yok."); return null; }
                    b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                }

                int res = 1024;
                var camGo = new GameObject("NovaMinimapCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                cam.transform.position = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // tam tepeden aşağı bak
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = b.size.y + 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.12f, 0.15f);

                var rt = new RenderTexture(res, res, 24);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);

                string dir = "Assets/Nova/Minimaps";
                Directory.CreateDirectory(dir);
                string file = $"{dir}/minimap_{root.name}_{DateTime.Now:HHmmss}.png";
                File.WriteAllBytes(file, png);
                AssetDatabase.ImportAsset(file);
                // İçe aktarımı sprite/okunabilir yap (UI'da minimap olarak kullanılabilsin)
                if (AssetImporter.GetAtPath(file) is TextureImporter ti)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.SaveAndReimport();
                }
                log?.Invoke($"Minimap kaydedildi: {file}");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(file));
                return file;
            }
            catch (Exception e)
            {
                log?.Invoke("Minimap hatası: " + e.Message);
                return null;
            }
        }

        private static GameObject FindWorldRoot()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("NovaTerra") || go.name.StartsWith("NovaCity") || go.name.StartsWith("NovaTown"))
                    return go;
            return null;
        }
    }
}
