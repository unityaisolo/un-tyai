using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>Menüden gelen deterministik arazi reçetesi (LLM yok).</summary>
    public class TerrainPlan
    {
        public string biome = "plains"; // plains | forest | valley | hills | coast | desert
        public int size = 400;          // metre (kare kenarı)
        public bool river, lake;
        public float riverCurve = 0.5f; // 0 = dümdüz kanal · 0.5 = doğal · 1 = güçlü menderes
        public float relief = 0.5f;     // engebe: 0 = düz — 1 = sarp
        public bool trees = true, rocks = true, bushes = true;
        public bool flowers = false;    // çiçek/çimen detay katmanı (katalog 'flower' rolü)
        public bool path = false;       // kıvrımlı toprak patika (T5 v1)
        public float density = 0.6f;    // genel bitki yoğunluğu
        public float treeMul = 1f, rockMul = 1f, bushMul = 1f; // rol başına çarpan (0-2)
        public bool addPlayer = true;   // kurulum sonunda FPS oyuncusu ekle (Play'e hazır)
    }

    /// <summary>
    /// Biome arazi üreteci — Unity Terrain + Perlin/fBm heightmap. Asset GEREKTİRMEZ:
    /// zemin prosedürel, dokular ambientCG'den (textures-raw), bitkiler katalogdan (assets-raw).
    /// Biome'lar: ova, orman, dağ vadisi (etrafı dağlarla çevrili + ırmak), tepelik, sahil, çöl.
    /// </summary>
    public static class TerrainGen
    {
        private const int Res = 257;        // heightmap çözünürlüğü
        private const int AlphaRes = 128;   // splat çözünürlüğü
        private static bool _building;

        public static async void Build(TerrainPlan p, int seed, Action<string> log)
        {
            if (NovaEditorGuard.BlockIfBusy(log)) return; // derleme/import sırasında GPU çökmesini önle
            if (_building) { log?.Invoke(NovaLocale.T("world.status.alreadyBuilding")); return; }
            _building = true;
            bool _shaderSync = NovaEditorGuard.BeginSyncShaders(); // DX12 çökme önlemi (mid-render shader swap yok)
            GameObject root = null;
            try
            {
                var rnd = new System.Random(seed);
                float sizeM = Mathf.Clamp(p.size, 150, 1000);
                float heightScale = p.biome == "valley" ? 80f : p.biome == "hills" ? 35f
                                  : p.biome == "coast" ? 28f : p.biome == "desert" ? 22f
                                  : p.biome == "forest" ? 30f
                                  : p.biome == "snow" ? 95f       // yüksek karlı dağlar
                                  : p.biome == "swamp" ? 10f      // neredeyse düz bataklık
                                  : p.biome == "canyon" ? 65f     // derin kanyon/mesa
                                  : p.biome == "volcanic" ? 70f   // volkan konisi
                                  : 15f;

                // ---- ÇEŞİTLİLİK: her seed'de farklı karakterde harita ----
                // Engebe artık kullanıcı slider'ında (relief); seed küçük bir oynama katar.
                heightScale *= Mathf.Lerp(0.55f, 1.6f, Mathf.Clamp01(p.relief))
                             * (0.92f + 0.16f * (float)rnd.NextDouble());
                float freqMul = 0.75f + 0.6f * (float)rnd.NextDouble(); // tepe sıklığı/iriliği

                root = new GameObject($"NovaTerra_{seed}");
                Undo.RegisterCreatedObjectUndo(root, "Nova: Arazi");

                // Önceki haritanın NavMesh'i sahne verisinde duruyor olabilir; yeni arazinin
                // altında eski mavi yüzey olarak görünür ve kullanıcı kaldıramaz. Baştan sil.
                WorldPrep.ClearNavMesh(null);

                // ---- 1) HEIGHTMAP ----
                log?.Invoke(NovaLocale.T("terrain.shaping"));
                float ox = (float)rnd.NextDouble() * 1000f, oz = (float)rnd.NextDouble() * 1000f;
                float phase = (float)rnd.NextDouble() * 10f;
                float waterLevel = -1f; // normalize (0-1); <0 = su yok

                // Bataklıkta su her yeri kaplar (biome doğası gereği ıslak zemin)
                bool hasWater = p.river || p.lake || p.biome == "coast" || p.biome == "swamp";
                if (p.biome == "coast") waterLevel = 0.30f + Rf(rnd, 0.03f);
                else if (p.biome == "swamp") waterLevel = 0.14f + Rf(rnd, 0.01f); // sığ, geniş su
                else if (hasWater) waterLevel = (p.biome == "valley" ? 0.10f : 0.12f) + Rf(rnd, 0.015f);

                var h = new float[Res, Res];
                for (int j = 0; j < Res; j++)
                for (int i = 0; i < Res; i++)
                {
                    float u = i / (float)(Res - 1), v = j / (float)(Res - 1);
                    h[j, i] = BiomeHeight(p.biome, u, v, ox, oz, freqMul);
                }

                // Nehir kıvrımı: slider değeri + seed'den küçük oynama; göl boyutu/konumu da oynar
                if (p.river) CarveRiver(h, waterLevel, phase, Mathf.Clamp01(p.riverCurve + Rf(rnd, 0.1f)));
                if (p.lake) CarveLake(h, waterLevel, 0.5f + Rf(rnd, 0.12f), 0.5f + Rf(rnd, 0.12f), 0.14f + Rf(rnd, 0.045f));

                // ---- PATİKA (T5 v1): haritayı boydan boya geçen kıvrımlı toprak yol ----
                // Splat'e toprak boyanır ve bitki saçılımı yoldan kaçınır (kazı yok — doğal iz).
                float[] pathVArr = null;
                float pathHalf = 0f;
                if (p.path)
                {
                    float pPhase = (float)rnd.NextDouble() * 10f;
                    pathVArr = new float[Res];
                    for (int i = 0; i < Res; i++)
                    {
                        float u = i / (float)(Res - 1);
                        pathVArr[i] = 0.5f + 0.18f * Mathf.Sin(u * 2.7f + pPhase)
                                           + 0.07f * Mathf.Sin(u * 6.3f + pPhase * 1.7f);
                    }
                    // Yarı genişlik (normalize): ~3 m, ama splat çözünürlüğünde en az ~1 piksel
                    pathHalf = Mathf.Max(3f / sizeM, 1.2f / AlphaRes);
                }

                var td = new TerrainData { heightmapResolution = Res };
                td.SetHeights(0, 0, h);
                td.size = new Vector3(sizeM, heightScale, sizeM);

                // ---- 2) DOKULAR (ambientCG → TerrainLayer) ----
                ApplyLayers(td, p.biome, waterLevel, log, pathVArr, pathHalf);

                var tGo = Terrain.CreateTerrainGameObject(td);
                tGo.name = "Terrain";
                tGo.transform.SetParent(root.transform);
                tGo.transform.position = Vector3.zero;
                var terrain = tGo.GetComponent<Terrain>();
                // Delikleri temizle: aynı sahnede daha önce fırçayla delik açıldıysa
                // yeni harita da deliksiz başlasın (delik = collider yok = oyuncu düşer).
                int hres = td.holesResolution;
                var noHoles = new bool[hres, hres];
                for (int j = 0; j < hres; j++) for (int i = 0; i < hres; i++) noHoles[j, i] = true;
                td.SetHoles(0, 0, noHoles);

                // ---- 3) SU ----
                float waterY = waterLevel > 0f ? waterLevel * heightScale : -999f;
                if (hasWater)
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    w.name = "Water";
                    UnityEngine.Object.DestroyImmediate(w.GetComponent<Collider>());
                    w.transform.SetParent(root.transform);
                    // Su düzlemi TAM arazi kadar olsun. Eskiden +1 pay veriliyordu ve
                    // uzaktan bakınca arazinin dışına taşan mavi bir kare gibi görünüyordu.
                    // Unity Plane primitive'i 10x10 m'dir → ölçek = kenar / 10.
                    w.transform.position = new Vector3(sizeM / 2f, waterY, sizeM / 2f);
                    w.transform.localScale = new Vector3(sizeM / 10f, 1f, sizeM / 10f);
                    w.GetComponent<Renderer>().sharedMaterial = WaterMat();
                }

                // ---- 4) BİTKİ/KAYA SAÇILIMI (katalogdan gerçek assetler) ----
                var nature = new GameObject("Nature");
                nature.transform.SetParent(root.transform);
                float area = sizeM * sizeM;

                // Doluluk da seed'le oynar (±%20); rol başına çarpanlar (Gelişmiş) uygulanır
                float fill = 0.8f + 0.4f * (float)rnd.NextDouble();
                float dT = p.density * Mathf.Clamp(p.treeMul, 0f, 2f);
                float dR = p.density * Mathf.Clamp(p.rockMul, 0f, 2f);
                float dB = p.density * Mathf.Clamp(p.bushMul, 0f, 2f);
                // Ağaç aralığı (m² başına): forest sık, çöl/volkanik/kanyon seyrek, bataklık orta
                float treeArea = p.biome == "forest" ? 220f
                               : p.biome == "desert" ? 4000f
                               : p.biome == "volcanic" ? 6000f   // çorak, çok az ölü ağaç
                               : p.biome == "canyon" ? 3500f     // seyrek
                               : p.biome == "swamp" ? 700f       // sık ama kısa/ölü
                               : p.biome == "snow" ? 500f        // iğne yapraklı orman
                               : 900f;
                float rockArea = (p.biome == "desert" || p.biome == "valley" || p.biome == "canyon" || p.biome == "volcanic") ? 1500f : 3000f;
                int nTree = !p.trees ? 0 : Mathf.Min(800, Mathf.RoundToInt(area / treeArea * dT * 1.6f * fill));
                int nRock = !p.rocks ? 0 : Mathf.Min(300, Mathf.RoundToInt(area / rockArea * dR * 1.6f * fill));
                int nBush = !p.bushes ? 0 : Mathf.Min(300, Mathf.RoundToInt(area / 1200f * dB * 1.6f * fill));
                int nFlower = !p.flowers ? 0 : Mathf.Min(350, Mathf.RoundToInt(area / 1000f * dB * 1.6f * fill));

                // DÜŞÜK-VRAM GÜVENLİĞİ: zayıf GPU'larda (ör. GTX 1650, 4 GB) çok sayıda model +
                // doku GPU belleğini taşırıp DirectX "device lost" çökmesine yol açabilir.
                // Grafik belleğine göre bitki sayısına yumuşak tavan koy (yalnız <6 GB kartlarda).
                int vram = SystemInfo.graphicsMemorySize; // MB (0 = bilinmiyor)
                if (vram > 0 && vram < 6000)
                {
                    float k = vram < 3000 ? 0.40f : vram < 4500 ? 0.55f : 0.75f;
                    nTree = Mathf.RoundToInt(nTree * k); nRock = Mathf.RoundToInt(nRock * k);
                    nBush = Mathf.RoundToInt(nBush * k); nFlower = Mathf.RoundToInt(nFlower * k);
                    Debug.Log($"[Nova] Düşük-VRAM ({vram} MB): bitki sayısı %{k * 100:0} ölçeklendi (GPU çökme önlemi).");
                }

#if GLTFAST_INSTALLED
                // Asset kütüphanesi yok/yanlış yolda ise: arazi + dokular YİNE kurulur,
                // sadece bitki saçılımı atlanır (boş sahne yerine kullanılabilir sonuç).
                var treePal = new List<Tmpl>();
                var rockPal = new List<Tmpl>();
                var bushPal = new List<Tmpl>();
                var flowerPal = new List<Tmpl>();
                // Sayaçları HER kurulumda sıfırla. Eskiden yalnızca kütüphane bulunduğunda
                // sıfırlanıyordu; kütüphane yokken önceki kurulumun sayıları raporlanıyor,
                // "0 obje yerleşti" ama "17 yerel klasör" gibi çelişkili teşhis çıkıyordu.
                NovaAssetDownloader.ResetStats();

                bool libReady = NovaAssetLibrary.EnsureReady(log, prompt: true);
                if (!libReady)
                {
                    nTree = nRock = nBush = nFlower = 0;
                    log?.Invoke(NovaLocale.T("lib.terrainNoPlants"));
                }
                else
                {
                    AssetCatalog.Load(null, true);
                    // TEŞHİS: kütüphane nereden okunuyor? (yerel geliştirici klasörü mü,
                    // proje içi bulut indirmesi mi) — beta destek sorularının çoğu bu.
                    Debug.Log("[Nova] Katalog: " + UnityAIConfig.CatalogPath);
                    log?.Invoke(NovaLocale.T("terrain.naturePalette"));
                    // KÜRATÖR BEYİN: aday listesi backend'e (/v1/world/curate) gider, LLM biome'a
                    // uygun tutarlı seti seçer. Anahtar yoksa/backend kapalıysa Curate karışık
                    // sıradaki ilk-N ile döner — eski rastgele davranışa eşdeğer, akış bozulmaz.
                    var pools = new Dictionary<string, (List<AssetCatalog.Entry> pool, int count)>
                    {
                        { "tree", (TerraPool("tree", p.biome, rnd), 6) },
                        { "rock", (TerraPool("rock", p.biome, rnd), 5) },
                        { "bush", (TerraPool("bush", p.biome, rnd), 4) },
                    };
                    if (p.flowers) pools["flower"] = (TerraPool("flower", p.biome, rnd), 4);
                    var picks = await WorldBuilderAI.Curate(
                        $"{BiomeName(p.biome)} arazisi (biome: {p.biome})", p.biome, pools, log);

                    // ÇEŞİTLİLİK: küratör hep aynı "en iyi" seti seçme eğilimindedir.
                    // Her rol için havuzdan seçilmemiş 1 sürpriz eleman ekle (havuz her build'de
                    // karışık sırada olduğundan sürpriz de her seferinde değişir).
                    foreach (var role in pools.Keys.ToList())
                    {
                        var pool = pools[role].pool;
                        var sel = picks[role];
                        var extra = pool.FirstOrDefault(e => !sel.Contains(e));
                        if (extra != null && sel.Count > 0) sel.Add(extra);
                    }

                    treePal = await TerraPalette(picks["tree"], log);
                    rockPal = await TerraPalette(picks["rock"], log);
                    bushPal = await TerraPalette(picks["bush"], log);
                    if (p.flowers && picks.ContainsKey("flower"))
                        flowerPal = await TerraPalette(picks["flower"], log);
                }

                // Patika varsa bitkiler yola basmasın
                Func<float, float, bool> blocked = null;
                if (pathVArr != null)
                {
                    float keep = pathHalf * 1.8f;
                    blocked = (u, v) => Mathf.Abs(v - pathVArr[Mathf.Clamp(Mathf.RoundToInt(u * (Res - 1)), 0, Res - 1)]) < keep;
                }

                int placed = 0;
                // Partiler halinde (await'li) — GPU kuyruğunu boğmadan yerleştir
                placed += await Scatter(treePal, nTree, terrain, nature.transform, waterY, 26f, rnd, 0.30f, blocked);
                placed += await Scatter(rockPal, nRock, terrain, nature.transform, waterY, 55f, rnd, 0.45f, blocked);
                placed += await Scatter(bushPal, nBush, terrain, nature.transform, waterY, 32f, rnd, 0.35f, blocked);
                placed += await Scatter(flowerPal, nFlower, terrain, nature.transform, waterY, 22f, rnd, 0.30f, blocked);

                foreach (var t in treePal.Concat(rockPal).Concat(bushPal).Concat(flowerPal))
                    if (t.Go != null) UnityEngine.Object.DestroyImmediate(t.Go);

                // TEŞHİS: modellerin kaynağını raporla (yerel / önbellek / bulut)
                var srcLine = NovaAssetDownloader.StatsLine();
                if (srcLine != null) { Debug.Log("[Nova] " + srcLine); log?.Invoke(srcLine); }

                // DENETÇİ: dev/uçan/gömük objeleri otomatik düzelt
                string lint = SceneLint.Audit(root, null);

                // PLAY'E HAZIR: oyuncuyu şimdi kur. Kullanıcı Play'e bastığında boş sahne
                // yerine gezilebilir bir dünya bulsun (kontroller HUD'da yazılı).
                if (p.addPlayer)
                    WorldExplorer.EnsurePlayer($"{BiomeName(p.biome)} · {sizeM:0} m", null);

                // AI görsel denetim KALDIRILDI (2026-07): vision modeli JSON yerine muhakeme
                // metni döndürüyordu, her kurulumda boşuna token harcıyordu. Deterministik
                // SceneLint denetimi (yukarıdaki `lint`) zaten çalışıyor ve işe yarıyor.
                // Kütüphane yoksa "Harita hazır" DEME. Arazi kuruldu ama bitki/kaya yok;
                // kullanıcı "hazır" yazısını görüp işin bittiğini sanıyordu. Doğru mesaj:
                // ne olduğunu ve ne yapması gerektiğini söyle.
                if (!libReady)
                    log?.Invoke(NovaLocale.T("terrain.readyNoLib", BiomeName(p.biome), sizeM));
                else
                {
                    string done = NovaLocale.T("terrain.ready", BiomeName(p.biome), sizeM, placed, lint);
                    log?.Invoke(NovaLocale.T("world.status.mapReadyExplore", done));
                }
#else
                log?.Invoke(NovaLocale.T("terrain.readyNoGltf", BiomeName(p.biome), sizeM));
                await Task.CompletedTask;
#endif
                Selection.activeGameObject = root;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                root = null;
            }
            catch (Exception e) { log?.Invoke(NovaLocale.T("world.status.mapBuildFailed", e.Message)); Debug.LogException(e); }
            finally
            {
                _building = false;
                NovaEditorGuard.EndSyncShaders(_shaderSync);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static string BiomeName(string b) => b switch
        {
            "valley" => NovaLocale.T("map.valley"), "hills" => NovaLocale.T("map.hills"), "coast" => NovaLocale.T("map.coast"),
            "desert" => NovaLocale.T("map.desert"), "forest" => NovaLocale.T("map.forest"),
            "snow" => NovaLocale.T("map.snow"), "swamp" => NovaLocale.T("map.swamp"),
            "canyon" => NovaLocale.T("map.canyon"), "volcanic" => NovaLocale.T("map.volcanic"),
            _ => NovaLocale.T("map.plains"),
        };

        // ---- Yükseklik fonksiyonları ----
        private static float Fbm(float x, float y, int oct, float freq, float gain)
        {
            float a = 0.5f, f = freq, s = 0f, norm = 0f;
            for (int o = 0; o < oct; o++)
            {
                s += Mathf.PerlinNoise(x * f, y * f) * a;
                norm += a; a *= gain; f *= 2f;
            }
            return s / Mathf.Max(norm, 1e-4f);
        }

        // Ridge noise: sivri sırtlar/dağlar (kumul yerine gerçek dağ silueti)
        private static float Ridge(float x, float y, int oct, float freq, float gain)
        {
            float a = 0.5f, f = freq, s = 0f, norm = 0f;
            for (int o = 0; o < oct; o++)
            {
                float n = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * f, y * f) - 1f);
                s += n * n * a; norm += a; a *= gain; f *= 2f;
            }
            return s / Mathf.Max(norm, 1e-4f);
        }

        // Domain warping: koordinatları gürültüyle büküp doğal, akışkan hatlar üretir
        // (düz Perlin'in "yumru yumru" tekrar hissini kırar — dağlar/vadiler daha organik).
        private static void Warp(ref float x, ref float y, float amount)
        {
            float wx = Fbm(x + 5.2f, y + 1.3f, 2, 1f, 0.5f) - 0.5f;
            float wy = Fbm(x + 9.7f, y + 4.1f, 2, 1f, 0.5f) - 0.5f;
            x += wx * amount; y += wy * amount;
        }

        private static float BiomeHeight(string biome, float u, float v, float ox, float oz, float freqMul = 1f)
        {
            // freqMul: seed'e bağlı frekans çarpanı — aynı biome'da bile tepe iriliği değişir
            float x = u * 4f * freqMul + ox, y = v * 4f * freqMul + oz;
            switch (biome)
            {
                case "valley":
                {
                    // Kenarlarda ridge-noise DAĞ halkası (domain warp ile organik), ortada düz ova
                    float wx = x, wy = y; Warp(ref wx, ref wy, 0.6f);
                    float d = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f; // 0 merkez → 1 kenar
                    float ring = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.98f, d));
                    float mountains = ring * (0.5f + 0.5f * Ridge(wx, wy, 4, 1.5f, 0.5f));
                    float plain = 0.12f + 0.03f * Fbm(x, y, 3, 2.5f, 0.5f);
                    return Mathf.Clamp01(plain + mountains * 0.9f);
                }
                case "hills":
                {
                    float wx = x, wy = y; Warp(ref wx, ref wy, 0.4f);
                    return Mathf.Clamp01(0.15f + 0.55f * Fbm(wx, wy, 4, 1.2f, 0.55f));
                }
                case "coast":
                {
                    // Batı tarafı deniz, doğuya doğru yükselen kıyı
                    float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.75f, u));
                    return Mathf.Clamp01(0.05f + shore * (0.35f + 0.35f * Fbm(x, y, 4, 1.4f, 0.5f)));
                }
                case "desert":
                {
                    // Kumul sırtları (ridged noise)
                    float n = Fbm(x, y, 4, 1.1f, 0.5f);
                    float ridge = 1f - Mathf.Abs(2f * n - 1f);
                    return Mathf.Clamp01(0.2f + 0.35f * ridge + 0.1f * Fbm(x + 9f, y + 9f, 3, 2.2f, 0.5f));
                }
                case "forest":
                    return Mathf.Clamp01(0.18f + 0.4f * Fbm(x, y, 4, 1.3f, 0.5f));

                // ---- YENİ BIOME'LAR ----
                case "snow":
                {
                    // Karlı yüksek dağlar: güçlü ridge + domain warp, tepeler sivri
                    float wx = x, wy = y; Warp(ref wx, ref wy, 0.7f);
                    return Mathf.Clamp01(0.2f + 0.7f * Ridge(wx, wy, 5, 1.3f, 0.55f));
                }
                case "swamp":
                {
                    // Bataklık: neredeyse düz, alçak; hafif tümsek/çukur (su her yeri kaplar)
                    return Mathf.Clamp01(0.10f + 0.06f * Fbm(x, y, 3, 2.0f, 0.5f));
                }
                case "canyon":
                {
                    // Mesa/kanyon: düz platolar + keskin uçurumlar (basamaklama/terracing)
                    float wx = x, wy = y; Warp(ref wx, ref wy, 0.5f);
                    float baseN = Fbm(wx, wy, 4, 1.1f, 0.5f);
                    float terr = Mathf.Round(baseN * 5f) / 5f;                 // 5 basamak → plato hissi
                    return Mathf.Clamp01(0.15f + 0.7f * Mathf.Lerp(terr, baseN, 0.25f));
                }
                case "volcanic":
                {
                    // Merkezde tek büyük volkan konisi + engebeli lav arazisi
                    float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                    float cone = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.05f, 0.55f, d)); // merkez yüksek
                    float crater = d < 0.06f ? -0.25f * Mathf.SmoothStep(0.06f, 0f, d) : 0f;  // tepe krateri çukur
                    float rough = 0.15f * Ridge(x, y, 4, 1.8f, 0.5f);
                    return Mathf.Clamp01(0.12f + cone * 0.75f + crater + rough);
                }

                default: // plains / ova
                    return Mathf.Clamp01(0.25f + 0.10f * Fbm(x, y, 3, 1.8f, 0.5f));
            }
        }

        // Irmak: bir kenardan öbürüne kıvrılan kanal; yatağı su seviyesinin altına kaz
        private static void CarveRiver(float[,] h, float waterLevel, float phase, float curve = 0.5f)
        {
            float bed = waterLevel - 0.035f;
            // curve: 0 = dümdüz kanal · 0.5 = mevcut doğal kıvrım · 1 = güçlü menderes
            float k = Mathf.Lerp(0.15f, 1.6f, Mathf.Clamp01(curve) );
            for (int j = 0; j < Res; j++)
            {
                float v = j / (float)(Res - 1);
                float riverU = 0.5f + 0.20f * k * Mathf.Sin(v * 3.4f + phase) + 0.08f * k * Mathf.Sin(v * 8.1f + phase * 2f);
                for (int i = 0; i < Res; i++)
                {
                    float u = i / (float)(Res - 1);
                    float d = Mathf.Abs(u - riverU);
                    float half = 0.018f;             // ~kanal yarı genişliği (normalize)
                    float bank = half * 3.2f;        // kıyı geçişi
                    if (d > bank) continue;
                    float t = d < half ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - half) / (bank - half));
                    h[j, i] = Mathf.Lerp(h[j, i], Mathf.Min(h[j, i], bed), t);
                }
            }
        }

        private static void CarveLake(float[,] h, float waterLevel, float cu, float cv, float radius)
        {
            float bed = waterLevel - 0.04f;
            for (int j = 0; j < Res; j++)
            for (int i = 0; i < Res; i++)
            {
                float u = i / (float)(Res - 1), v = j / (float)(Res - 1);
                float d = Mathf.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv));
                if (d > radius) continue;
                float t = 1f - Mathf.SmoothStep(0f, 1f, d / radius);
                h[j, i] = Mathf.Lerp(h[j, i], bed, t);
            }
        }

        private static float Rf(System.Random rnd, float amt) => ((float)rnd.NextDouble() * 2f - 1f) * amt;

        // ---- Doku katmanları (textures-raw'dan yükle; yoksa düz renk) ----
        // pathV: patika orta çizgisi (u→v, normalize); pathHalf: yarı genişlik (normalize)
        private static void ApplyLayers(TerrainData td, string biome, float waterLevel, Action<string> log,
            float[] pathV = null, float pathHalf = 0f)
        {
            var texRoot = Path.Combine(Path.GetDirectoryName(UnityAIConfig.CatalogPath) ?? "", "textures-raw");

            TerrainLayer L(string pattern, Color fallback, float tile)
            {
                var (col, nrm) = FindTexture(texRoot, pattern);
                var layer = new TerrainLayer { tileSize = new Vector2(tile, tile) };
                if (col != null) { layer.diffuseTexture = col; if (nrm != null) layer.normalMapTexture = nrm; }
                else layer.diffuseTexture = SolidTex(fallback);
                return layer;
            }

            // 4 KATMAN: ana doku + İKİNCİ ana varyant (tekrar hissini kırar) + yamaç + alçak/kıyı.
            // Yakın çekimde gerçekçilik: küçük tile (yüksek detay) + iki çim varyantının
            // Perlin ile karışması (tek doku 6 m'de tekrar ederken göz hemen fark ediyordu).
            TerrainLayer main, main2, slope, low;
            if (biome == "desert")
            {
                main = L("sand|ground093|ground079", new Color(0.85f, 0.75f, 0.5f), 4f);
                main2 = L("ground0|dirt", new Color(0.80f, 0.70f, 0.48f), 6.5f);
                slope = L("rock|cliff|gravel", new Color(0.5f, 0.45f, 0.4f), 8f);
                low = main;
            }
            else if (biome == "snow")
            {
                // Kar: beyaz zemin, yamaçlarda buzlu kaya, alçakta kirli kar
                main = L("snow|ice", new Color(0.92f, 0.94f, 0.97f), 4f);
                main2 = L("snow|ice", new Color(0.86f, 0.89f, 0.94f), 7f);
                slope = L("rock|cliff|gravel", new Color(0.42f, 0.43f, 0.46f), 8f);
                low = L("snow|ground0", new Color(0.78f, 0.80f, 0.84f), 5f);
            }
            else if (biome == "swamp")
            {
                // Bataklık: koyu yosunlu yeşil + çamur, alçakta ıslak çamur
                main = L("moss|grass00|grass", new Color(0.24f, 0.34f, 0.20f), 3.5f);
                main2 = L("mud|dirt|ground0", new Color(0.28f, 0.26f, 0.18f), 6f);
                slope = L("rock|gravel", new Color(0.35f, 0.36f, 0.32f), 8f);
                low = L("mud|dirt", new Color(0.22f, 0.20f, 0.14f), 4f);
            }
            else if (biome == "canyon")
            {
                // Kanyon/mesa: kırmızımsı-turuncu kaya katmanları
                main = L("rock_red|sand|ground079", new Color(0.72f, 0.45f, 0.30f), 5f);
                main2 = L("rock|cliff|ground0", new Color(0.60f, 0.38f, 0.26f), 8f);
                slope = L("cliff|rock", new Color(0.52f, 0.32f, 0.22f), 9f);
                low = L("sand|dirt", new Color(0.78f, 0.58f, 0.40f), 5f);
            }
            else if (biome == "volcanic")
            {
                // Volkanik: koyu siyah/gri bazalt, yamaçta çıplak kaya
                main = L("rock_black|rock|gravel", new Color(0.18f, 0.17f, 0.17f), 4f);
                main2 = L("gravel|rock|ground0", new Color(0.24f, 0.22f, 0.21f), 6.5f);
                slope = L("cliff|rock", new Color(0.14f, 0.13f, 0.13f), 8f);
                low = L("ash|dirt|gravel", new Color(0.28f, 0.25f, 0.23f), 5f);
            }
            else
            {
                main = L("grass00|grass", new Color(0.3f, 0.5f, 0.25f), 3.5f);   // yakın plan detayı
                main2 = L("grass0|moss|forest", new Color(0.28f, 0.45f, 0.22f), 7f); // farklı ölçek+doku
                slope = L("rock|cliff|gravel", new Color(0.45f, 0.44f, 0.42f), 8f);
                low = L("sand|dirt|ground0", new Color(0.6f, 0.55f, 0.4f), 5f);
            }
            // Çim/toprak yüzeyleri mat olsun (parlak plastik görünümü kırılır)
            foreach (var lay in new[] { main, main2, slope, low })
            {
                if (lay == null) continue;
                lay.specular = new Color(0.03f, 0.03f, 0.03f);
                lay.smoothness = 0.05f;
                lay.metallic = 0f;
                lay.normalScale = 1.1f;
            }
            td.terrainLayers = new[] { main, main2, slope, low };

            // Splat kuralları: dik yamaç → kaya; su kıyısı → kum/toprak; kalanı iki çim varyantı
            var alpha = new float[AlphaRes, AlphaRes, 4];
            td.alphamapResolution = AlphaRes;
            float vn = (float)new System.Random(biome.GetHashCode()).NextDouble() * 50f;
            for (int j = 0; j < AlphaRes; j++)
            for (int i = 0; i < AlphaRes; i++)
            {
                float u = i / (float)(AlphaRes - 1), v = j / (float)(AlphaRes - 1);
                float steep = td.GetSteepness(u, v);
                float hh = td.GetInterpolatedHeight(u, v) / Mathf.Max(td.size.y, 1e-4f);
                float wRock = Mathf.InverseLerp(28f, 45f, steep);
                float wLow = waterLevel > 0f ? Mathf.InverseLerp(waterLevel + 0.05f, waterLevel + 0.005f, hh) : 0f;
                float wGround = Mathf.Clamp01(1f - wRock - wLow);
                // Yumuşak, organik lekeler halinde iki çim varyantını karıştır
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f,
                    Mathf.PerlinNoise(u * 6f + vn, v * 6f + vn)));
                alpha[j, i, 0] = wGround * (1f - blend);
                alpha[j, i, 1] = wGround * blend;
                alpha[j, i, 2] = wRock;
                alpha[j, i, 3] = wLow;

                // Patika: yol şeridinde toprak/kum katmanı baskın olsun
                if (pathV != null)
                {
                    float pv = pathV[Mathf.Clamp(Mathf.RoundToInt(u * (pathV.Length - 1)), 0, pathV.Length - 1)];
                    float dvp = Mathf.Abs(v - pv);
                    float wPath = 1f - Mathf.SmoothStep(pathHalf * 0.45f, pathHalf, dvp);
                    if (wPath > 0f)
                    {
                        alpha[j, i, 0] *= 1f - wPath;
                        alpha[j, i, 1] *= 1f - wPath;
                        alpha[j, i, 2] *= 1f - wPath;
                        alpha[j, i, 3] = Mathf.Max(alpha[j, i, 3], wPath);
                    }
                }
            }
            td.SetAlphamaps(0, 0, alpha);
        }

        // textures-raw altındaki klasörlerde başlığı/dizini pattern'e uyan ilk Color(+Normal) haritası
        private static (Texture2D, Texture2D) FindTexture(string texRoot, string pattern)
        {
            try
            {
                if (!Directory.Exists(texRoot)) return (null, null);
                var re = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (var dir in Directory.GetDirectories(texRoot))
                {
                    if (!re.IsMatch(Path.GetFileName(dir))) continue;
                    var files = Directory.GetFiles(dir);
                    var col = files.FirstOrDefault(f => f.Contains("_Color"));
                    if (col == null) continue;
                    var nrm = files.FirstOrDefault(f => f.Contains("_NormalGL"));
                    return (LoadTex(col), nrm != null ? LoadTex(nrm) : null);
                }
            }
            catch { }
            return (null, null);
        }

        private static Texture2D LoadTex(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            t.LoadImage(File.ReadAllBytes(path));
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        private static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px); t.Apply();
            return t;
        }

        private static Material WaterMat()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "NovaWater" };
            var c = new Color(0.15f, 0.4f, 0.65f, 0.75f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            // Şeffaflık (URP best-effort; olmazsa opak mavi kalır — kabul)
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f); // 0.9 su yüzeyinde beyaz parlama noktaları yapıyordu (vision denetim bulgusu)
            return m;
        }

        // ---- Doğa saçılımı ----
        private struct Tmpl { public GameObject Go; public AssetCatalog.Entry E; }

