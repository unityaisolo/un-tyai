using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// T6 — KALICILIK. Üretilen dünyayı projeye YAZAR: TerrainData, doku katmanları,
    /// ve glTFast ile bellekte oluşturulmuş mesh/materyalleri asset olarak kaydeder.
    /// Bunlar kaydedilmezse Unity kapanınca harita "boş beyaz zemin" olarak açılır.
    /// Çıktı: Assets/Nova/Worlds/&lt;dünya adı&gt;/
    /// </summary>
    public static class TerrainPersistence
    {
        private const string Root = "Assets/Nova/Worlds";

        public static void Save(Action<string> log)
        {
            var world = FindWorld();
            if (world == null) { log?.Invoke(NovaLocale.T("persist.noMap")); return; }

            try
            {
                string dir = EnsureFolder(world.name);
                log?.Invoke(NovaLocale.T("persist.savingTerrain"));

                // 1) TERRAIN DATA — heightmap + splat + katmanlar (2K dokular burada kalıcılaşır)
                var terrain = world.GetComponentInChildren<Terrain>();
                if (terrain != null && terrain.terrainData != null)
                {
                    var td = terrain.terrainData;

                    // 1a) Katman dokularını PNG olarak yaz (bellekteki Texture2D'ler kaybolur)
                    var layers = td.terrainLayers;
                    for (int i = 0; i < layers.Length; i++)
                    {
                        if (layers[i] == null) continue;
                        var layer = layers[i];
                        if (!AssetDatabase.Contains(layer))
                        {
                            layer.diffuseTexture = SaveTexture(layer.diffuseTexture, dir, $"layer{i}_color");
                            layer.normalMapTexture = SaveTexture(layer.normalMapTexture, dir, $"layer{i}_normal", isNormal: true);
                            AssetDatabase.CreateAsset(layer, $"{dir}/TerrainLayer_{i}.terrainlayer");
                        }
                    }
                    td.terrainLayers = layers;

                    if (!AssetDatabase.Contains(td))
                        AssetDatabase.CreateAsset(td, $"{dir}/TerrainData.asset");
                }

                // 2) MESH + MATERYAL — glTFast'ten gelen her şey bellekte; tek konteyner asset'e yaz
                log?.Invoke(NovaLocale.T("persist.savingModels"));
                string containerPath = $"{dir}/WorldAssets.asset";
                var container = ScriptableObject.CreateInstance<NovaWorldAssets>();
                AssetDatabase.CreateAsset(container, containerPath);

                var savedMeshes = new HashSet<Mesh>();
                var savedMats = new HashSet<Material>();
                int meshCount = 0, matCount = 0;

                foreach (var mf in world.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh;
                    if (m == null || AssetDatabase.Contains(m) || !savedMeshes.Add(m)) continue;
                    AssetDatabase.AddObjectToAsset(m, container);
                    meshCount++;
                }
                foreach (var r in world.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null || AssetDatabase.Contains(mat) || !savedMats.Add(mat)) continue;
                        // Materyalin dokularını da yaz (aksi halde pembe görünür)
                        SaveMaterialTextures(mat, dir);
                        AssetDatabase.AddObjectToAsset(mat, container);
                        matCount++;
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 3) SAHNE — referanslar ancak sahne kaydedilirse kalıcı olur
                EditorSceneManager.MarkSceneDirty(world.scene);
                bool sceneSaved = false;
                if (string.IsNullOrEmpty(world.scene.path))
                {
                    if (EditorUtility.DisplayDialog(NovaLocale.T("dialog.saveMap.title"),
                        NovaLocale.T("dialog.saveMap.body"),
                        NovaLocale.T("dialog.saveMap.save"), NovaLocale.T("dialog.saveMap.later")))
                        sceneSaved = EditorSceneManager.SaveOpenScenes();
                }
                else sceneSaved = EditorSceneManager.SaveOpenScenes();

                string msg = NovaLocale.T("persist.saved", dir, meshCount, matCount,
                    sceneSaved ? NovaLocale.T("persist.sceneSaved") : NovaLocale.T("persist.sceneNotSaved"));
                Debug.Log("[Nova Kalıcılık] " + msg);
                log?.Invoke(msg);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                log?.Invoke(NovaLocale.T("persist.saveFailed", e.Message));
            }
        }

        private static GameObject FindWorld()
        {
            // Seçili Nova kökü > sahnedeki ilk Nova kökü
            var sel = Selection.activeGameObject;
            if (sel != null && IsWorld(sel.transform.root.gameObject)) return sel.transform.root.gameObject;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (IsWorld(go)) return go;
            return null;
        }

        private static bool IsWorld(GameObject go) =>
            go.name.StartsWith("NovaTerra") || go.name.StartsWith("NovaCity") ||
            go.name.StartsWith("NovaTown") || go.name.StartsWith("NovaDecor");

        private static string EnsureFolder(string worldName)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Nova")) AssetDatabase.CreateFolder("Assets", "Nova");
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/Nova", "Worlds");
            string safe = string.Join("_", worldName.Split(Path.GetInvalidFileNameChars()));
            string dir = $"{Root}/{safe}";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root, safe);
            return dir;
        }

        /// <summary>Bellekteki Texture2D'yi PNG olarak projeye yazar ve import edilmiş halini döner.</summary>
        private static Texture2D SaveTexture(Texture texture, string dir, string name, bool isNormal = false)
        {
            var tex = texture as Texture2D;
            if (tex == null) return null;
            if (AssetDatabase.Contains(tex)) return tex; // zaten proje asset'i

            byte[] png;
            try { png = tex.EncodeToPNG(); }
            catch
            {
                // Okunamayan doku: RenderTexture üzerinden kopyala
                var rt = RenderTexture.GetTemporary(tex.width, tex.height);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                png = copy.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(copy);
            }
            if (png == null) return null;

            string path = $"{dir}/{name}.png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void SaveMaterialTextures(Material mat, string dir)
        {
            if (mat == null || mat.shader == null) return;
            string[] props = { "_BaseMap", "_MainTex", "_BumpMap", "_NormalMap" };
            foreach (var p in props)
            {
                if (!mat.HasProperty(p)) continue;
                var t = mat.GetTexture(p) as Texture2D;
                if (t == null || AssetDatabase.Contains(t)) continue;
                bool isNormal = p.Contains("Bump") || p.Contains("Normal");
                var saved = SaveTexture(t, dir, $"mat_{Sanitize(mat.name)}_{p.Trim('_')}", isNormal);
                if (saved != null) mat.SetTexture(p, saved);
            }
        }

        private static string Sanitize(string s) =>
            string.Join("_", (s ?? "mat").Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
    }

    /// <summary>Kaydedilen mesh/materyalleri barındıran konteyner (alt-asset kabı).</summary>
    public class NovaWorldAssets : ScriptableObject { }
}
