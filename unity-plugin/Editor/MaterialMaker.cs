using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityAI.Lib;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityAI
{
    /// <summary>
    /// Malzeme/Texture üretimi (backend /v1/generate/image, fal FLUX). Prompt -> dikişsiz texture ->
    /// Assets'e kaydet -> Material oluştur -> hedef nesneye uygula. Ana pencereden çağrılır.
    /// </summary>
    public static class MaterialMaker
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private const string TexDir = "Assets/NovaTextures";

        public static void Generate(string baseUrl, string prompt, float tiling, GameObject[] targets, Action<string> status)
        {
            _ = Run(baseUrl, prompt, tiling, targets, status);
        }

        private static async Task Run(string baseUrl, string prompt, float tiling, GameObject[] targets, Action<string> status)
        {
            Report(status, NovaLocale.T("mat.generatingTexture"));
            string imageUrl = null;
            try
            {
                var body = "{\"prompt\":" + Quote("seamless tileable PBR texture, top-down, " + prompt) + "}";
                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/generate/image");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + UnityAIConfig.ApiToken);
                var resp = await Http.SendAsync(req);
                var txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Report(status, NovaLocale.T("mat.genError", txt)); return; }
                var obj = Json.Deserialize(txt) as Dictionary<string, object>;
                imageUrl = obj != null && obj.TryGetValue("imageUrl", out var u) ? u?.ToString() : null;
                if (string.IsNullOrEmpty(imageUrl)) { Report(status, NovaLocale.T("mat.noImageUrl")); return; }
            }
            catch (Exception e) { Report(status, NovaLocale.T("mat.requestError", e.Message)); return; }

            byte[] bytes;
            try { bytes = await Http.GetByteArrayAsync(imageUrl); }
            catch (Exception e) { Report(status, NovaLocale.T("mat.downloadError", e.Message)); return; }

            EditorApplication.delayCall += () => SaveAndApply(bytes, prompt, tiling, targets, status);
        }

        private static void Report(Action<string> status, string msg)
        {
            EditorApplication.delayCall += () => status?.Invoke(msg);
        }

        private static void SaveAndApply(byte[] bytes, string prompt, float tiling, GameObject[] targets, Action<string> status)
        {
            try
            {
                if (!AssetDatabase.IsValidFolder(TexDir)) AssetDatabase.CreateFolder("Assets", "NovaTextures");
                string safe = Sanitize(prompt);
                string texPath = AssetDatabase.GenerateUniqueAssetPath(TexDir + "/" + safe + ".png");
                System.IO.File.WriteAllBytes(texPath, bytes);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);

                var imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (imp != null) { imp.wrapMode = TextureWrapMode.Repeat; imp.SaveAndReimport(); }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

                bool urp = GraphicsSettings.currentRenderPipeline != null;
                var shader = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Standard");
                var mat = new Material(shader);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                mat.mainTextureScale = new Vector2(tiling, tiling);

                string matPath = AssetDatabase.GenerateUniqueAssetPath(TexDir + "/" + safe + ".mat");
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();

                var valid = new List<GameObject>();
                if (targets != null) foreach (var t in targets) if (t != null) valid.Add(t);
                if (valid.Count == 0)
                {
                    var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    plane.name = safe + "_surface";
                    Undo.RegisterCreatedObjectUndo(plane, "Nova: Malzeme düzlemi");
                    valid.Add(plane);
                }

                // Orijinal malzemeleri sakla (geri alma için) + Undo kaydı
                _lastEdit.Clear();
                int applied = 0;
                GameObject last = null;
                foreach (var go in valid)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        Undo.RecordObject(r, "Nova: malzeme uygula");
                        _lastEdit.Add(new Snap { R = r, Mats = r.sharedMaterials });
                        int slots = Mathf.Max(1, r.sharedMaterials.Length);
                        var arr = new Material[slots];
                        for (int i = 0; i < slots; i++) arr[i] = mat;
                        r.sharedMaterials = arr;
                        applied++;
                    }
                    last = go;
                }
                if (last != null) Selection.activeGameObject = last;
                EditorGUIUtility.PingObject(mat);
                status?.Invoke(NovaLocale.T("mat.applied", applied, matPath));
            }
            catch (Exception e) { status?.Invoke(NovaLocale.T("mat.saveApplyError", e.Message)); }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---- Geri alma (son uygulanan malzemeyi orijinaline döndür) ----
        private class Snap { public Renderer R; public Material[] Mats; }
        private static readonly List<Snap> _lastEdit = new List<Snap>();

        public static void Revert(Action<string> status)
        {
            if (_lastEdit.Count == 0) { status?.Invoke(NovaLocale.T("mat.nothingToRevert")); return; }
            int n = 0;
            foreach (var sn in _lastEdit)
            {
                if (sn.R == null) continue;
                Undo.RecordObject(sn.R, "Nova: malzeme geri al");
                sn.R.sharedMaterials = sn.Mats;
                n++;
            }
            _lastEdit.Clear();
            status?.Invoke(NovaLocale.T("mat.reverted", n));
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "tex") sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            var r = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(r)) return "texture";
            return r.Length > 32 ? r.Substring(0, 32) : r;
        }
    }
}
