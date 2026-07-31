using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityAI.Lib;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// 3D üretim hattı. backend /v1/generate/3d çağrılır (fal anahtarı orada), dönen GLB
    /// glTFast ile import edilir.
    ///  - GeneratePreview: sahneye KOYMADAN önizleyiciye verir (3D Stüdyo akışı) + aşama bildirimi.
    ///  - GenerateAndPlace: doğrudan sahneye ekler (agent / Generate3DModel aracı).
    /// </summary>
    public static class ModelGenerator
    {
        // Doğrudan sahneye eklenince tetiklenir (ad, glbUrl). Agent yolu kullanır.
        public static event System.Action<string, string> ModelGenerated;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        // ---- Önizleme akışı (3D Stüdyo) ----
        // onReady(go, name, glbUrl, generator) ; onStep(index, label)  index<0 => hata
        public static void GeneratePreview(
            string baseUrl, string token, string prompt, string imageUrl, string name, int faceLimit,
            Action<GameObject, string, string, string> onReady,
            Action<int, string> onStep, Action<string> log)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) { onStep?.Invoke(-1, "editör meşgul"); return; }
            _ = RunPreviewAsync(baseUrl, token, prompt, imageUrl, name, faceLimit, onReady, onStep, log);
        }

        private static async Task RunPreviewAsync(
            string baseUrl, string token, string prompt, string imageUrl, string name, int faceLimit,
            Action<GameObject, string, string, string> onReady,
            Action<int, string> onStep, Action<string> log)
        {
            Step(onStep, 1, NovaLocale.T("step.modelBuilding"));
            var res = await RequestGlbAsync(baseUrl, token, prompt, imageUrl, faceLimit, log);
            if (string.IsNullOrEmpty(res.glbUrl)) { Step(onStep, -1, NovaLocale.T("gen.buildFailed")); return; }
            Step(onStep, 2, NovaLocale.T("step.texturePrep"));
            EditorApplication.delayCall += () => ImportForPreview(res.glbUrl, res.model, name, onReady, onStep, log);
        }

        // ---- Doğrudan sahneye ekleme (agent) ----
        public static void GenerateAndPlace(
            string baseUrl, string token, string prompt, string imageUrl,
            string name, Vector3 pos, Action<string> log)
        {
            _ = RunPlaceAsync(baseUrl, token, prompt, imageUrl, name, pos, log);
        }

        private static async Task RunPlaceAsync(
            string baseUrl, string token, string prompt, string imageUrl,
            string name, Vector3 pos, Action<string> log)
        {
            var res = await RequestGlbAsync(baseUrl, token, prompt, imageUrl, 0, log);
            if (string.IsNullOrEmpty(res.glbUrl)) return;
            EditorApplication.delayCall += () => ImportAndPlace(res.glbUrl, name, pos, log);
        }

        // ---- Ortak: backend'den GLB URL + model iste ----
        private static async Task<(string glbUrl, string model)> RequestGlbAsync(
            string baseUrl, string token, string prompt, string imageUrl, int faceLimit, Action<string> log)
        {
            try
            {
                var sb = new StringBuilder("{");
                bool first = true;
                if (!string.IsNullOrEmpty(prompt)) { sb.Append("\"prompt\":").Append(Quote(prompt)); first = false; }
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    if (!first) sb.Append(',');
                    sb.Append("\"imageUrl\":").Append(Quote(imageUrl));
                    first = false;
                }
                if (faceLimit > 0)
                {
                    if (!first) sb.Append(',');
                    sb.Append("\"faceLimit\":").Append(faceLimit);
                    first = false;
                }
                sb.Append('}');

                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/generate/3d");
                req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(token))
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

                var resp = await Http.SendAsync(req);
                var txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Report(log, NovaLocale.T("gen.error3d", txt)); return (null, null); }

                var obj = Json.Deserialize(txt) as Dictionary<string, object>;
                string glbUrl = obj != null && obj.TryGetValue("glbUrl", out var u) ? u?.ToString() : null;
                string model = obj != null && obj.TryGetValue("model", out var mo) ? mo?.ToString() : null;
                if (string.IsNullOrEmpty(glbUrl)) { Report(log, NovaLocale.T("gen.noGlbUrl")); return (null, null); }
                return (glbUrl, model);
            }
            catch (Exception e) { Report(log, NovaLocale.T("gen.requestError3d", e.Message)); return (null, null); }
        }

        private static void Report(Action<string> log, string msg)
        {
            EditorApplication.delayCall += () => log?.Invoke(msg);
        }

        private static void Step(Action<int, string> onStep, int i, string label)
        {
            if (onStep == null) return;
            EditorApplication.delayCall += () => onStep(i, label);
        }

        // ---- Karakter hattı: mevcut modeli rigle + animasyon (backend /v1/character/pipeline) ----
        // onReady(go, riggedUrl, walkUrl)
        public static void RigAndAnimate(
            string baseUrl, string token, string modelUrl, int[] actionIds, float heightMeters,
            Action<GameObject, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            _ = RunRigAsync(baseUrl, token, modelUrl, actionIds, heightMeters, onReady, onStep, log);
        }

        private static async Task RunRigAsync(
            string baseUrl, string token, string modelUrl, int[] actionIds, float heightMeters,
            Action<GameObject, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            Step(onStep, 1, NovaLocale.T("step.rigging"));
            var r = await RequestRigAsync(baseUrl, token, modelUrl, actionIds, heightMeters, log);
            if (string.IsNullOrEmpty(r.riggedUrl)) { Step(onStep, -1, NovaLocale.T("gen.rigFailed")); return; }
            EditorApplication.delayCall += () => ImportRigged(r.riggedUrl, r.animUrl, onReady, onStep, log);
        }

        private static async Task<(string riggedUrl, string animUrl)> RequestRigAsync(
            string baseUrl, string token, string modelUrl, int[] actionIds, float heightMeters, Action<string> log)
        {
            try
            {
                var sb = new StringBuilder("{\"modelUrl\":").Append(Quote(modelUrl));
                if (actionIds != null && actionIds.Length > 0)
                {
                    sb.Append(",\"animationActionIds\":[");
                    for (int i = 0; i < actionIds.Length; i++) { if (i > 0) sb.Append(','); sb.Append(actionIds[i]); }
                    sb.Append(']');
                }
                if (heightMeters > 0f)
                    sb.Append(",\"heightMeters\":").Append(heightMeters.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('}');

                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/character/pipeline");
                req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(token))
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

                var resp = await Http.SendAsync(req);
                var txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Report(log, NovaLocale.T("gen.rigError", txt)); return (null, null); }

                var obj = Json.Deserialize(txt) as Dictionary<string, object>;
                string rigged = obj != null && obj.TryGetValue("riggedUrl", out var r) ? r?.ToString() : null;
                int want = (actionIds != null && actionIds.Length > 0) ? actionIds[0] : -1;
                string chosen = null, walk = null, run = null, firstUrl = null;
                if (obj != null && obj.TryGetValue("animations", out var an) && an is List<object> list)
                {
                    foreach (var it in list)
                    {
                        if (it is Dictionary<string, object> d)
                        {
                            string nm = d.TryGetValue("name", out var n) ? n?.ToString() : "";
                            string url = d.TryGetValue("url", out var u) ? u?.ToString() : null;
                            if (string.IsNullOrEmpty(url)) continue;
                            if (firstUrl == null) firstUrl = url;
                            int aid = -1;
                            if (d.TryGetValue("actionId", out var a) && a != null) int.TryParse(a.ToString(), out aid);
                            if (want >= 0 && (aid == want || nm == "action_" + want)) chosen = url;
                            if (nm == "walk") walk = url;
                            else if (nm == "run") run = url;
                        }
                    }
                }
                if (string.IsNullOrEmpty(rigged)) { Report(log, NovaLocale.T("gen.noRiggedUrl")); return (null, null); }
                return (rigged, chosen ?? walk ?? run ?? firstUrl);
            }
            catch (Exception e) { Report(log, NovaLocale.T("gen.rigRequestError", e.Message)); return (null, null); }
        }

        // ---- Metinden görsel (görselden-3D için kaynak) ----
        public static void GenerateImage(
            string baseUrl, string token, string prompt, Action<string> onUrl, Action<string> log)
        {
            _ = RunImageAsync(baseUrl, token, prompt, onUrl, log);
        }

        private static async Task RunImageAsync(
            string baseUrl, string token, string prompt, Action<string> onUrl, Action<string> log)
        {
            try
            {
                var sb = new StringBuilder("{\"prompt\":").Append(Quote(prompt)).Append('}');
                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/generate/image");
                req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(token))
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

                var resp = await Http.SendAsync(req);
                var txt = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) { Report(log, NovaLocale.T("gen.imageError", txt)); return; }

                var obj = Json.Deserialize(txt) as Dictionary<string, object>;
                string url = obj != null && obj.TryGetValue("imageUrl", out var u) ? u?.ToString() : null;
                if (string.IsNullOrEmpty(url)) { Report(log, NovaLocale.T("gen.noImageUrl")); return; }
                EditorApplication.delayCall += () => onUrl?.Invoke(url);
            }
            catch (Exception e) { Report(log, NovaLocale.T("gen.imageRequestError", e.Message)); }
        }

