using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    public struct PendingEdit
    {
        public string Id, Path, OldText, NewText;
    }

    /// <summary>
    /// AI'nın önerdiği kod değişikliklerini "bekleyen" tutar. Kullanıcı Kod sekmesinde
    /// diff'i görüp Uygula/Reddet der. Uygula -> dosya yazılır (Cursor tarzı akış).
    /// </summary>
    public static class CodeEdits
    {
        private static readonly List<PendingEdit> _pending = new List<PendingEdit>();

        public static event Action Changed;        // liste her değiştiğinde
        public static event Action<string> Proposed; // yeni öneri (path) -> UI sekme değiştirebilir
        public static event Action<string> Applied;   // uygulandı (path)

        public static IReadOnlyList<PendingEdit> Pending => _pending;

        /// <summary>
        /// GÜVENLİK: Yolu proje köküne göre çözer ve SADECE Assets/ altında kalıyorsa
        /// tam yolu döner; aksi halde null ("../" ile proje dışına kaçış engellenir).
        /// </summary>
        public static string SafeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string full = Path.GetFullPath(Path.Combine(projectRoot, path));
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                                    + Path.DirectorySeparatorChar;
                return full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)
                    ? full : null;
            }
            catch { return null; }
        }

        /// <summary>Öneri ekler. Yol Assets/ dışına çıkıyorsa null döner (öneri reddedilir).</summary>
        public static string Propose(string path, string newText)
        {
            if (SafeFullPath(path) == null)
            {
                Debug.LogWarning($"[UnityAI] Güvenlik: '{path}' Assets/ dışına çıkıyor — öneri reddedildi.");
                return null;
            }
            var e = new PendingEdit
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Path = path,
                OldText = ReadExisting(path),
                NewText = newText ?? "",
            };
            _pending.Add(e);
            Proposed?.Invoke(path);
            Changed?.Invoke();
            return e.Id;
        }

        public static void Apply(string id)
        {
            int i = _pending.FindIndex(p => p.Id == id);
            if (i < 0) return;
            var e = _pending[i];
            try { Write(e.Path, e.NewText); }
            catch (Exception ex) { Debug.LogError("[UnityAI] Yazma hatası: " + ex.Message); return; }
            _pending.RemoveAt(i);
            Applied?.Invoke(e.Path);
            Changed?.Invoke();
        }

        public static void Reject(string id)
        {
            int i = _pending.FindIndex(p => p.Id == id);
            if (i < 0) return;
            _pending.RemoveAt(i);
            Changed?.Invoke();
        }

        private static string ReadExisting(string path)
        {
            try { var full = SafeFullPath(path); return full != null && File.Exists(full) ? File.ReadAllText(full) : ""; }
            catch { return ""; }
        }

        private static void Write(string path, string content)
        {
            var full = SafeFullPath(path)
                ?? throw new IOException($"Güvenlik: '{path}' Assets/ dışına çıkıyor — yazma engellendi.");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();
        }
    }
}
