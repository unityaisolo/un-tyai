using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityAI.Lib;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>Yerleşim planı — backend AI'dan ya da yerel heuristikten gelir.</summary>
    public class WorldPlan
    {
        public string style = "any";
        public string theme = "any"; // modern | rural | fantasy — harita tipinden gelir
        public List<string> themes = new List<string>();
        public int size = 10;
        public float density = 0.6f;
        public float greenery = 0.4f;
        public bool vehicles = true;
        public bool props = true;
        public string summary = "";
    }

    /// <summary>
    /// World Builder v2 (Faz 1) — GERÇEK-DÜNYA ÖLÇEĞİ + YÖNELİM + AİLE KİLİDİ.
    /// - Her asset rolünün realTarget'ına (metre) ölçeklenir; parsel-doldurma YOK.
    /// - Binalar yola dönük yerleşir (setback'li), araçlar yol eksenine hizalanır.
    /// - Şehir tek stile + mümkünse tek asset-ailesine kilitlenir.
    /// - Rol bazlı seçim: interior/fincan gibi kirlilik Faz 0 küratöründe elendi.
    /// </summary>
    public static class WorldBuilderAI
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(60) };
        private static bool _building; // çift tıklama koruması

        // Güvenlik sınırları (native çökmeyi önlemek için)
        private const int MaxGrid = 12;
        private const int MaxTriangles = 80000;

        private static readonly string[] BuildingRoles = { "house", "shop", "civic", "tower" };

        // Rolün birincil ekseni yükseklik mi? (değilse uzun yatay kenar hedeflenir)
        private static bool HeightAxis(string role) =>
            role == "house" || role == "shop" || role == "civic" || role == "tower" ||
            role == "tree" || role == "bush" || role == "flower" || role == "lamp" ||
            role == "sign" || role == "fountain" || role == "character";

        // Katalogda realTarget yoksa rol varsayılanı (metre)
        private static float RoleDefault(string role)
        {
            switch (role)
            {
                case "house": return 8f; case "shop": return 7f; case "civic": return 14f; case "tower": return 18f;
                case "tree": return 7f; case "bush": return 1.5f; case "flower": return 0.6f; case "rock": return 1.5f;
                case "lamp": return 4.5f; case "bench": return 1.5f; case "sign": return 2.5f;
                case "fence": return 1.2f; case "fountain": return 3f; case "car": return 4.5f; case "truck": return 7f;
                default: return 1.5f;
            }
        }

        // ---- Plan isteği (backend /v1/world/plan) ----
        public static async Task<WorldPlan> RequestPlanAsync(string prompt, Action<string> log)
        {
            List<string> styles, themes;
            try { AssetCatalog.Load(null, true); styles = AssetCatalog.Styles(); themes = AssetCatalog.Themes(); }
            catch (Exception e) { log?.Invoke(NovaLocale.T("city.catalogReadError", e.Message)); return Heuristic(prompt, new List<string>(), new List<string>()); }

            try
            {
                var body = new Dictionary<string, object>
                {
                    { "prompt", prompt }, { "model", "nova-flash" }, // beyin: Groq
                    { "styles", styles.Cast<object>().ToList() },
                    { "themes", themes.Cast<object>().ToList() },
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, UnityAIConfig.BaseUrl + "/v1/world/plan");
                req.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req);
                string txt = await resp.Content.ReadAsStringAsync();
                if (Json.Deserialize(txt) is Dictionary<string, object> root && root.TryGetValue("plan", out var pv) && pv is Dictionary<string, object> pd)
                {
                    log?.Invoke(NovaLocale.T("city.planReceived", root.TryGetValue("source", out var s) ? s : "?"));
                    return FromDict(pd);
                }
                log?.Invoke(NovaLocale.T("city.planUnparseable"));
                return Heuristic(prompt, styles, themes);
            }
            catch (Exception e)
            {
                log?.Invoke(NovaLocale.T("city.serverUnreachable", e.Message));
                return Heuristic(prompt, styles, themes);
            }
        }

        private static WorldPlan FromDict(Dictionary<string, object> d)
        {
            var p = new WorldPlan();
            if (d.TryGetValue("style", out var st) && st != null) p.style = st.ToString();
            if (d.TryGetValue("themes", out var th) && th is IList tl)
                p.themes = tl.Cast<object>().Where(x => x != null).Select(x => x.ToString()).ToList();
            if (d.TryGetValue("size", out var sz)) p.size = Mathf.Clamp((int)ToF(sz, 10), 4, MaxGrid);
            if (d.TryGetValue("density", out var de)) p.density = Mathf.Clamp01(ToF(de, 0.6f));
            if (d.TryGetValue("greenery", out var gr)) p.greenery = Mathf.Clamp01(ToF(gr, 0.4f));
            if (d.TryGetValue("vehicles", out var ve)) p.vehicles = ToB(ve, true);
            if (d.TryGetValue("props", out var pr)) p.props = ToB(pr, true);
            if (d.TryGetValue("summary", out var su) && su != null) p.summary = su.ToString();
            return p;
        }

        private static float ToF(object o, float dflt)
        {
            if (o == null) return dflt;
            return float.TryParse(o.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : dflt;
        }
        private static bool ToB(object o, bool dflt) => o is bool b ? b : (o != null && bool.TryParse(o.ToString(), out var r) ? r : dflt);

        private static WorldPlan Heuristic(string prompt, List<string> styles, List<string> themes)
        {
            string t = (prompt ?? "").ToLowerInvariant();
            bool Has(params string[] ws) => ws.Any(w => t.Contains(w));
            var p = new WorldPlan();
            p.style = styles.Contains("realistic") && Has("gerçekçi", "realistic") ? "realistic"
                    : styles.Contains("low-poly") && Has("low-poly", "low poly", "basit", "stilize") ? "low-poly"
                    : "any";
            p.density = Has("yoğun", "kalabalık", "dense", "büyük") ? 0.8f : Has("seyrek", "az", "sparse") ? 0.35f : 0.6f;
            p.greenery = Has("orman", "ağaç", "yeşil", "park", "doğa", "forest") ? 0.7f : 0.35f;
            p.size = Has("büyük", "geniş", "large", "big") ? MaxGrid : Has("küçük", "small") ? 7 : 10;
            p.summary = prompt;
            return p;
        }

        // ---- Yerleştirme ----
        public static async void Build(WorldPlan plan, int seed, Action<string> log)
        {
#if GLTFAST_INSTALLED
            if (NovaEditorGuard.BlockIfBusy(log)) return; // derleme/import sırasında GPU çökmesini önle
            if (_building) { log?.Invoke(NovaLocale.T("world.status.alreadyBuildingCity")); return; }
            _building = true;
            bool _shaderSync = NovaEditorGuard.BeginSyncShaders(); // DX12 çökme önlemi
            GameObject root = null;
            try
            {
                // Şehir tamamen katalog binalarından kurulur — kütüphane şart.
                if (!NovaAssetLibrary.EnsureReady(log, prompt: true)) return;
                AssetCatalog.Load(null, true);

                // 1) STİL KİLİDİ — şehir tek stil
                var bAll = Cap(AssetCatalog.FilterRoles(BuildingRoles, plan.style));
                if (bAll.Count == 0) bAll = Cap(AssetCatalog.FilterRoles(BuildingRoles, "any"));
                if (bAll.Count == 0) { log?.Invoke(NovaLocale.T("city.noSuitableBuildings")); return; }
                string style = plan.style != "any" && bAll.Any(e => e.style == plan.style)
                    ? plan.style
                    : Dominant(bAll, e => e.style);
                bAll = bAll.Where(e => e.style == style).ToList();

                // 2) AİLE KİLİDİ — baskın aile yeterince zenginse ona kilitlen
                string fam = Dominant(bAll, e => e.family ?? "");
                var famList = bAll.Where(e => (e.family ?? "") == fam).ToList();
                bool famLock = famList.Count >= 4; // az çeşit > karışık stil (tutarlılık önce)
                var buildings = famLock ? famList : bAll;
                log?.Invoke(NovaLocale.T("city.styleFamilySummary", style,
                    famLock ? fam : NovaLocale.T("city.mixedFamilyCount", bAll.Select(e => e.family).Distinct().Count()),
                    buildings.Count));

                var housesL = buildings.Where(e => e.role == "house").ToList();
                var shopsL = buildings.Where(e => e.role == "shop").ToList();
                var towersL = buildings.Where(e => e.role == "tower").ToList();
                var civicsL = buildings.Where(e => e.role == "civic").ToList();
                if (housesL.Count == 0) housesL = buildings;

                var treesL = Pool("tree", style); var bushesL = Pool("bush", style);
                var vehSrc = plan.vehicles ? Cap(AssetCatalog.FilterRoles(new[] { "car", "truck" }, style)) : new List<AssetCatalog.Entry>();
                var propSrc = plan.props ? Cap(AssetCatalog.FilterRoles(new[] { "prop", "bench" }, style)) : new List<AssetCatalog.Entry>();

                var rnd = new System.Random(seed);
                int grid = Mathf.Clamp(plan.size, 4, MaxGrid);
                float cell = 14f, road = 4f, plot = cell - road; // ev 6-10 m + bahçe payı → 14 m hücre
                float total = grid * cell;

                root = new GameObject($"NovaCity_{seed}");
                Undo.RegisterCreatedObjectUndo(root, "Nova: AI Şehir");

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.SetParent(root.transform);
                ground.transform.position = new Vector3(total / 2f - cell / 2f, 0f, total / 2f - cell / 2f);
                ground.transform.localScale = new Vector3(total / 10f + 1f, 1f, total / 10f + 1f);
                SetMat(ground, new Color(0.28f, 0.30f, 0.26f));

                var roadMat = SolidMat(new Color(0.12f, 0.12f, 0.13f));
                for (int i = 0; i <= grid; i++)
                {
                    MakeStrip(root.transform, roadMat, new Vector3(total / 2f - cell / 2f, 0.02f, i * cell - cell / 2f), new Vector3(total + road, 0.04f, road));
                    MakeStrip(root.transform, roadMat, new Vector3(i * cell - cell / 2f, 0.02f, total / 2f - cell / 2f), new Vector3(road, 0.04f, total + road));
                }

                // 3) PALETLER — sınırlı sayıda benzersiz import, kalanı klon
                log?.Invoke(NovaLocale.T("city.preparingPalette"));
                var housePal = await BuildPalette(housesL, 7, rnd, log);
                var shopPal = await BuildPalette(shopsL, 3, rnd, log);
                var towerPal = await BuildPalette(towersL, 2, rnd, log);
                var civicPal = await BuildPalette(civicsL, 1, rnd, log);
                var treePal = await BuildPalette(treesL, 6, rnd, log);
                var bushPal = await BuildPalette(bushesL, 3, rnd, log);
                var vehPal = await BuildPalette(vehSrc, 4, rnd, log);
                var propPal = await BuildPalette(propSrc, 5, rnd, log);
                if (housePal.Count == 0) { log?.Invoke(NovaLocale.T("city.buildingImportFailed")); return; }

                float cGrid = (grid - 1) / 2f;
                var mapCenter = new Vector3(cGrid * cell, 0f, cGrid * cell);
                int placed = 0, treesN = 0;
                bool civicPlaced = false;

                for (int gx = 0; gx < grid; gx++)
                for (int gz = 0; gz < grid; gz++)
                {
                    var center = new Vector3(gx * cell, 0f, gz * cell);
                    float cd = Mathf.Max(Mathf.Abs(gx - cGrid), Mathf.Abs(gz - cGrid)) / Mathf.Max(cGrid, 1f);
                    bool core = cd < 0.3f;
                    double buildProb = core ? 1.0 : plan.density * (1.0 - cd * 0.45);

                    if (rnd.NextDouble() < buildProb)
                    {
                        // ZONLAMA (hafif, Faz 2'de organikleşecek): merkez ticari/yüksek, halka konut
                        List<Tmpl> pool;
                        if (!civicPlaced && civicPal.Count > 0 && cd < 0.45f && rnd.NextDouble() < 0.35)
                        { pool = civicPal; civicPlaced = true; }
                        else if (core && towerPal.Count > 0 && rnd.NextDouble() < 0.55) pool = towerPal;
                        else if (core && shopPal.Count > 0) pool = shopPal;
                        else if (cd < 0.65f && shopPal.Count > 0 && rnd.NextDouble() < 0.2) pool = shopPal;
                        else pool = housePal;

                        var t = pool[rnd.Next(pool.Count)];
                        var go = Clone(t.Go);
                        // YÖNELİM: bina, haritanın merkezine bakan yol tarafına döner (sokak hissi);
                        // %25 rastgele sapma tekdüzeliği kırar.
                        int front = FrontToward(mapCenter - center, rnd);
                        PlaceBuilding(go, root.transform, center, front, t.E, plot, rnd, log);
                        Mark(go, "building", 22f, t.E);
                        placed++;

                        if (propPal.Count > 0 && rnd.NextDouble() < 0.3)
                        {
                            var pr = propPal[rnd.Next(propPal.Count)];
                            var pgo = Clone(pr.Go);
                            var off = new Vector3(plot * 0.32f * Sign(rnd), 0f, plot * 0.32f * Sign(rnd));
                            PlaceScaled(pgo, root.transform, center + off, Quaternion.Euler(0f, rnd.Next(4) * 90f, 0f),
                                Target(pr.E) * Var(rnd, 0.15f), HeightAxis(pr.E.role), plot * 0.25f, false);
                            Mark(pgo, "prop", Target(pr.E), pr.E);
                        }
                    }
                    else if (treePal.Count > 0 && rnd.NextDouble() < plan.greenery + cd * 0.2f)
                    {
                        int n = 1 + (rnd.NextDouble() < 0.5 ? 1 : 0);
                        for (int k = 0; k < n; k++)
                        {
                            var t = treePal[rnd.Next(treePal.Count)];
                            var go = Clone(t.Go);
                            var off = new Vector3((float)(rnd.NextDouble() - 0.5) * plot * 0.8f, 0f, (float)(rnd.NextDouble() - 0.5) * plot * 0.8f);
                            PlaceScaled(go, root.transform, center + off, Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f),
                                Target(t.E) * Var(rnd, 0.3f), true, 0f, false);
                            Mark(go, "tree", Target(t.E), t.E);
                            treesN++;
                        }
                        if (bushPal.Count > 0 && rnd.NextDouble() < 0.5)
                        {
                            var b = bushPal[rnd.Next(bushPal.Count)];
                            var go = Clone(b.Go);
                            PlaceScaled(go, root.transform, center + new Vector3(plot * 0.3f * Sign(rnd), 0f, plot * 0.3f * Sign(rnd)),
                                Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f), Target(b.E) * Var(rnd, 0.3f), true, 0f, false);
                            Mark(go, "bush", Target(b.E), b.E);
                        }
                    }
                }

                // 4) ARAÇLAR — yol eksenine hizalı, şerit ofsetli
                if (vehPal.Count > 0)
                    for (int k = 0; k < grid + 2; k++)
                    {
                        var t = vehPal[rnd.Next(vehPal.Count)];
                        var go = Clone(t.Go);
                        bool alongX = rnd.NextDouble() < 0.5; // X ekseni boyunca uzanan yol mu
                        int line = rnd.Next(grid + 1);
                        float along = (float)(rnd.NextDouble() * total) - cell / 2f;
                        float lane = (road * 0.22f) * Sign(rnd);
                        Vector3 pos = alongX
                            ? new Vector3(along, 0f, line * cell - cell / 2f + lane)
                            : new Vector3(line * cell - cell / 2f + lane, 0f, along);
                        PlaceVehicle(go, root.transform, pos, alongX, Target(t.E) * Var(rnd, 0.1f), rnd);
                        Mark(go, "vehicle", Target(t.E), t.E);
                    }

                DestroyPalettes(housePal, shopPal, towerPal, civicPal, treePal, bushPal, vehPal, propPal);

                // DENETÇİ: dev/uçan/gömük objeleri otomatik düzelt (rapor Console'da)
                string lint = SceneLint.Audit(root, null);

                // PLAY'E HAZIR: şehirde de oyuncu kur (arazi ile tutarlı davranış)
                WorldExplorer.EnsurePlayer(NovaLocale.T("city.label"), null);

                Selection.activeGameObject = root;
                log?.Invoke(NovaLocale.T("world.status.mapReadyExplore", NovaLocale.T("city.ready", placed, treesN, lint)));
                root = null; // başarı — cleanup'ta silme
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("world.status.cityBuildFailed", e.Message)); }
            finally
            {
                _building = false;
                NovaEditorGuard.EndSyncShaders(_shaderSync);
                if (root != null) UnityEngine.Object.DestroyImmediate(root); // yarım kalan şehri bırakma
            }