#if GLTFAST_INSTALLED
        private static async void ImportForPreview(
            string glbUrl, string model, string name,
            Action<GameObject, string, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            try
            {
                Step(onStep, 3, NovaLocale.T("gen.importingToUnity"));
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var importSettings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                bool ok = await gltf.Load(glbUrl, importSettings);
                if (!ok) { Step(onStep, -1, NovaLocale.T("gen.glbLoadFailed")); log?.Invoke(NovaLocale.T("gen.glbLoadFailedUrl", glbUrl)); return; }

                Step(onStep, 4, NovaLocale.T("step.previewPrep"));
                var go = new GameObject(string.IsNullOrEmpty(name) ? "GeneratedModel" : name);
                go.hideFlags = HideFlags.HideAndDontSave;
                var settings = new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh };
                var instantiator = new GLTFast.GameObjectInstantiator(gltf, go.transform, null, settings);
                bool inst = await gltf.InstantiateMainSceneAsync(instantiator);
                if (!inst) { UnityEngine.Object.DestroyImmediate(go); Step(onStep, -1, NovaLocale.T("gen.instantiateFailed")); return; }

                log?.Invoke(NovaLocale.T("gen.previewReady"));
                onReady?.Invoke(go, name, glbUrl, model);
            }
            catch (Exception e) { Step(onStep, -1, NovaLocale.T("gen.importError")); log?.Invoke(NovaLocale.T("gen.importErrorMsg", e.Message)); }
        }

        private static async void ImportAndPlace(string glbUrl, string name, Vector3 pos, Action<string> log)
        {
            try
            {
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var importSettings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                bool ok = await gltf.Load(glbUrl, importSettings);
                if (!ok) { log?.Invoke(NovaLocale.T("gen.glbLoadFailedUrl", glbUrl)); return; }

                var go = new GameObject(string.IsNullOrEmpty(name) ? "GeneratedModel" : name);
                go.transform.position = pos;
                var settings = new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh };
                var instantiator = new GLTFast.GameObjectInstantiator(gltf, go.transform, null, settings);
                bool inst = await gltf.InstantiateMainSceneAsync(instantiator);
                Undo.RegisterCreatedObjectUndo(go, "UnityAI: Generate3DModel");
                Selection.activeGameObject = go;
                log?.Invoke(inst ? NovaLocale.T("gen.placedInScene", go.name) : NovaLocale.T("gen.instantiateFailed"));
                if (inst) ModelGenerated?.Invoke(go.name, glbUrl);
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("gen.importErrorMsg", e.Message)); }
        }

        // Riglenmiş modeli (statik) önizlemeye alır.
        private static async void ImportRigged(
            string riggedUrl, string walkUrl,
            Action<GameObject, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            try
            {
                Step(onStep, 2, NovaLocale.T("step.unityImport"));
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var importSettings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                bool ok = await gltf.Load(riggedUrl, importSettings);
                if (!ok) { Step(onStep, -1, NovaLocale.T("gen.riggedGlbLoadFailed")); return; }

                Step(onStep, 3, NovaLocale.T("step.previewPrep"));
                var go = new GameObject("RiggedCharacter");
                go.hideFlags = HideFlags.HideAndDontSave;
                var settings = new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh };
                var instantiator = new GLTFast.GameObjectInstantiator(gltf, go.transform, null, settings);
                bool inst = await gltf.InstantiateMainSceneAsync(instantiator);
                if (!inst) { UnityEngine.Object.DestroyImmediate(go); Step(onStep, -1, NovaLocale.T("gen.riggedInstantiateFailed")); return; }

                log?.Invoke(NovaLocale.T("gen.riggedReady"));
                onReady?.Invoke(go, riggedUrl, walkUrl);
            }
            catch (Exception e) { Step(onStep, -1, NovaLocale.T("gen.rigImportError")); log?.Invoke(NovaLocale.T("gen.rigImportErrorMsg", e.Message)); }
        }

        // Animasyonlu GLB'yi (yürüme) sahneye koyar; Play modunda oynar.
        public static void PlaceAnimatedFromUrl(string glbUrl, string name, Vector3 pos, float targetHeight, Action<string> log)
        {
            _ = PlaceAnimatedAsync(glbUrl, name, pos, targetHeight, log);
        }

        private static async Task PlaceAnimatedAsync(string glbUrl, string name, Vector3 pos, float targetHeight, Action<string> log)
        {
            try
            {
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var importSettings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.Legacy };
                bool ok = await gltf.Load(glbUrl, importSettings);
                if (!ok) { log?.Invoke(NovaLocale.T("gen.animGlbLoadFailed")); return; }

                var go = new GameObject(string.IsNullOrEmpty(name) ? "Character" : name);
                go.transform.position = pos;
                var instantiator = new GLTFast.GameObjectInstantiator(gltf, go.transform);
                bool inst = await gltf.InstantiateMainSceneAsync(instantiator);
                if (inst)
                {
                    var anim = go.GetComponentInChildren<Animation>();
                    if (anim != null)
                    {
                        anim.playAutomatically = true;
                        anim.wrapMode = WrapMode.Loop;
                        foreach (AnimationState st in anim) { anim.clip = st.clip; break; }
                    }
                }
                if (targetHeight > 0f)
                {
                    var rends = go.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        if (b.size.y > 0.0001f) go.transform.localScale *= targetHeight / b.size.y;
                    }
                }
                Undo.RegisterCreatedObjectUndo(go, "UnityAI: Karakter (animasyonlu)");
                Selection.activeGameObject = go;
                log?.Invoke(inst ? NovaLocale.T("gen.charPlaced") : NovaLocale.T("gen.charInstantiateFailed"));
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("gen.animImportError", e.Message)); }
        }
#else
        private static void ImportForPreview(
            string glbUrl, string model, string name,
            Action<GameObject, string, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            Step(onStep, -1, NovaLocale.T("gen.gltfastMissingStep"));
            log?.Invoke(NovaLocale.T("gen.gltfastMissingWithUrl", glbUrl));
        }

        private static void ImportAndPlace(string glbUrl, string name, Vector3 pos, Action<string> log)
        {
            log?.Invoke(NovaLocale.T("gen.gltfastMissingWithUrl", glbUrl));
        }

        private static void ImportRigged(
            string riggedUrl, string walkUrl,
            Action<GameObject, string, string> onReady, Action<int, string> onStep, Action<string> log)
        {
            Step(onStep, -1, NovaLocale.T("gen.gltfastMissingStep"));
            log?.Invoke(NovaLocale.T("gen.gltfastMissingRigged", riggedUrl));
        }

        public static void PlaceAnimatedFromUrl(string glbUrl, string name, Vector3 pos, float targetHeight, Action<string> log)
        {
            log?.Invoke(NovaLocale.T("gen.gltfastMissingAnim", glbUrl));
        }
#endif

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
