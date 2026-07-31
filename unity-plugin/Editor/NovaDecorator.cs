using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityAI.Lib;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// E1 — AKILLI SAHNE DEKORATÖRÜ v2. Seçili noktanın (veya SceneView merkezinin)
    /// çevresine dekor döşer: zemine oturur, dik yamaca bitki koymaz, tek Undo ile geri alınır.
    /// v2 yenilikleri: DOĞAL DİL planı (backend /v1/world/decor → rol karışımı) +
    /// KÜRATÖR beyin asset seçimi (/v1/world/curate). Sohbetten DecorateArea aracıyla çağrılır.
    /// v3: alan seçimi + parça değiştirme.
    /// </summary>
    public static class NovaDecorator
    {
        public class Preset
        {
            public string Key;   // yerelleştirme anahtarı (decor.<Key>)
            public string Name;
            // role · adet · match (isim bununla eşleşmeli; null=hepsi) · ban (eşleşen elenir)
            public (string role, int count, string match, string ban)[] Mix;
        }

        // Sahne parçası/dev kayaları her preset'ten uzak tut
        private const string RockBan = "temple|bridge|mountain|iceberg|crystal|cove|swallow|cliff|walkway|gem";

        public static readonly Preset[] Presets =
        {
            new Preset { Key = "forest", Name = "Orman köşesi", Mix = new[]
            {
                ("tree", 10, (string)null, "palm|xmas|christmas"),   // vadide palmiye olmaz
                ("bush", 6, null, "hedge"),                          // budanmış çit bahçe işi
                ("rock", 4, null, RockBan),
            } },
            new Preset { Key = "camp", Name = "Kamp alanı", Mix = new[]
            {
                ("prop", 5, "barrel|crate|box", (string)null),       // kampta varil/sandık; hidrant/posta kutusu DEĞİL
                ("bench", 1, null, "working"),
                ("rock", 5, null, RockBan),
                ("tree", 3, null, "palm|xmas|christmas"),
            } },
            new Preset { Key = "garden", Name = "Köy bahçesi", Mix = new[]
            {
                ("fence", 6, (string)null, "barrier|guardrail|traffic|modular"),
                ("bush", 4, null, null),
                ("tree", 2, null, "palm|dead"),
                ("prop", 2, "mailbox|barrel|pot|crate", null),
            } },
            new Preset { Key = "rocky", Name = "Kayalık", Mix = new[]
            {
                ("rock", 12, (string)null, RockBan),
                ("bush", 2, null, "hedge"),
            } },
            new Preset { Key = "meadow", Name = "Çiçek çayırı", Mix = new[]
            {
                ("flower", 14, (string)null, "pot|mushroom|mushnub"), // çayırda mantar karakteri olmasın
                ("bush", 3, null, "hedge"),
            } },
        };

        private static bool _busy;
        private static readonly System.Net.Http.HttpClient Http =
            new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(60) };

        // v3: son dekor isteği (çeşitleme "yeni tohumla tekrar" için)
        private static string _lastPrompt;
        private static float _lastRadius = 15f;

        private static bool IsPlant(string role) => role == "tree" || role == "bush" || role == "flower";

        // ═══════════ v3: DÜZENLEME AKIŞLARI ═══════════

        /// <summary>
        /// Dekoru kaldırır. nearSelectionOnly=true ise seçili nesnenin/SceneView merkezinin
        /// yakınındaki NovaDecor kökünü, false ise sahnedeki TÜM dekoru siler. Undo'lu.
        /// </summary>
        public static void ClearDecor(bool nearSelectionOnly, Action<string> log)
        {
            var roots = new List<GameObject>();
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("NovaDecor_")) roots.Add(go);
            if (roots.Count == 0) { log?.Invoke("Sahnede kaldırılacak dekor yok."); return; }

            if (nearSelectionOnly)
            {
                Vector3? center = GetTargetCenter();
                if (center == null) { log?.Invoke("Bir dekora yakın dur veya seç, sonra tekrar dene."); return; }
                GameObject best = null; float bestD = float.MaxValue;
                foreach (var r in roots)
                {
                    float d = (r.transform.position - center.Value).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = r; }
                }
                if (best != null && bestD < 60f * 60f)
                {
                    Undo.DestroyObjectImmediate(best);
                    log?.Invoke($"En yakın dekor kaldırıldı: {best.name} (Ctrl+Z geri alır).");
                    return;
                }
                log?.Invoke("Yakında dekor bulunamadı.");
                return;
            }

            int n = roots.Count;
            foreach (var r in roots) Undo.DestroyObjectImmediate(r);
            log?.Invoke($"{n} dekor grubu kaldırıldı (Ctrl+Z geri alır).");
        }

        /// <summary>
        /// v3 çeşitleme: en son dekoru kaldırıp aynı temayı YENİ tohumla yeniden döşer.
        /// "beğenmedim, başka türlü dene" akışı.
        /// </summary>
        public static void ReDecorate(Action<string> log)
        {
            if (string.IsNullOrEmpty(_lastPrompt)) { log?.Invoke("Önce bir dekor kur, sonra çeşitle."); return; }
            // En son eklenen NovaDecor kökünü kaldır
            GameObject last = null;
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("NovaDecor_")) last = go; // son eşleşen ~ en yeni
            if (last != null) Undo.DestroyObjectImmediate(last);
            log?.Invoke($"🎲 Yeniden çeşitleniyor: {_lastPrompt}");
            ApplySmart(_lastPrompt, _lastRadius, log); // yeni tohum (her çağrıda rastgele)
        }

        /// <summary>
        /// v3 parça değiştir: seçili yerleştirilmiş asset'i (NovaPlaced) aynı rolden FARKLI
        /// bir katalog parçasıyla, aynı konum/ölçekte değiştirir. "bu ağacı başkasıyla değiştir".
        /// </summary>
        public static async void ReplaceSelected(Action<string> log)
        {
#if GLTFAST_INSTALLED
            if (NovaEditorGuard.BlockIfBusy(log)) return;
            try
            {
            var sel = Selection.activeGameObject;
            var mark = sel != null ? sel.GetComponentInParent<NovaWorld.NovaPlaced>() : null;
            if (mark == null) { log?.Invoke("Önce Nova ile yerleştirilmiş bir nesne seç (ağaç/kaya/prop...)."); return; }

            string role = mark.role;
            var pool = AssetCatalog.FilterRoles(new[] { role }, "any")
                .Where(e => e.triangles >= 0 && e.triangles <= 60000 && e.file != mark.assetFile).ToList();
            if (pool.Count == 0) { log?.Invoke($"'{role}' rolünde alternatif asset yok."); return; }

            var e = pool[new System.Random().Next(pool.Count)];
            var old = mark.gameObject;
            var parent = old.transform.parent;
            var pos = old.transform.position;
            var rot = old.transform.rotation;
            float target = mark.targetSize > 0f ? mark.targetSize : AssetCatalog.TargetOf(e, 2f);

            var go = await Import(e);
            if (go == null) { log?.Invoke($"Yeni parça yüklenemedi: {e.name}"); return; }
            go.transform.SetParent(parent);
            go.transform.rotation = rot;

            // Ölçek: hedef boya getir, sonra eski konuma otur (yatayda merkez, dikeyde taban)
            go.transform.position = pos;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (maxDim > 1e-4f) go.transform.localScale *= target / maxDim;

                // Ölçekten sonra bounds'u yeniden ölç ve eski konuma hizala
                b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
            }

            var newMark = go.AddComponent<NovaWorld.NovaPlaced>();
            newMark.role = role; newMark.targetSize = target; newMark.assetFile = e.file;

            Undo.RegisterCreatedObjectUndo(go, "Nova: Parça değiştir");
            Undo.DestroyObjectImmediate(old);
            Selection.activeGameObject = go;
            log?.Invoke($"Parça değiştirildi ({role}): {e.name} (Ctrl+Z geri alır).");
            }
            catch (Exception ex)
            {
                log?.Invoke("Parça değiştirilemedi: " + ex.Message);
                Debug.LogException(ex);
            }
