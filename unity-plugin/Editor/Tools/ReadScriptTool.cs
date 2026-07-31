using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityAI.Tools
{
    /// <summary>Assets içindeki bir C# dosyasının içeriğini okur (düzenleme öncesi bağlam).</summary>
    public class ReadScriptTool : ITool
    {
        public string Name => "ReadScript";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            if (string.IsNullOrEmpty(path)) return ToolResult.Failure(NovaLocale.T("tool.pathRequired"));
            // GÜVENLİK: sadece Assets/ altı okunabilir ("../" ile proje dışına kaçış yok)
            var full = CodeEdits.SafeFullPath(path);
            if (full == null) return ToolResult.Failure(NovaLocale.T("tool.securityOutsideAssetsRead", path));
            if (!File.Exists(full)) return ToolResult.Failure(NovaLocale.T("tool.fileMissing", path));
            string content = File.ReadAllText(full);
            return ToolResult.Success(content, new Dictionary<string, object> { { "content", content } });
        }
    }
}
