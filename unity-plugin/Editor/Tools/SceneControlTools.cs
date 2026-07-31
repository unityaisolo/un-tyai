using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    /// <summary>
    /// SAHNE / ARAZİ / ASSET KONTROL ARAÇLARI.
    /// Amaç: kullanıcının Dünya sekmesinde tıklayarak yaptığı her şeyi Kod Ajanı'ndan
    /// cümleyle de yapabilmesi — "araziyi çöle çevir", "şu palmiyeleri kaldır",
    /// "sahnede hangi assetler var" gibi. Hepsi Undo (Ctrl+Z) ile geri alınabilir.
    /// </summary>
    public static class SceneControlHelpers
    {
        /// <summary>Sahnedeki Nova ile yerleştirilmiş nesneleri toplar.</summary>
        public static List<NovaWorld.NovaPlaced> AllPlaced()
            => UnityEngine.Object.FindObjectsByType<NovaWorld.NovaPlaced>(FindObjectsInactive.Include).ToList();

        public static string Str(Dictionary<string, object> a, string k, string def = "")
            => a != null && a.TryGetValue(k, out var v) && v != null ? v.ToString() : def;

        public static float Num(Dictionary<string, object> a, string k, float def)
        {
            if (a != null && a.TryGetValue(k, out var v) && v != null &&
                float.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var f)) return f;
            return def;
        }

        public static bool Flag(Dictionary<string, object> a, string k, bool def)
        {
            if (a != null && a.TryGetValue(k, out var v) && v != null && bool.TryParse(v.ToString(), out var b)) return b;
            return def;
        }
    }

    /// <summary>Sahnedeki asset envanterini döker — AI "ne var?" diye sorabilsin.</summary>
    public class ListPlacedAssetsTool : ITool
    {
        public string Name => "ListPlacedAssets";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            var marks = SceneControlHelpers.AllPlaced();
            if (marks.Count == 0) return ToolResult.Success(NovaLocale.T("tool.listPlacedEmpty"));

            var byFile = new Dictionary<string, (string role, int count)>();
            foreach (var m in marks)
            {
                if (m == null || string.IsNullOrEmpty(m.assetFile)) continue;
                byFile.TryGetValue(m.assetFile, out var cur);
                byFile[m.assetFile] = (m.role ?? cur.role ?? "?", cur.count + 1);
            }

            var lines = byFile.OrderByDescending(k => k.Value.count).Take(40)
                .Select(k => $"{k.Key} · rol={k.Value.role} · adet={k.Value.count}");
            return ToolResult.Success(
                NovaLocale.T("tool.listPlacedSummary", marks.Count, byFile.Count, string.Join("\n", lines)),
                new Dictionary<string, object> { { "total", marks.Count }, { "kinds", byFile.Count } });
        }
    }

    /// <summary>Belirli bir asseti (veya rolü) sahneden kaldırır — "şu ağaçları sil".</summary>
    public class RemovePlacedAssetsTool : ITool
    {
        public string Name => "RemovePlacedAssets";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string match = SceneControlHelpers.Str(args, "match");
            string role = SceneControlHelpers.Str(args, "role");
            if (string.IsNullOrEmpty(match) && string.IsNullOrEmpty(role))
                return ToolResult.Failure(NovaLocale.T("tool.needMatchOrRole"));

            var marks = SceneControlHelpers.AllPlaced();
            int removed = 0;
            foreach (var m in marks)
            {
                if (m == null) continue;
                bool hitRole = !string.IsNullOrEmpty(role) &&
                               string.Equals(m.role, role, StringComparison.OrdinalIgnoreCase);
                bool hitName = !string.IsNullOrEmpty(match) &&
                               ((m.assetFile ?? "").IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                m.gameObject.name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hitRole && !hitName) continue;

                if (m.linkedCollider != null) Undo.DestroyObjectImmediate(m.linkedCollider);
                Undo.DestroyObjectImmediate(m.gameObject);
                removed++;
            }

            return removed == 0
                ? ToolResult.Failure(NovaLocale.T("tool.noMatchingAsset", match, role))
                : ToolResult.Success(NovaLocale.T("tool.removedFromScene", removed),
                    new Dictionary<string, object> { { "removed", removed } });
        }
    }

    /// <summary>Araziyi yeniden üretir — "araziyi çöle çevir", "daha büyük ve tepelik yap".</summary>
    public class BuildTerrainTool : ITool
    {
        public string Name => "BuildTerrain";

        private static readonly HashSet<string> Biomes =
            new HashSet<string> { "plains", "forest", "valley", "hills", "coast", "desert", "snow", "swamp", "canyon", "volcanic" };

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string biome = SceneControlHelpers.Str(args, "biome", "plains").ToLowerInvariant();
            if (!Biomes.Contains(biome))
                return ToolResult.Failure(NovaLocale.T("tool.unknownBiome", biome, string.Join(", ", Biomes)));

            var plan = new TerrainPlan
            {
                biome = biome,
                size = Mathf.Clamp((int)SceneControlHelpers.Num(args, "size", 400f), 100, 2000),
                density = Mathf.Clamp01(SceneControlHelpers.Num(args, "density", 0.6f)),
                river = SceneControlHelpers.Flag(args, "river", biome == "valley"),
                lake = SceneControlHelpers.Flag(args, "lake", false),
                trees = SceneControlHelpers.Flag(args, "trees", biome != "desert"),
                rocks = SceneControlHelpers.Flag(args, "rocks", true),
                bushes = SceneControlHelpers.Flag(args, "bushes", biome != "desert"),
            };

            int seed = new System.Random().Next();
            TerrainGen.Build(plan, seed, msg => Debug.Log("[Nova Arazi] " + msg));

            return ToolResult.Success(
                NovaLocale.T("tool.terrainBuilding", TerrainGen.BiomeName(biome), plan.size, plan.density, plan.river, plan.lake),
                new Dictionary<string, object> { { "biome", biome }, { "size", plan.size }, { "seed", seed } });
        }
    }

    /// <summary>
    /// E1: Doğal dilden bölge dekorasyonu — "buraya kamp alanı kur", "bu bölgeyi süsle".
    /// Plan backend beyninden gelir, assetler küratörle seçilir, NovaDecorator yerleştirir.
    /// </summary>
    public class DecorateAreaTool : ITool
    {
        public string Name => "DecorateArea";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string prompt = SceneControlHelpers.Str(args, "prompt", "").Trim();
            if (string.IsNullOrEmpty(prompt))
                return ToolResult.Failure(NovaLocale.T("tool.decorPromptRequired"));
            float radius = Mathf.Clamp(SceneControlHelpers.Num(args, "radius", 15f), 4f, 60f);

            NovaDecorator.ApplySmart(prompt, radius, msg => Debug.Log("[Nova Dekor] " + msg));

            return ToolResult.Success(
                NovaLocale.T("tool.decorStarted", prompt, radius),
                new Dictionary<string, object> { { "prompt", prompt }, { "radius", radius } });
        }
    }

    /// <summary>Sahne sağlık taraması — rapor döner, istenirse onarır.</summary>
    public class ScanSceneTool : ITool
    {
        public string Name => "ScanScene";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            bool repair = SceneControlHelpers.Flag(args, "repair", false);
            string report = SceneHealth.ScanAndReport(offerFix: repair);
            return ToolResult.Success(report);
        }
    }

    /// <summary>v3: Dekor düzenleme — kaldır / çeşitle / seçili parçayı değiştir.</summary>
    public class EditDecorTool : ITool
    {
        public string Name => "EditDecor";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string action = SceneControlHelpers.Str(args, "action", "").Trim().ToLowerInvariant();
            string scope = SceneControlHelpers.Str(args, "scope", "near").Trim().ToLowerInvariant();
            string result = "Dekor düzenleme başlatıldı.";
            void Log(string m) { result = m; Debug.Log("[Nova Dekor] " + m); }

            switch (action)
            {
                case "clear": NovaDecorator.ClearDecor(scope != "all", Log); break;
                case "vary": NovaDecorator.ReDecorate(Log); break;
                case "replace": NovaDecorator.ReplaceSelected(Log); break;
                default: return ToolResult.Failure("action: 'clear' | 'vary' | 'replace' olmalı.");
            }
            return ToolResult.Success(result);
        }
    }

    /// <summary>Oyun şablonu kurar: FPS arena (dalga savunması) veya 3D platformer.</summary>
    public class BuildGameTemplateTool : ITool
    {
        public string Name => "BuildGameTemplate";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string type = SceneControlHelpers.Str(args, "type", "").Trim().ToLowerInvariant();
            bool play = SceneControlHelpers.Flag(args, "play", false);
            string result = "Şablon kuruluyor...";
            void Log(string m) { result = m; Debug.Log("[Nova Şablon] " + m); }

            switch (type)
            {
                case "arena":
                    ArenaBuilder.Build(Log, enterPlay: play);
                    return ToolResult.Success("FPS Arena kuruluyor — WASD hareket, sol tık ateş, R yeniden başla. "
                                            + "Her dalgada düşman sayısı ve hızı artar.");
                case "platformer":
                    PlatformerBuilder.Build(Log, enterPlay: play);
                    return ToolResult.Success("3D Platformer kuruluyor — WASD hareket, Space zıpla, coin topla. "
                                            + "Düşersen son platformdan devam edersin.");
                case "racer":
                case "racing":
                case "drift":
                    RacerBuilder.Build(Log, enterPlay: play);
                    return ToolResult.Success("Yarış pisti kuruluyor — WASD sür, Space el freni (drift), R piste dön. "
                                            + "Tur süresi ve en iyi turun ölçülür.");
                case "towerdefense":
                case "tower":
                case "td":
                    TowerDefenseBuilder.Build(Log, enterPlay: play);
                    return ToolResult.Success("Kule savunma kuruluyor — yol KENARINA sol tıkla kule kur (50 altın), "
                                            + "düşman vurdukça altın kazan, üssü koru. R yeniden başlatır.");
                default:
                    return ToolResult.Failure("type: 'arena' | 'platformer' | 'racer' | 'towerdefense' olmalı "
                                            + "(sonsuz koşu için BuildRunner).");
            }
        }
    }

    /// <summary>3D Sonsuz Koşu oyunu şablonu kurar (Subway Surfers tarzı).</summary>
    public class BuildRunnerTool : ITool
    {
        public string Name => "BuildRunner";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            bool play = SceneControlHelpers.Flag(args, "play", false);
            string result = "Sonsuz koşu kuruluyor...";
            RunnerBuilder.Build(msg => { result = msg; Debug.Log("[Nova Koşu] " + msg); }, enterPlay: play);
            return ToolResult.Success(
                result + " (Play'e bas — A/D şerit değiştir, Space zıpla, R yeniden başla.)",
                new Dictionary<string, object> { { "play", play } });
        }
    }

    /// <summary>
    /// T7: Oyuna hazırlık — NavMesh bake + oyuncu spawn + üstten minimap.
    /// </summary>
    public class PrepareForPlayTool : ITool
    {
        public string Name => "PrepareForPlay";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            bool nav = SceneControlHelpers.Flag(args, "navmesh", true);
            bool spawn = SceneControlHelpers.Flag(args, "spawn", true);
            bool minimap = SceneControlHelpers.Flag(args, "minimap", true);
            string result = "Oyuna hazırlık başlatıldı.";
            WorldPrep.PrepareForPlay(nav, spawn, minimap, msg => { result = msg; Debug.Log("[Nova Hazırlık] " + msg); });
            return ToolResult.Success(result);
        }
    }

    /// <summary>
    /// A9: URP göç asistanı — Standard/pembe materyalleri tarar, istenirse URP/Lit'e çevirir.
    /// "URP'ye geç", "materyaller pembe/bozuk", "shaderları URP yap" isteklerinde kullanılır.
    /// </summary>
    public class MigrateToUrpTool : ITool
    {
        public string Name => "MigrateToURP";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            bool convert = SceneControlHelpers.Flag(args, "convert", false);
            if (!convert)
                return ToolResult.Success(UrpMigrator.ScanAndReport()); // önce rapor
            int n = UrpMigrator.Migrate(confirm: true);
            return ToolResult.Success(n > 0
                ? $"{n} materyal URP/Lit'e çevrildi (Ctrl+Z geri alır). Özel shader'lar için "
                  + "'Assets/.../X.shader dosyasını URP'ye çevir' diyerek kod ajanını kullan."
                : "Çevrilecek Standard materyal bulunamadı ya da işlem iptal edildi.");
        }
    }
}