#else
            log?.Invoke(NovaLocale.T("world.status.gltfastMissing"));
            await Task.CompletedTask;
#endif
        }

        // ---- FAZ 2: ORGANİK ŞEHİR (ızgara yok — kıvrımlı yollar + parseller + zonlama) ----
        public static async void BuildOrganic(WorldPlan plan, float sizeM, int seed, Action<string> log)
        {
#if GLTFAST_INSTALLED
            if (NovaEditorGuard.BlockIfBusy(log)) return; // derleme/import sırasında GPU çökmesini önle
            if (_building) { log?.Invoke(NovaLocale.T("world.status.alreadyBuildingCity")); return; }
            _building = true;
            bool _shaderSync = NovaEditorGuard.BeginSyncShaders(); // DX12 çökme önlemi
            GameObject root = null;
            try
            {
                // Şehir tamamen katalog binalarından kurulur — kütüphane şart.
                if (!NovaAssetLibrary.EnsureReady(log, prompt: true)) return;
                AssetCatalog.Load(null, true);

                // Stil + aile kilidi (Faz 1 ile aynı mantık)
                var bAll = Cap(AssetCatalog.FilterRoles(BuildingRoles, plan.style));
                if (bAll.Count == 0) bAll = Cap(AssetCatalog.FilterRoles(BuildingRoles, "any"));
                if (bAll.Count == 0) { log?.Invoke(NovaLocale.T("city.noSuitableBuildings")); return; }
                string style = plan.style != "any" && bAll.Any(e => e.style == plan.style)
                    ? plan.style : Dominant(bAll, e => e.style);
                bAll = bAll.Where(e => e.style == style).ToList();

                // TEMA FİLTRESİ: modern şehre kulübe/ağaç-ev, köye gökdelen sızmasın
                if (plan.theme != "any")
                {
                    var strict = bAll.Where(e => e.theme == plan.theme).ToList();
                    var loose = bAll.Where(e => e.theme == plan.theme || e.theme == "generic" || string.IsNullOrEmpty(e.theme)).ToList();
                    if (strict.Count >= 6) bAll = strict;
                    else if (loose.Count >= 6) bAll = loose;
                }

                string fam = Dominant(bAll, e => e.family ?? "");
                var famList = bAll.Where(e => (e.family ?? "") == fam).ToList();
                bool famLock = famList.Count >= 4;
                var buildings = famLock ? famList : bAll;
                log?.Invoke(NovaLocale.T("city.organicPlanning", style, plan.theme, famLock ? fam : NovaLocale.T("city.mixed")));

                var housesL = buildings.Where(e => e.role == "house").ToList();
                var shopsL = buildings.Where(e => e.role == "shop").ToList();
                var towersL = buildings.Where(e => e.role == "tower").ToList();
                var civicsL = buildings.Where(e => e.role == "civic").ToList();
                if (housesL.Count == 0) housesL = buildings;

                var rnd = new System.Random(seed);
                var map = CityLayout.Generate(sizeM, plan.density, plan.greenery, seed);

                root = new GameObject($"NovaCity_{seed}");
                Undo.RegisterCreatedObjectUndo(root, "Nova: Organik Şehir");

                // Zemin (çimen tonu — organik şehirde asfalt ızgara hissi yok)
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.SetParent(root.transform);
                ground.transform.position = new Vector3(sizeM / 2f, 0f, sizeM / 2f);
                ground.transform.localScale = new Vector3(sizeM / 10f + 3f, 1f, sizeM / 10f + 3f);
                SetMat(ground, new Color(0.32f, 0.38f, 0.27f));

                // YOLLAR: polyline'lar boyunca kısa şeritler (kıvrımlar pürüzsüz görünür)
                var roadsGo = new GameObject("Roads");
                roadsGo.transform.SetParent(root.transform);
                var asphalt = SolidMat(new Color(0.13f, 0.13f, 0.14f));
                for (int r = 0; r < map.Roads.Count; r++)
                {
                    float w = map.RoadWidths[r];
                    foreach (var (p, dir) in CityLayout.ArcWalk(map.Roads[r], 3.8f))
                    {
                        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        s.name = "Road";
                        s.transform.SetParent(roadsGo.transform);
                        s.transform.position = new Vector3(p.x, 0.03f, p.y);
                        s.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                        s.transform.localScale = new Vector3(w, 0.06f, 4.6f);
                        s.GetComponent<Renderer>().sharedMaterial = asphalt;
                    }
                }

                // KÜRATÖR BEYİN: paletleri rastgele değil, LLM seçer (tema/aile/ölçek uyumu).
                // Her build'de farklı ama TUTARLI bir set — "1 saniyede ezber şehir" dönemi bitti.
                log?.Invoke(NovaLocale.T("city.roadNetwork", map.Roads.Count, map.Lots.Count));
                var roleReq = new Dictionary<string, (List<AssetCatalog.Entry> pool, int count)>
                {
                    { "house", (housesL, 10) }, { "shop", (shopsL, 4) },
                    { "tower", (towersL, 3) }, { "civic", (civicsL, 1) },
                    { "tree", (Pool("tree", style), 6) }, { "bush", (Pool("bush", style), 3) },
                };
                if (plan.vehicles) roleReq["vehicle"] = (Cap(AssetCatalog.FilterRoles(new[] { "car", "truck" }, style)), 4);
                if (plan.props) { roleReq["lamp"] = (Pool("lamp", style), 2); roleReq["bench"] = (Pool("bench", style), 2); }
                var picks = await Curate(plan.summary, plan.theme, roleReq, log);

                log?.Invoke(NovaLocale.T("city.importingSelectedSet"));
                var housePal = await BuildPalette(picks["house"], picks["house"].Count, rnd, log);
                var shopPal = await BuildPalette(picks["shop"], picks["shop"].Count, rnd, log);
                var towerPal = await BuildPalette(picks["tower"], picks["tower"].Count, rnd, log);
                var civicPal = await BuildPalette(picks["civic"], picks["civic"].Count, rnd, log);
                var treePal = await BuildPalette(picks["tree"], picks["tree"].Count, rnd, log);
                var bushPal = await BuildPalette(picks["bush"], picks["bush"].Count, rnd, log);
                var vehPal = plan.vehicles ? await BuildPalette(picks["vehicle"], picks["vehicle"].Count, rnd, log) : new List<Tmpl>();
                var lampPal = plan.props ? await BuildPalette(picks["lamp"], picks["lamp"].Count, rnd, log) : new List<Tmpl>();
                var benchPal = plan.props ? await BuildPalette(picks["bench"], picks["bench"].Count, rnd, log) : new List<Tmpl>();
                if (housePal.Count == 0) { log?.Invoke(NovaLocale.T("city.buildingImportFailed")); return; }

                // LOTLAR: zona uygun bina, yola dönük
                int placed = 0, parks = 0;
                bool civicDone = false;
                foreach (var lot in map.Lots)
                {
                    var pos = new Vector3(lot.Center.x, 0f, lot.Center.y);
                    var f3 = new Vector3(lot.Facing.x, 0f, lot.Facing.y).normalized;

                    if (lot.Zone == "park")
                    {
                        parks++;
                        int nT = 1 + rnd.Next(3);
                        for (int k = 0; k < nT && treePal.Count > 0; k++)
                        {
                            var t = treePal[rnd.Next(treePal.Count)];
                            var go = Clone(t.Go);
                            var off = new Vector3((float)(rnd.NextDouble() - 0.5) * lot.Width * 0.7f, 0f, (float)(rnd.NextDouble() - 0.5) * lot.Depth * 0.7f);
                            PlaceScaled(go, root.transform, pos + off, Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f),
                                Target(t.E) * Var(rnd, 0.3f), true, 0f, false);
                            Mark(go, "tree", Target(t.E), t.E);
                        }
                        if (benchPal.Count > 0 && rnd.NextDouble() < 0.4)
                        {
                            var bch = benchPal[rnd.Next(benchPal.Count)];
                            var go = Clone(bch.Go);
                            PlaceScaled(go, root.transform, pos + f3 * (lot.Depth * 0.35f), Quaternion.LookRotation(f3),
                                Target(bch.E), false, 0f, false);
                            Mark(go, "bench", Target(bch.E), bch.E);
                        }
                        continue;
                    }

                    List<Tmpl> pool;
                    if (!civicDone && civicPal.Count > 0 && lot.Zone == "residential" && rnd.NextDouble() < 0.15)
                    { pool = civicPal; civicDone = true; }
                    else if (lot.Zone == "core")
                        pool = towerPal.Count > 0 && rnd.NextDouble() < 0.55 ? towerPal
                             : shopPal.Count > 0 ? shopPal : housePal;
                    else if (lot.Zone == "residential")
                        pool = shopPal.Count > 0 && rnd.NextDouble() < 0.15 ? shopPal : housePal;
                    else pool = housePal;

                    var tm = pool[rnd.Next(pool.Count)];
                    var b = Clone(tm.Go);
                    PlaceBuildingRot(b, root.transform, pos, f3, tm.E, Mathf.Min(lot.Width, lot.Depth), rnd);
                    Mark(b, "building", 22f, tm.E);
                    placed++;

                    // Bahçe dolgusu: arka boşluğa çalı
                    if (bushPal.Count > 0 && lot.Zone != "core" && rnd.NextDouble() < 0.4)
                    {
                        var bu = bushPal[rnd.Next(bushPal.Count)];
                        var go = Clone(bu.Go);
                        PlaceScaled(go, root.transform, pos - f3 * (lot.Depth * 0.32f), Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f),
                            Target(bu.E) * Var(rnd, 0.3f), true, 0f, false);
                        Mark(go, "bush", Target(bu.E), bu.E);
                    }
                }

                // ARAÇLAR: yol boyunca, yol yönüne hizalı
                if (vehPal.Count > 0)
                {
                    int nV = Mathf.RoundToInt(sizeM / 45f);
                    for (int k = 0; k < nV; k++)
                    {
                        int r = rnd.Next(map.Roads.Count);
                        var pts = new List<(Vector2 p, Vector2 dir)>(CityLayout.ArcWalk(map.Roads[r], 17f));
                        if (pts.Count == 0) continue;
                        var (p, dir) = pts[rnd.Next(pts.Count)];
                        var perp = new Vector2(-dir.y, dir.x) * (map.RoadWidths[r] * 0.22f) * Sign(rnd);
                        var tm = vehPal[rnd.Next(vehPal.Count)];
                        var go = Clone(tm.Go);
                        PlaceVehicleDir(go, root.transform, new Vector3(p.x + perp.x, 0f, p.y + perp.y),
                            new Vector3(dir.x, 0f, dir.y), Target(tm.E) * Var(rnd, 0.1f), rnd);
                        Mark(go, "vehicle", Target(tm.E), tm.E);
                    }
                }

                // SOKAK LAMBALARI: arterler boyunca dönüşümlü kenar
                if (lampPal.Count > 0)
                {
                    int side = 1;
                    for (int a = 0; a < map.ArteryCount; a++)
                        foreach (var (p, dir) in CityLayout.ArcWalk(map.Roads[a], 30f))
                        {
                            side = -side;
                            var perp = new Vector2(-dir.y, dir.x) * side * (map.RoadWidths[a] * 0.5f + 1f);
                            var tm = lampPal[rnd.Next(lampPal.Count)];
                            var go = Clone(tm.Go);
                            PlaceScaled(go, root.transform, new Vector3(p.x + perp.x, 0f, p.y + perp.y),
                                Quaternion.LookRotation(new Vector3(-perp.x, 0f, -perp.y).normalized),
                                Target(tm.E), true, 0f, false);
                            Mark(go, "lamp", Target(tm.E), tm.E);
                        }
                }

                DestroyPalettes(housePal, shopPal, towerPal, civicPal, treePal, bushPal, vehPal, lampPal, benchPal);

                string lint = SceneLint.Audit(root, null);
                Selection.activeGameObject = root;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

                // "HAZIR" DEMEDEN ÖNCE: AI görsel denetim — sahne envanteriyle bakar,
                // uymayan asset'lerin TÜM kopyalarını otomatik kaldırır.
                // AI görsel denetim KALDIRILDI (2026-07) — bkz. TerrainGen'deki not.
                root = null;
                log?.Invoke(NovaLocale.T("world.status.mapReadyExplore",
                    NovaLocale.T("city.organicReady", map.Roads.Count, placed, parks, lint)));
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("world.status.cityBuildFailed", e.Message)); Debug.LogException(e); }
            finally
            {
                _building = false;
                NovaEditorGuard.EndSyncShaders(_shaderSync);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
#else
            log?.Invoke(NovaLocale.T("world.status.gltfastMissing"));
            await Task.CompletedTask;
#endif
        }

        // KÜRATÖR: adayları backend'e gönder, LLM'in seçtiği dosyalarla dön. Hata/anahtar yoksa ilk-N.
        // internal: TerrainGen (arazi paleti) de aynı beyni kullanır.
        internal static async Task<Dictionary<string, List<AssetCatalog.Entry>>> Curate(
            string mapType, string theme,
            Dictionary<string, (List<AssetCatalog.Entry> pool, int count)> roles, Action<string> log)
        {
            var result = roles.ToDictionary(kv => kv.Key, kv => kv.Value.pool.Take(kv.Value.count).ToList());
            try
            {
                var candidates = new Dictionary<string, object>();
                var counts = new Dictionary<string, object>();
                foreach (var kv in roles)
                {
                    counts[kv.Key] = kv.Value.count;
                    // 22 aday yeter: Groq ücretsiz katman TPM 8000 — şişkin listeler 429 yiyordu
                    candidates[kv.Key] = kv.Value.pool.Take(22).Select(e => (object)new Dictionary<string, object>
                    {
                        { "file", e.file }, { "name", e.name },
                        { "theme", string.IsNullOrEmpty(e.theme) ? "generic" : e.theme },
                        { "family", e.family ?? "" },
                        { "size", Mathf.Max(e.sizeMeters != null ? e.sizeMeters.y : 0f, 0f) * (e.unitScale > 1e-6f ? e.unitScale : 1f) },
                    }).ToList();
                }
                var body = new Dictionary<string, object>
                { { "mapType", mapType }, { "theme", theme }, { "candidates", candidates }, { "counts", counts } };
                using var req = new HttpRequestMessage(HttpMethod.Post, UnityAIConfig.BaseUrl + "/v1/world/curate");
                req.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await Http.SendAsync(req);
                if ((int)resp.StatusCode == 404)
                {
                    Debug.LogError("[Nova Küratör] /v1/world/curate YOK (404) — backend ESKİ. 'cd backend && npm run dev' ile yeniden başlat!");
                    log?.Invoke(NovaLocale.T("city.curatorBackendOld"));
                    return result;
                }
                string txt = await resp.Content.ReadAsStringAsync();
                if (Json.Deserialize(txt) is Dictionary<string, object> root && root.TryGetValue("picks", out var pv) && pv is Dictionary<string, object> picks)
                {
                    foreach (var kv in roles)
                    {
                        if (picks.TryGetValue(kv.Key, out var lv) && lv is List<object> files && files.Count > 0)
                        {
                            var byFile = kv.Value.pool.GroupBy(e => e.file).ToDictionary(g => g.Key, g => g.First());
                            var sel = files.Select(f => f?.ToString())
                                .Where(f => !string.IsNullOrEmpty(f) && byFile.ContainsKey(f))
                                .Select(f => byFile[f]).ToList();
                            if (sel.Count > 0) result[kv.Key] = sel;
                        }
                    }
                    string src = root.TryGetValue("source", out var s) ? s?.ToString() : "?";
                    if (root.TryGetValue("notes", out var nv) && nv is List<object> notes && notes.Count > 0)
                        Debug.Log($"[Nova Küratör] ({src}) " + string.Join(" · ", notes));
                    if (src == "ai")
                        log?.Invoke(NovaLocale.T("city.curatorPicked"));
                    else
                    {
                        Debug.LogWarning("[Nova Küratör] AI seçim YAPILAMADI (source=" + src + ") — GROQ_API_KEY / backend terminalini kontrol et.");
                        log?.Invoke(NovaLocale.T("city.curatorFailed", src));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Nova Küratör] ULAŞILAMADI: " + e.Message + " — backend çalışıyor mu? (npm run dev)");
                log?.Invoke(NovaLocale.T("city.curatorUnreachable", e.Message));
            }
            return result;
        }

        // Bina: serbest yöne dönük yerleştirme (organik yollar için) + setback
        private static void PlaceBuildingRot(GameObject go, Transform parent, Vector3 center, Vector3 facing,
            AssetCatalog.Entry e, float plot, System.Random rnd)
        {
            var rot = Quaternion.LookRotation(facing);
            go.transform.SetParent(parent);
            go.transform.rotation = rot;
            if (!CalcBounds(go, out var raw)) { go.transform.position = center; return; }

            float unit = e != null && e.unitScale > 1e-6f ? e.unitScale : 1f;
            float realH = raw.size.y * unit;
            float realFoot = Mathf.Max(raw.size.x, raw.size.z) * unit;
            float scale;
            if (realH >= 2.5f && realH <= 60f && realFoot <= plot * 0.95f && realFoot >= 3f)
                scale = unit * Var(rnd, 0.08f);
            else
            {
                float foot = Mathf.Max(Mathf.Max(raw.size.x, raw.size.z), 1e-4f);
                scale = FootTarget(e?.role) * Var(rnd, 0.12f) / foot;
                float h = raw.size.y * scale;
                if (h > 60f) scale *= 60f / h;
                if (h < 2.8f) scale *= 2.8f / h;
                float f2 = foot * scale;
                if (f2 > plot * 0.98f) scale *= plot * 0.98f / f2;
            }
            go.transform.localScale *= Mathf.Clamp(scale, 1e-6f, 1e6f);

            CalcBounds(go, out var b);
            go.transform.position += new Vector3(center.x - b.center.x, center.y - b.min.y, center.z - b.center.z);
            CalcBounds(go, out b);

            // SETBACK: cephe yol kenarına yaklaşsın
            float extent = Mathf.Abs(facing.x) * b.extents.x + Mathf.Abs(facing.z) * b.extents.z;
            float shift = Mathf.Clamp(plot * 0.5f - extent - 0.8f, 0f, plot * 0.3f);
            go.transform.position += facing * shift;

            CalcBounds(go, out b);
            var col = new GameObject(go.name + "_col");
            col.transform.SetParent(parent);
            col.transform.position = b.center;
            col.AddComponent<BoxCollider>().size = b.size;
            _lastCol = col;
        }

        // Araç: verilen yön vektörüne hizalı (organik yollar)
        private static void PlaceVehicleDir(GameObject go, Transform parent, Vector3 pos, Vector3 dir,
            float targetLen, System.Random rnd)
        {
            go.transform.rotation = Quaternion.identity;
            if (!CalcBounds(go, out var raw)) { go.transform.SetParent(parent); go.transform.position = pos; return; }
            bool longIsX = raw.size.x >= raw.size.z;
            float baseYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yaw = baseYaw + (longIsX ? -90f : 0f);
            if (rnd.NextDouble() < 0.5) yaw += 180f;
            PlaceScaled(go, parent, pos, Quaternion.Euler(0f, yaw, 0f), targetLen, false, 0f, false);
        }

        // ---- Yardımcılar ----
        private static GameObject _lastCol; // son bina collider'ı — Mark ile objeye bağlanır

        // Denetçi işareti: beklenen boyut + kaynak dosya (SceneLint bununla dev/uçan objeyi bulur)
        private static void Mark(GameObject go, string role, float target, AssetCatalog.Entry e)
        {
            if (go == null) return;
            var m = go.AddComponent<NovaWorld.NovaPlaced>();
            m.role = role; m.targetSize = target; m.assetFile = e != null ? e.file : go.name;
            m.linkedCollider = _lastCol; _lastCol = null;
        }

        private static float Target(AssetCatalog.Entry e) => AssetCatalog.TargetOf(e, RoleDefault(e.role));
        private static float Var(System.Random rnd, float amt) => 1f + ((float)rnd.NextDouble() * 2f - 1f) * amt;
        private static float Sign(System.Random rnd) => rnd.NextDouble() < 0.5 ? -1f : 1f;

        private static List<AssetCatalog.Entry> Pool(string role, string style)
        {
            var l = Cap(AssetCatalog.FilterRoles(new[] { role }, style));
            return l;
        }

        // Merkeze doğru bakan cephe yönü: 0=+Z(N) 1=+X(E) 2=-Z(S) 3=-X(W); %25 rastgele
        private static int FrontToward(Vector3 toCenter, System.Random rnd)
        {
            if (rnd.NextDouble() < 0.25 || toCenter.sqrMagnitude < 0.01f) return rnd.Next(4);
            return Mathf.Abs(toCenter.x) > Mathf.Abs(toCenter.z)
                ? (toCenter.x > 0 ? 1 : 3)
                : (toCenter.z > 0 ? 0 : 2);
        }

        private static readonly Vector3[] FrontDirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        // Çok ağır meshleri ele (native import/collider çökmesini önler)
        private static List<AssetCatalog.Entry> Cap(List<AssetCatalog.Entry> list) =>
            list.Where(e => e.triangles >= 0 && e.triangles <= MaxTriangles).ToList();

        private static string Dominant(List<AssetCatalog.Entry> list, Func<AssetCatalog.Entry, string> key) =>
            list.GroupBy(key).OrderByDescending(g => g.Count()).First().Key;

        private struct Tmpl { public GameObject Go; public AssetCatalog.Entry E; }

#if GLTFAST_INSTALLED
        // Sınırlı sayıda benzersiz asset'i BİR KEZ import eder (şablon paleti) — Entry ile eşli.
        private static async Task<List<Tmpl>> BuildPalette(List<AssetCatalog.Entry> pool, int count, System.Random rnd, Action<string> log)
        {
            var templates = new List<Tmpl>();
            if (pool == null || pool.Count == 0) return templates;
            var picked = pool.OrderBy(_ => rnd.Next()).Take(Mathf.Min(count, pool.Count));
            foreach (var e in picked)
            {
                var t = await Import(e, log);
                if (t != null)
                {
                    t.SetActive(false); t.hideFlags = HideFlags.HideAndDontSave;
                    templates.Add(new Tmpl { Go = t, E = e });
                    Debug.Log($"[Nova] Palet ({e.role}): {e.file} · family={e.family} · {e.triangles} tri");
                }
            }
            return templates;
        }

        private static void DestroyPalettes(params List<Tmpl>[] pals)
        {
            foreach (var pal in pals)
                foreach (var t in pal)
                    if (t.Go != null) UnityEngine.Object.DestroyImmediate(t.Go);
        }

        private static GameObject Clone(GameObject tmpl)
        {
            var c = UnityEngine.Object.Instantiate(tmpl);
            c.hideFlags = HideFlags.None;
            c.SetActive(true);
            return c;
        }

        private static async Task<GameObject> Import(AssetCatalog.Entry e, Action<string> log)
        {
            try
            {
                var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                var importSettings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                // Model yerelde yoksa buluttan indir (lazy dağıtım); indirilemezse atla
                var uri = await NovaAssetDownloader.EnsureUri(e);
                if (string.IsNullOrEmpty(uri)) return null;
                bool ok = await gltf.Load(uri, importSettings);
                if (!ok) return null;
                var go = new GameObject(e.name);
                var settings = new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh };
                var inst = new GLTFast.GameObjectInstantiator(gltf, go.transform, null, settings);
                bool okInst = await gltf.InstantiateMainSceneAsync(inst);
                if (!okInst) { UnityEngine.Object.DestroyImmediate(go); return null; }
                return go;
            }
            catch (Exception ex) { log?.Invoke(NovaLocale.T("terrain.importSkipped", e.name, ex.Message)); return null; }
        }