#if GLTFAST_INSTALLED
        // Aday havuzu: role uygun, makul poligonlu, KARIŞIK sırada.
        // (Küratör beyin adaylardan seçer; beyin yoksa ilk-N alınır = eski rastgele davranış.)
        // GÜVENLİK BAN'LARI: küratör/rastgele seçim ne yaparsa yapsın, biome'a apaçık
        // uymayan adaylar havuza HİÇ girmez (vadide palmiye, kaya rolünde dağ/iceberg vb.).
        private static string TerraBan(string role, string biome)
        {
            if (role == "rock")
            {
                string baseBan = "temple|bridge|iceberg|crystal|cove|swallow|walkway|gem";
                // Kanyon/volkanikte büyük kaya kütleleri (mountain/cliff) İSTENİR — yasağı gevşet
                return (biome == "canyon" || biome == "volcanic") ? baseBan : baseBan + "|mountain|cliff";
            }
            if (role == "tree")
                return biome == "desert" ? "pine|spruce|fir|xmas|christmas|snow|oak|birch"
                     : biome == "coast" ? "xmas|christmas|snow|pine|spruce"
                     : biome == "snow" ? "palm|cactus|xmas|christmas|oak|birch|willow" // karda iğne yapraklı
                     : biome == "swamp" ? "palm|cactus|xmas|christmas|pine|spruce|fir"  // bataklıkta ölü/söğüt
                     : biome == "volcanic" ? "palm|cactus|xmas|christmas|oak|birch|pine|flower" // çoğu ölü
                     : "palm|cactus|xmas|christmas|snow";
            // Budanmış çit + ev/saksı bitkileri doğada olmaz (vision denetimi bunları
            // sürekli yakalıyordu — kaynakta keserek vision kredisi harcamayı önlüyoruz).
            if (role == "bush") return "hedge|houseplant|house.?plant|potted|indoor";
            if (role == "flower")
                return "pot|vase|houseplant|house.?plant|fiddle|monstera|indoor|potted|bonsai|cactus.?pot";
            return null;
        }

        private static List<AssetCatalog.Entry> TerraPool(string role, string biome, System.Random rnd)
        {
            var q = AssetCatalog.FilterRoles(new[] { role }, "any")
                .Where(e => e.triangles >= 0 && e.triangles <= 60000);
            string ban = TerraBan(role, biome);
            if (!string.IsNullOrEmpty(ban))
                q = q.Where(e => !System.Text.RegularExpressions.Regex.IsMatch(
                    e.name ?? "", ban, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            return q.OrderBy(_ => rnd.Next()).ToList();
        }

        private static async Task<List<Tmpl>> TerraPalette(List<AssetCatalog.Entry> picks, Action<string> log)
        {
            var res = new List<Tmpl>();
            foreach (var e in picks)
            {
                try
                {
                    var gltf = new GLTFast.GltfImport(null, new GLTFast.UninterruptedDeferAgent(), null, null);
                    var settings = new GLTFast.ImportSettings { AnimationMethod = GLTFast.AnimationMethod.None };
                    // Model yerelde yoksa buluttan indir (lazy dağıtım); indirilemezse atla
                    var uri = await NovaAssetDownloader.EnsureUri(e);
                    if (string.IsNullOrEmpty(uri)) continue;
                    if (!await gltf.Load(uri, settings)) continue;
                    var go = new GameObject(e.name);
                    var inst = new GLTFast.GameObjectInstantiator(gltf, go.transform, null,
                        new GLTFast.InstantiationSettings { Mask = GLTFast.ComponentType.Mesh });
                    if (!await gltf.InstantiateMainSceneAsync(inst)) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    NovaMeshFix.Repair(go, verbose: true); // vertex renkli modeller beyaz kalmasın (log = kanıt)
                    go.SetActive(false);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    Debug.Log($"[Nova] Palet ({e.role}): {e.file} · family={e.family}");
                    res.Add(new Tmpl { Go = go, E = e });
                    // GPU KORUMASI: ardışık GLB yüklemeleri arasında nefes payı bırak
                    // (mesh+doku upload'ları üst üste binince zayıf DX12 sürücüleri çöküyor)
                    await Task.Delay(40);
                }
                catch (Exception ex) { log?.Invoke(NovaLocale.T("terrain.importSkipped", e.name, ex.Message)); }
            }
            return res;
        }

        /// <summary>
        /// Nesneleri araziye saçar. GPU KORUMASI: yüzlerce nesne TEK karede sahneye girerse
        /// (eski senkron hali) render kuyruğu boğulur ve zayıf DX12 sürücülerinde
        /// "device lost" çökmesi olur. Bu yüzden PARTİLER halinde yerleştirip aralarda
        /// GPU'ya nefes aldırıyoruz (bir sonraki kareye bırak).
        /// </summary>
        private static async Task<int> Scatter(List<Tmpl> pal, int count, Terrain terrain, Transform parent,
            float waterY, float maxSlope, System.Random rnd, float sizeVar,
            Func<float, float, bool> blocked = null)
        {
            if (pal.Count == 0 || count <= 0) return 0;
            var td = terrain.terrainData;
            int placed = 0, tries = count * 4;
            const int BatchSize = 20;   // her 20 nesnede bir GPU'ya nefes ver
            for (int t = 0; t < tries && placed < count; t++)
            {
                if (placed > 0 && placed % BatchSize == 0) await Task.Delay(16); // ~1 kare
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble();
                if (blocked != null && blocked(u, v)) continue;      // patika şeridine değil
                float wx = u * td.size.x, wz = v * td.size.z;
                float wy = terrain.SampleHeight(new Vector3(wx, 0f, wz));
                if (wy < waterY + 0.6f) continue;                    // suyun içine/kıyısına değil
                if (td.GetSteepness(u, v) > maxSlope) continue;      // aşırı dik yamaca değil

                var tm = pal[rnd.Next(pal.Count)];
                var go = UnityEngine.Object.Instantiate(tm.Go);
                go.hideFlags = HideFlags.None;
                go.SetActive(true);
                go.transform.SetParent(parent);
                go.transform.rotation = Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f);

                // realTarget'a ölçekle (yükseklik ekseni), zemine oturt
                var rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) { UnityEngine.Object.DestroyImmediate(go); continue; }
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                // EN BÜYÜK boyuta göre normalize et: yatık dal/kütük gibi alçak-ama-uzun
                // asset'lerde yükseklik hedefi ölçeği patlatıyordu (dev odun hatası).
                float target = AssetCatalog.TargetOf(tm.E, 4f) * (1f + ((float)rnd.NextDouble() * 2f - 1f) * sizeVar);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float scale = maxDim > 1e-4f ? target / maxDim : 1f;
                go.transform.localScale *= Mathf.Clamp(scale, 1e-6f, 1e6f);
                b = go.GetComponentsInChildren<Renderer>()[0].bounds;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                // Eğimli zeminde uçmayı önle: taban izinin 4 köşesinden zemini örnekle,
                // EN DÜŞÜĞÜNE otur; kayaları biraz göm (doğal görünüm + uçma sıfırlanır).
                float ex = b.extents.x, ez = b.extents.z;
                float gMin = wy;
                gMin = Mathf.Min(gMin, terrain.SampleHeight(new Vector3(wx - ex, 0f, wz - ez)));
                gMin = Mathf.Min(gMin, terrain.SampleHeight(new Vector3(wx + ex, 0f, wz - ez)));
                gMin = Mathf.Min(gMin, terrain.SampleHeight(new Vector3(wx - ex, 0f, wz + ez)));
                gMin = Mathf.Min(gMin, terrain.SampleHeight(new Vector3(wx + ex, 0f, wz + ez)));
                float sink = tm.E.role == "rock" ? b.size.y * 0.18f : 0.03f;
                go.transform.position += new Vector3(wx - b.center.x, gMin - b.min.y - sink, wz - b.center.z);
                var mark = go.AddComponent<NovaWorld.NovaPlaced>();
                mark.role = tm.E.role; mark.targetSize = target; mark.assetFile = tm.E.file;
                placed++;
            }
            return placed;
        }
#endif
    }
}
