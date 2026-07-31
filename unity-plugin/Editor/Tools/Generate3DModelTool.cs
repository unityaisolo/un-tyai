using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    /// <summary>
    /// 3D model üretir (backend + fal) ve sahneye yerleştirir. Üretim asenkron olduğundan
    /// aracı hemen "başlatıldı" olarak döner; model hazır olunca sahneye eklenir.
    /// </summary>
    public class Generate3DModelTool : ITool
    {
        public string Name => "Generate3DModel";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string prompt = args.TryGetValue("prompt", out var p) ? p?.ToString() : null;
            if (string.IsNullOrEmpty(prompt)) return ToolResult.Failure(NovaLocale.T("tool.promptRequired"));
            string imageUrl = args.TryGetValue("imageUrl", out var iu) ? iu?.ToString() : null;
            string name = args.TryGetValue("name", out var n) ? n?.ToString() : "GeneratedModel";
            UnityToolUtil.TryVec3(args, "position", out var pos);

            ModelGenerator.GenerateAndPlace(
                UnityAI.UnityAIConfig.BaseUrl, UnityAI.UnityAIConfig.ApiToken, prompt, imageUrl, name, pos,
                msg => Debug.Log("[UnityAI] " + msg));

            return ToolResult.Success(NovaLocale.T("tool.gen3dStarted", prompt));
        }
    }
}
