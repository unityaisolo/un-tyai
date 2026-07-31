using System;
using System.Collections.Generic;

namespace UnityAI.Tools
{
    /// <summary>
    /// Kayıtlı araçları tutar ve isimle çalıştırır.
    /// Yeni araç eklemek için: Register(new MyTool()).
    /// </summary>
    public static class ToolRegistry
    {
        private static readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>();

        // Yıkıcı aksiyonlar: auto-approve kapalıyken kullanıcı onayı ister.
        private static readonly HashSet<string> _destructive = new HashSet<string>
        {
            "DeleteGameObject", "Generate3DModel", "RemovePlacedAssets", "BuildTerrain",
        };

        static ToolRegistry()
        {
            Register(new CreateGameObjectTool());
            Register(new CreatePrimitiveTool());
            Register(new DeleteGameObjectTool());
            Register(new SetTransformTool());
            Register(new AddComponentTool());
            Register(new SetComponentPropertyTool());
            Register(new InstantiatePrefabTool());
            Register(new ReadSceneHierarchyTool());
            Register(new ReadConsoleLogsTool());
            Register(new ReadScriptTool());
            Register(new WriteScriptTool());
            Register(new Generate3DModelTool());
            // Sahne / arazi / asset yönetimi — Kod Ajanı'ndan cümleyle kullanılabilir
            Register(new ListPlacedAssetsTool());
            Register(new RemovePlacedAssetsTool());
            Register(new BuildTerrainTool());
            Register(new ScanSceneTool());
            Register(new DecorateAreaTool()); // E1: doğal dilden bölge dekorasyonu
            Register(new MigrateToUrpTool()); // A9: URP göç asistanı
            Register(new PrepareForPlayTool()); // T7: oyuna hazırlık
            Register(new EditDecorTool()); // v3: dekor düzenleme
            Register(new BuildRunnerTool()); // 3D sonsuz koşu şablonu
            Register(new BuildGameTemplateTool()); // FPS arena + 3D platformer şablonları
        }

        public static void Register(ITool tool) => _tools[tool.Name] = tool;

        public static IEnumerable<string> Names => _tools.Keys;

        public static bool IsDestructive(string name) => _destructive.Contains(name);

        public static ToolResult Execute(string name, Dictionary<string, object> args)
        {
            if (!_tools.TryGetValue(name, out var tool))
                return ToolResult.Failure(NovaLocale.T("tool.unknownTool", name));
            try
            {
                return tool.Execute(args ?? new Dictionary<string, object>());
            }
            catch (Exception e)
            {
                return ToolResult.Failure(NovaLocale.T("tool.threwError", name, e.Message));
            }
        }
    }
}
