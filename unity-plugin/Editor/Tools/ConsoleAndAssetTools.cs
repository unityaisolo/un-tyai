using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    /// <summary>
    /// Unity konsol girdilerini okur (LogEntries internal API'si üzerinden).
    /// Değişiklik yapmaz; agent'a bağlam sağlar.
    /// </summary>
    public class ReadConsoleLogsTool : ITool
    {
        public string Name => "ReadConsoleLogs";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            int limit = args.TryGetValue("limit", out var l) && l != null
                ? (int)UnityToolUtil.ToFloat(l) : 50;
            HashSet<string> filter = null;
            if (args.TryGetValue("types", out var t) && t is IList<object> list)
                filter = new HashSet<string>(list.Select(x => x?.ToString()));

            var entries = ReadViaReflection(limit, filter, out string err);
            if (entries == null) return ToolResult.Failure(err);
            string joined = entries.Count == 0 ? NovaLocale.T("tool.consoleEmpty") : string.Join("\n", entries);
            return ToolResult.Success(joined,
                new Dictionary<string, object> { { "count", entries.Count }, { "logs", joined } });
        }

        private static List<string> ReadViaReflection(int limit, HashSet<string> filter, out string err)
        {
            err = null;
            try
            {
                var logEntries = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                var logEntry = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                if (logEntries == null || logEntry == null) { err = NovaLocale.T("tool.logEntriesMissing"); return null; }

                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                int count = (int)logEntries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
                var getEntry = logEntries.GetMethod("GetEntryInternal", flags);
                var msgField = logEntry.GetField("message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var modeField = logEntry.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var result = new List<string>();
                int start = Math.Max(0, count - limit);
                for (int i = start; i < count; i++)
                {
                    var entry = Activator.CreateInstance(logEntry);
                    getEntry.Invoke(null, new object[] { i, entry });
                    string msg = msgField?.GetValue(entry)?.ToString() ?? "";
                    int mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                    string kind = ClassifyMode(mode);
                    if (filter != null && !filter.Contains(kind)) continue;
                    result.Add($"[{kind}] {msg.Split('\n')[0]}");
                }
                logEntries.GetMethod("EndGettingEntries", flags).Invoke(null, null);
                return result;
            }
            catch (Exception e) { err = NovaLocale.T("tool.consoleReadError", e.Message); return null; }
        }

        // Unity mode bit maskesinden kaba sınıflandırma
        private static string ClassifyMode(int mode)
        {
            const int error = 1 << 0 | 1 << 1 | 1 << 4 | 1 << 5 | 1 << 6 | 1 << 7 | 1 << 9;
            const int warning = 1 << 8;
            if ((mode & error) != 0) return "Error";
            if ((mode & warning) != 0) return "Warning";
            return "Log";
        }
    }

    /// <summary>Bir C# script değişikliği önerir (Kod sekmesinde diff onayı). Doğrudan yazmaz.</summary>
    public class WriteScriptTool : ITool
    {
        public string Name => "WriteScript";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            string content = args.TryGetValue("content", out var c) ? c?.ToString() : null;
            if (string.IsNullOrEmpty(path) || content == null)
                return ToolResult.Failure(NovaLocale.T("tool.pathContentRequired"));
            if (!path.StartsWith("Assets/"))
                return ToolResult.Failure(NovaLocale.T("tool.pathMustStartAssets"));

            string id = CodeEdits.Propose(path, content);
            if (id == null)
                return ToolResult.Failure(NovaLocale.T("tool.securityOutsideAssetsWrite", path));
            return ToolResult.Success(NovaLocale.T("tool.changeProposed", path));
        }
    }
}
