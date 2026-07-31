using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Kalıcılık: üretilen GLB'yi runtime import etmek yerine projeye indirir,
    /// Assets/NovaModels altına kaydeder, Unity'nin glTFast importer'ı ile import eder
    /// ve KALICI bir prefab örneği olarak sahneye koyar. Böylece sahne kaydı/yeniden açılışında kaybolmaz.
    /// Ayrı, bağımsız dosya (ana pencereye dokunmadan).
    /// </summary>
    public static class AssetSaver
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private const string Dir = "Assets/NovaModels";

        public static void SaveGlbAndPlace(string glbUrl, string name, float height, Action<string> log)
        {
            _ = Run(glbUrl, name, height, log);
        }

        private static async Task Run(string glbUrl, string name, float height, Action<string> log)
        {
            byte[] bytes;
            try { bytes = await Http.GetByteArrayAsync(glbUrl); }
            catch (Exception e) { Report(log, NovaLocale.T("mat.downloadError", e.Message)); return; }
            EditorApplication.delayCall += () => SaveImportPlace(bytes, name, height, log);
        }

        private static void Report(Action<string> log, string msg)
        {
            EditorApplication.delayCall += () => log?.Invoke(msg);
        }

        private static void SaveImportPlace(byte[] bytes, string name, float height, Action<string> log)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "NovaModels");
                string safe = Sanitize(string.IsNullOrEmpty(name) ? "model" : name);
                string path = AssetDatabase.GenerateUniqueAssetPath(Dir + "/" + safe + ".glb");

                System.IO.File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    log?.Invoke(NovaLocale.T("persist.savedNotImported", path));
                    return;
                }

                var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (go == null) go = UnityEngine.Object.Instantiate(prefab);
                go.name = safe;

                // Boy: hedef yüksekliğe ölçekle
                if (height > 0f)
                {
                    var rends = go.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        if (b.size.y > 0.0001f) go.transform.localScale *= height / b.size.y;
                    }
                }

                // Collider ekle (yoksa) — oynanabilirlik için
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var mf = go.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        mf.gameObject.AddComponent<MeshCollider>();
                }

                Undo.RegisterCreatedObjectUndo(go, "Nova: Kalıcı model");
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                log?.Invoke(NovaLocale.T("persist.permanentAdded", path));
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("mat.saveApplyError", e.Message)); }
        }

        private static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            var r = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(r)) return "model";
            return r.Length > 40 ? r.Substring(0, 40) : r;
        }
    }
}
