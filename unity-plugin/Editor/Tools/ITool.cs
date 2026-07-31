using System.Collections.Generic;

namespace UnityAI.Tools
{
    /// <summary>
    /// Bir Unity aksiyonu. Backend'in tanıdığı isimle eşleşir.
    /// Çalıştırma her zaman Editor main thread'inde yapılır (bkz. ToolRegistry).
    /// </summary>
    public interface ITool
    {
        string Name { get; }

        /// <summary>Args sözlüğünü alır, sonucu döner. İstisna fırlatabilir; ToolRegistry yakalar.</summary>
        ToolResult Execute(Dictionary<string, object> args);
    }

    public struct ToolResult
    {
        public bool Ok;
        public string Message;
        public Dictionary<string, object> Data;

        public static ToolResult Success(string message, Dictionary<string, object> data = null)
            => new ToolResult { Ok = true, Message = message, Data = data ?? new Dictionary<string, object>() };

        public static ToolResult Failure(string message)
            => new ToolResult { Ok = false, Message = message, Data = new Dictionary<string, object>() };
    }
}