#endif

        private static bool CalcBounds(GameObject go, out Bounds b)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { b = default; return false; }
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return true;
        }

        /// <summary>
        /// GERÇEK ÖLÇEK yerleştirme: hedef boy (metre) birincil eksene uygulanır.
        /// heightAxis=true → yükseklik hedeflenir; false → uzun yatay kenar.
        /// footCap>0 ise taban izi o değeri aşamaz (bina parsele taşmasın).
        /// </summary>
        private static Bounds PlaceScaled(GameObject go, Transform parent, Vector3 pos, Quaternion rot,
            float target, bool heightAxis, float footCap, bool addCollider)
        {
            go.transform.SetParent(parent);
            go.transform.rotation = rot;
            if (!CalcBounds(go, out var b)) { go.transform.position = pos; return default; }

            float dim = heightAxis ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            float scale = dim > 1e-4f ? target / dim : 1f;
            if (footCap > 0f)
            {
                float foot = Mathf.Max(b.size.x, b.size.z) * scale;
                if (foot > footCap) scale *= footCap / foot;
            }
            go.transform.localScale *= Mathf.Clamp(scale, 1e-6f, 1e6f);

            CalcBounds(go, out b);
            go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
            CalcBounds(go, out b);

            if (addCollider)
            {
                var col = new GameObject(go.name + "_col");
                col.transform.SetParent(parent);
                col.transform.position = b.center;
                var bc = col.AddComponent<BoxCollider>();
                bc.size = b.size;
            }
            return b;
        }

        // Rolün taban izi hedefi (m) — bina ölçeği YÜKSEKLİĞE değil TABANA göre normalize edilir.
        // Yükseklik modelin kendi oranından gelir: kulübe alçak kalır, çok katlı doğal yükselir.
        private static float FootTarget(string role)
        {
            switch (role)
            {
                case "shop": return 9f;
                case "civic": return 12f;
                case "tower": return 9f;   // kule dar tabanlı ama oranı gereği uzun çıkar
                default: return 8f;        // house
            }
        }

        // Bina: yola dönük + setback. front: 0=+Z 1=+X 2=-Z 3=-X (cephenin baktığı yol tarafı).
        private static void PlaceBuilding(GameObject go, Transform parent, Vector3 center, int front,
            AssetCatalog.Entry e, float plot, System.Random rnd, Action<string> log)
        {
            // Model cephesinin +Z'ye baktığı varsayımı (yaygın konvansiyon; Faz 3 blokları bunu küratörle düzeltecek)
            var rot = Quaternion.LookRotation(FrontDirs[front]);

            go.transform.SetParent(parent);
            go.transform.rotation = rot;
            if (!CalcBounds(go, out var raw)) { go.transform.position = center; return; }

            // 1) Yazarın gerçek ölçeği makulse ONA GÜVEN (unitScale ile metreye çevrilmiş boy)
            float unit = e != null && e.unitScale > 1e-6f ? e.unitScale : 1f;
            float realH = raw.size.y * unit;
            float realFoot = Mathf.Max(raw.size.x, raw.size.z) * unit;
            float scale;
            if (realH >= 2.5f && realH <= 60f && realFoot <= plot * 0.95f && realFoot >= 3f)
            {
                scale = unit * Var(rnd, 0.08f);
            }
            else
            {
                // 2) TABAN İZİ normalizasyonu: taban rol hedefine, yükseklik modelin oranına
                float foot = Mathf.Max(Mathf.Max(raw.size.x, raw.size.z), 1e-4f);
                scale = FootTarget(e?.role) * Var(rnd, 0.12f) / foot;
                float h = raw.size.y * scale;
                if (h > 60f) scale *= 60f / h;        // gökdelen tavanı
                if (h < 2.8f) scale *= 2.8f / h;      // en az tek kat
                float f2 = foot * scale;
                if (f2 > plot * 0.95f) scale *= plot * 0.95f / f2; // parsel taşması: son söz tabanın
            }

            go.transform.localScale *= Mathf.Clamp(scale, 1e-6f, 1e6f);
            CalcBounds(go, out var b);
            go.transform.position += new Vector3(center.x - b.center.x, center.y - b.min.y, center.z - b.center.z);
            CalcBounds(go, out b);

            var colGo = new GameObject(go.name + "_col");
            colGo.transform.SetParent(parent);
            colGo.transform.position = b.center;
            colGo.AddComponent<BoxCollider>().size = b.size;
            _lastCol = colGo;
            if (b.size == Vector3.zero) return;

            // SETBACK: cepheyi yol kenarına yaklaştır (öndeki boşluk arkaya kalsın)
            var dir = FrontDirs[front];
            float extent = Mathf.Abs(dir.x) > 0.5f ? b.extents.x : b.extents.z;
            float shift = Mathf.Clamp(plot * 0.5f - extent - 0.8f, 0f, plot * 0.28f);
            go.transform.position += dir * shift;
            colGo.transform.position += dir * shift; // collider birlikte kayar (isimle aramak yanlış klonu bulabiliyordu)
        }

        // Araç: yol eksenine hizalı (uzun ekseni yola paralel), rastgele yön (gidiş/geliş)
        private static void PlaceVehicle(GameObject go, Transform parent, Vector3 pos, bool alongX,
            float targetLen, System.Random rnd)
        {
            go.transform.rotation = Quaternion.identity;
            if (!CalcBounds(go, out var raw)) { go.transform.SetParent(parent); go.transform.position = pos; return; }
            bool longIsX = raw.size.x >= raw.size.z;
            float yaw = alongX == longIsX ? 0f : 90f;
            if (rnd.NextDouble() < 0.5) yaw += 180f; // gidiş/geliş
            PlaceScaled(go, parent, pos, Quaternion.Euler(0f, yaw, 0f), targetLen, false, 0f, false);
        }

        private static void MakeStrip(Transform parent, Material mat, Vector3 pos, Vector3 size)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "Road";
            s.transform.SetParent(parent);
            s.transform.position = pos;
            s.transform.localScale = size;
            s.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void SetMat(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = SolidMat(c);
        }

        private static Material SolidMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}