#else
            log?.Invoke("glTFast kurulu değil.");
            await Task.CompletedTask;
#endif
        }

        /// <summary>Hedef merkez: seçili nesne (Terrain hariç) > SceneView pivotu > null.</summary>
        private static Vector3? GetTargetCenter()
        {
            var sel = Selection.activeTransform;
            bool selIsTerrain = sel != null && sel.GetComponentInChildren<Terrain>() != null;
            if (sel != null && !selIsTerrain) return sel.position;
            if (SceneView.lastActiveSceneView != null) return SceneView.lastActiveSceneView.pivot;
            return null;
        }

        /// <summary>Eski API: hazır preset ile döşe (geriye dönük uyum).</summary>
        public static async void Apply(int presetIndex, float radius, Action<string> log)
        {
            var preset = Presets[Mathf.Clamp(presetIndex, 0, Presets.Length - 1)];
            await ApplyMixAsync(preset, radius, log, preset.Name);
        }

        /// <summary>
        /// E1 v2: DOĞAL DİLDEN dekor. "kamp alanı", "çitli çiçek bahçesi" gibi bir tarif alır;
        /// backend beyni rol karışımına çevirir, küratör asset seçer, motor yerleştirir.
        /// Backend yoksa anahtar kelimeden yerel preset'e düşer — akış hiç kırılmaz.
        /// </summary>
        public static async void ApplySmart(string prompt, float radius, Action<string> log)
        {
            _lastPrompt = prompt; _lastRadius = radius; // v3 çeşitleme için hatırla
            Preset plan = null;
            try
            {
                log?.Invoke(NovaLocale.T("decor.planning", prompt));
                plan = await FetchPlan(prompt, log);
            }
            catch (Exception e) { Debug.LogWarning("[Nova Dekor] Plan alınamadı: " + e.Message); }
            if (plan == null || plan.Mix == null || plan.Mix.Length == 0)
            {
                plan = LocalGuess(prompt);
                log?.Invoke(NovaLocale.T("decor.noAiPlan", plan.Name));
            }
            await ApplyMixAsync(plan, radius, log, prompt);
        }

        /// <summary>Backend /v1/world/decor → Preset. Hata/uyumsuzlukta null.</summary>
        private static async Task<Preset> FetchPlan(string prompt, Action<string> log)
        {
            var roles = new List<object> { "tree", "bush", "rock", "flower", "fence", "lamp", "bench", "sign", "fountain", "prop" };
            var body = new Dictionary<string, object> { { "prompt", prompt }, { "roles", roles } };
            using var req = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post, UnityAIConfig.BaseUrl + "/v1/world/decor");
            req.Content = new System.Net.Http.StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + UnityAIConfig.ApiToken);
            using var resp = await Http.SendAsync(req);
            string txt = await resp.Content.ReadAsStringAsync();
            if (!(Json.Deserialize(txt) is Dictionary<string, object> r) ||
                !(r.TryGetValue("plan", out var pv) && pv is Dictionary<string, object> plan)) return null;

            string src = r.TryGetValue("source", out var sv) ? sv?.ToString() : "?";
            string name = plan.TryGetValue("name", out var nv) ? nv?.ToString() : prompt;
            var mixes = new List<(string role, int count, string match, string ban)>();
            if (plan.TryGetValue("mix", out var mv) && mv is List<object> list)
            {
                foreach (var it in list)
                {
                    if (!(it is Dictionary<string, object> m)) continue;
                    string role = m.TryGetValue("role", out var rv) ? rv?.ToString() : null;
                    if (string.IsNullOrEmpty(role)) continue;
                    int count = m.TryGetValue("count", out var cv) && cv is long l ? (int)l
                              : cv is double d ? (int)d : 3;
                    string match = m.TryGetValue("match", out var mtv) ? mtv?.ToString() : null;
                    string ban = m.TryGetValue("ban", out var bv) ? bv?.ToString() : null;
                    mixes.Add((role, Mathf.Clamp(count, 1, 15), string.IsNullOrEmpty(match) ? null : match,
                        string.IsNullOrEmpty(ban) ? null : ban));
                }
            }
            if (mixes.Count == 0) return null;
            string notes = plan.TryGetValue("notes", out var ntv) ? ntv?.ToString() : "";
            Debug.Log($"[Nova Dekor] Plan ({src}): {name} · " +
                      string.Join(", ", mixes.Select(m => $"{m.role}×{m.count}")) +
                      (string.IsNullOrEmpty(notes) ? "" : $" · {notes}"));
            return new Preset { Key = "smart", Name = name, Mix = mixes.ToArray() };
        }

        /// <summary>Backend'e hiç ulaşılamazsa: anahtar kelimeden hazır preset.</summary>
        private static Preset LocalGuess(string prompt)
        {
            string t = (prompt ?? "").ToLowerInvariant();
            bool Has(params string[] ws) { foreach (var w in ws) if (t.Contains(w)) return true; return false; }
            if (Has("kamp", "camp", "çadır")) return Presets[1];
            if (Has("bahçe", "garden", "çit")) return Presets[2];
            if (Has("kaya", "rock", "taş")) return Presets[3];
            if (Has("çiçek", "çayır", "meadow")) return Presets[4];
            return Presets[0];
        }

        private static async Task ApplyMixAsync(Preset preset, float radius, Action<string> log, string curatePrompt)
        {
#if GLTFAST_INSTALLED
            if (NovaEditorGuard.BlockIfBusy(log)) return; // derleme/import sırasında GPU çökmesini önle
            if (_busy) { log?.Invoke(NovaLocale.T("decor.alreadyRunning")); return; }
            _busy = true;
            bool _shaderSync = NovaEditorGuard.BeginSyncShaders(); // DX12 çökme önlemi
            GameObject root = null;
            try
            {
                radius = Mathf.Clamp(radius, 4f, 60f);

                // Hedef nokta: seçili obje > SceneView bakış merkezi.
                // Terrain seçiliyse SAYMA: (1) merkez olarak anlamsız, (2) Terrain seçimi
                // Unity'nin fırça araçlarını açıyor ve kullanıcı yanlışlıkla araziyi yontuyor.
                Vector3 center;
                var sel = Selection.activeTransform;
                bool selIsTerrain = sel != null && sel.GetComponentInChildren<Terrain>() != null;
                if (sel != null && !selIsTerrain) center = sel.position;
                else if (SceneView.lastActiveSceneView != null) center = SceneView.lastActiveSceneView.pivot;
                else { log?.Invoke(NovaLocale.T("decor.lookAtSceneView")); return; }

                // Dekoratör tamamen katalog modellerine dayanır — kütüphane yoksa yapacak iş yok.
                if (!NovaAssetLibrary.EnsureReady(log, prompt: true)) return;

                AssetCatalog.Load(null, true);
                int seed = new System.Random().Next();
                var rnd = new System.Random(seed);

                Debug.Log($"[Nova] REÇETE · Dekor: {preset.Name} · merkez {center} · yarıçap {radius:0}m · " +
                          string.Join(", ", preset.Mix.Select(m => $"{m.role}×{m.count}" + (m.match != null ? $"[{m.match}]" : ""))));

                root = new GameObject($"NovaDecor_{seed}");
                Undo.RegisterCreatedObjectUndo(root, "Nova: Dekor");
                root.transform.position = center;

                // ---- 1) HAVUZLAR: rol başına filtreli aday listesi ----
                var poolByRole = new Dictionary<string, List<AssetCatalog.Entry>>();
                foreach (var (role, count, match, ban) in preset.Mix)
                {
                    var basePool = AssetCatalog.FilterRoles(new[] { role }, "any")
                        .Where(e => e.triangles >= 0 && e.triangles <= 60000).ToList();
                    var pool = basePool;
                    // İSİM FİLTRESİ: "kampta yangın musluğu" tarzı uyumsuzluğu kaynağında kes
                    if (!string.IsNullOrEmpty(match))
                        pool = pool.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.name ?? "", match, System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                    // Beynin regex'i hiç asset bulamadıysa match'i bırak (yanlış regex ≠ boş dekor)
                    if (pool.Count == 0 && !string.IsNullOrEmpty(match)) pool = basePool;

                    // GÜVENLİK BAN'I: plan ne derse desin rol başına apaçık uyumsuzlar elenir.
                    // ("box" deseni mailbox'ı yakalar; kaya rolünde dağ/iceberg dev sürprizler yapar.)
                    string safety = role == "rock" ? RockBan
                                  : role == "prop" ? (match != null && match.Contains("mail")
                                        ? "hydrant|manhole|traffic"          // plan mailbox'ı açıkça istedi (ör. bahçe)
                                        : "mail|post.?box|hydrant|manhole|traffic")
                                  : role == "tree" ? "xmas|christmas" : null;
                    string effBan = string.IsNullOrEmpty(ban) ? safety
                                  : string.IsNullOrEmpty(safety) ? ban : ban + "|" + safety;
                    if (!string.IsNullOrEmpty(effBan))
                        pool = pool.Where(e => !System.Text.RegularExpressions.Regex.IsMatch(e.name ?? "", effBan, System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                    Debug.Log($"[Nova Dekor] Havuz {role}: {basePool.Count} aday → filtre sonrası {pool.Count}" +
                              (match != null ? $" · match[{match}]" : "") + (effBan != null ? $" · ban[{effBan}]" : ""));
                    if (pool.Count == 0) { log?.Invoke(NovaLocale.T("decor.noSuitableRole", role)); continue; }
                    poolByRole[role] = pool.OrderBy(_ => rnd.Next()).ToList();
                }
                if (poolByRole.Count == 0)
                {
                    log?.Invoke(NovaLocale.T("decor.noSuitableAssets"));
                    UnityEngine.Object.DestroyImmediate(root);
                    root = null;
                    return;
                }

                // ---- 2) KÜRATÖR BEYİN: rol başına 1-3 uyumlu çeşidi seçer (aynı aile tercihli) ----
                var curateInput = poolByRole.ToDictionary(
                    kv => kv.Key, kv => (kv.Value, Mathf.Min(3, kv.Value.Count)));
                var curated = await WorldBuilderAI.Curate($"dekor: {curatePrompt}", "any", curateInput, log);

                // ---- 3) YERLEŞTİRME ----
                int placed = 0;
                foreach (var (role, count, match, ban) in preset.Mix)
                {
                    if (!poolByRole.ContainsKey(role)) continue;
                    var picksList = curated.TryGetValue(role, out var cp) && cp.Count > 0
                        ? cp : poolByRole[role].Take(3).ToList();

                    var palette = new List<(GameObject go, AssetCatalog.Entry e)>();
                    foreach (var e in picksList.Take(3))
                    {
                        var t = await Import(e);
                        if (t != null)
                        {
                            t.SetActive(false); t.hideFlags = HideFlags.HideAndDontSave;
                            palette.Add((t, e));
                            Debug.Log($"[Nova] Palet (dekor/{role}): {e.file} · family={e.family}");
                        }
                    }
                    if (palette.Count == 0) continue;

                    int tries = count * 4, done = 0;
                    for (int i = 0; i < tries && done < count; i++)
                    {
                        // Halka dağılımı: merkeze yığılmasın
                        float ang = (float)(rnd.NextDouble() * Math.PI * 2);
                        float dist = radius * Mathf.Sqrt((float)rnd.NextDouble());
                        var p = center + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                        if (!Physics.Raycast(p + Vector3.up * 80f, Vector3.down, out var hit, 300f)) continue;
                        if (IsPlant(role) && Vector3.Angle(hit.normal, Vector3.up) > 35f) continue; // dik yamaca bitki yok
                        if (hit.collider.GetComponentInParent<NovaWorld.NovaPlaced>() != null) continue; // başka dekorun üstüne değil

                        var (tmpl, e) = palette[rnd.Next(palette.Count)];
                        var go = UnityEngine.Object.Instantiate(tmpl);
                        go.hideFlags = HideFlags.None;
                        go.SetActive(true);
                        go.transform.SetParent(root.transform);
                        go.transform.rotation = Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f);

                        var rends = go.GetComponentsInChildren<Renderer>();
                        if (rends.Length == 0) { UnityEngine.Object.DestroyImmediate(go); continue; }
                        var b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        float target = AssetCatalog.TargetOf(e, 2f) * (0.8f + (float)rnd.NextDouble() * 0.5f);
                        float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                        go.transform.localScale *= Mathf.Clamp(maxDim > 1e-4f ? target / maxDim : 1f, 1e-6f, 1e6f);

                        b = go.GetComponentsInChildren<Renderer>()[0].bounds;
                        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                        float sink = role == "rock" ? b.size.y * 0.15f : 0.02f;
                        go.transform.position += new Vector3(hit.point.x - b.center.x, hit.point.y - b.min.y - sink, hit.point.z - b.center.z);

                        var mark = go.AddComponent<NovaWorld.NovaPlaced>();
                        mark.role = role; mark.targetSize = target; mark.assetFile = e.file;
                        placed++; done++;
                    }

                    foreach (var (t, _) in palette) UnityEngine.Object.DestroyImmediate(t);
                }

                if (placed == 0)
                {
                    log?.Invoke(NovaLocale.T("decor.nothingPlaced"));
                    UnityEngine.Object.DestroyImmediate(root);
                    root = null;
                    return;
                }

                string lint = SceneLint.Audit(root, null);
                Selection.activeGameObject = root;
                log?.Invoke(NovaLocale.T("decor.ready", preset.Name, placed, lint));
                root = null;
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("decor.buildFailed", e.Message)); Debug.LogException(e); }
            finally
            {
                _busy = false;
                NovaEditorGuard.EndSyncShaders(_shaderSync);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
#else
            log?.Invoke(NovaLocale.T("world.status.gltfastMissingDecor"));
            await Task.CompletedTask;
#endif
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
                NovaMeshFix.Repair(go); // vertex renkli modeller beyaz kalmasın
                return go;
            }
            catch { return null; }
        }
#endif
    }
}
