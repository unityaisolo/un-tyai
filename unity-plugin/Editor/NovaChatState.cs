using System.Collections.Generic;
using UnityAI.Lib;
using UnityEditor;

namespace UnityAI
{
    /// <summary>
    /// SOHBET HAFIZASI — domain reload'a dayanıklı.
    ///
    /// Unity, script yazıldığında/derlendiğinde tüm C# alanlarını sıfırlar (domain reload).
    /// Editör penceresi yeniden kurulur ve sohbet uçardı: "kod yaz" dedin, kod yazıldı,
    /// Unity derledi, konuşma silindi. Burada geçmişi + ekrandaki balonları SessionState'e
    /// yazıyoruz; SessionState derlemeyi atlatır (Unity kapanınca temizlenir).
    /// </summary>
    public static class NovaChatState
    {
        private const string KeyHistory = "Nova.Chat.History";
        private const string KeyView = "Nova.Chat.View";
        private const string KeyCost = "Nova.Chat.Cost";
        private const string KeyBusy = "Nova.Chat.WasRunning";

        /// <summary>Domain reload sırasında bir tur yarıda kaldı mı?</summary>
        public static bool WasInterrupted
        {
            get => SessionState.GetBool(KeyBusy, false);
            set => SessionState.SetBool(KeyBusy, value);
        }

        public static double Cost
        {
            get => SessionState.GetFloat(KeyCost, 0f);
            set => SessionState.SetFloat(KeyCost, (float)value);
        }

        public static void Save(List<BackendClient.Message> history, List<Dictionary<string, object>> view)
        {
            var msgs = new List<object>();
            foreach (var m in history)
            {
                var d = new Dictionary<string, object> { { "role", m.Role }, { "content", m.Content ?? "" } };
                if (!string.IsNullOrEmpty(m.ToolCallId)) d["toolCallId"] = m.ToolCallId;
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    var calls = new List<object>();
                    foreach (var c in m.ToolCalls)
                        calls.Add(new Dictionary<string, object>
                        {
                            { "id", c.Id }, { "name", c.Name }, { "argsJson", c.ArgsJson },
                        });
                    d["toolCalls"] = calls;
                }
                msgs.Add(d);
            }
            SessionState.SetString(KeyHistory, Json.Serialize(new Dictionary<string, object> { { "m", msgs } }));
            SessionState.SetString(KeyView, Json.Serialize(new Dictionary<string, object> { { "v", new List<object>(view) } }));
        }

        public static List<BackendClient.Message> LoadHistory()
        {
            var res = new List<BackendClient.Message>();
            var raw = SessionState.GetString(KeyHistory, "");
            if (string.IsNullOrEmpty(raw)) return res;
            if (!(Json.Deserialize(raw) is Dictionary<string, object> root)) return res;
            if (!(root.TryGetValue("m", out var mv) && mv is List<object> list)) return res;

            foreach (var o in list)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                var msg = new BackendClient.Message
                {
                    Role = Str(d, "role"),
                    Content = Str(d, "content"),
                    ToolCallId = d.ContainsKey("toolCallId") ? Str(d, "toolCallId") : null,
                };
                if (d.TryGetValue("toolCalls", out var cv) && cv is List<object> calls && calls.Count > 0)
                {
                    msg.ToolCalls = new List<BackendClient.ToolCall>();
                    foreach (var c in calls)
                        if (c is Dictionary<string, object> cd)
                            msg.ToolCalls.Add(new BackendClient.ToolCall
                            {
                                Id = Str(cd, "id"), Name = Str(cd, "name"), ArgsJson = Str(cd, "argsJson"),
                            });
                }
                res.Add(msg);
            }
            return res;
        }

        /// <summary>Ekrandaki balonlar: her biri {sender, body}.</summary>
        public static List<Dictionary<string, object>> LoadView()
        {
            var res = new List<Dictionary<string, object>>();
            var raw = SessionState.GetString(KeyView, "");
            if (string.IsNullOrEmpty(raw)) return res;
            if (!(Json.Deserialize(raw) is Dictionary<string, object> root)) return res;
            if (!(root.TryGetValue("v", out var vv) && vv is List<object> list)) return res;
            foreach (var o in list)
                if (o is Dictionary<string, object> d) res.Add(d);
            return res;
        }

        public static void Clear()
        {
            SessionState.EraseString(KeyHistory);
            SessionState.EraseString(KeyView);
            SessionState.EraseFloat(KeyCost);
            SessionState.EraseBool(KeyBusy);
        }

        private static string Str(Dictionary<string, object> d, string k)
            => d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    }
}
