using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityAI.Lib;

namespace UnityAI
{
    /// <summary>
    /// UnityAI backend'ine bağlanan SSE istemcisi.
    /// Olaylar arka thread'de gelir; çağıran main thread'e marshal etmelidir.
    /// </summary>
    public class BackendClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private readonly string _baseUrl;
        private readonly string _token;

        public BackendClient(string baseUrl, string token)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _token = token;
        }

        public class ToolCall
        {
            public string Id;
            public string Name;
            public string ArgsJson; // ham JSON nesnesi (ör. {"name":"Cube"})
        }

        public class Message
        {
            public string Role;                 // system | user | assistant | tool
            public string Content;
            public string ToolCallId;           // tool rolü için
            public List<ToolCall> ToolCalls;    // assistant rolü için
            public List<string> Images;         // base64 PNG — kullanıcının eklediği görseller
        }

        /// <summary>
        /// /v1/chat akışını dinler. onEvent her SSE olayı için çağrılır (arka thread'de).
        /// </summary>
        public async Task StreamChatAsync(
            string model,
            IList<Message> messages,
            bool council,
            Action<Dictionary<string, object>> onEvent,
            CancellationToken ct)
        {
            string body = BuildRequestJson(model, messages, council);
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/chat");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_token))
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (!line.StartsWith("data:")) continue;
                string payload = line.Substring(5).Trim();
                if (payload.Length == 0) continue;
                if (Json.Deserialize(payload) is Dictionary<string, object> ev)
                    onEvent(ev);
            }
        }

        private static string BuildRequestJson(string model, IList<Message> messages, bool council)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":").Append(Quote(model)).Append(",\"messages\":[");
            for (int i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"role\":").Append(Quote(m.Role))
                  .Append(",\"content\":").Append(Quote(m.Content ?? ""));
                if (!string.IsNullOrEmpty(m.ToolCallId))
                    sb.Append(",\"toolCallId\":").Append(Quote(m.ToolCallId));
                if (m.Images != null && m.Images.Count > 0)
                {
                    sb.Append(",\"images\":[");
                    for (int j = 0; j < m.Images.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        sb.Append(Quote(m.Images[j]));
                    }
                    sb.Append(']');
                }
                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    sb.Append(",\"toolCalls\":[");
                    for (int j = 0; j < m.ToolCalls.Count; j++)
                    {
                        var tc = m.ToolCalls[j];
                        if (j > 0) sb.Append(',');
                        sb.Append("{\"id\":").Append(Quote(tc.Id))
                          .Append(",\"name\":").Append(Quote(tc.Name))
                          .Append(",\"args\":").Append(string.IsNullOrEmpty(tc.ArgsJson) ? "{}" : tc.ArgsJson)
                          .Append('}');
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append(']');
            if (council) sb.Append(",\"council\":true");
            sb.Append("}");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
