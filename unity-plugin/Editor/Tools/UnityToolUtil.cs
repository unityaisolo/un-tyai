using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI.Tools
{
    /// <summary>Araçların paylaştığı yardımcılar: yol çözümleme, değer dönüşümü.</summary>
    public static class UnityToolUtil
    {
        /// <summary>"Parent/Child/Leaf" biçiminde bir yolu sahnede çözer.</summary>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var direct = GameObject.Find(path);
            if (direct != null) return direct;

            string[] segs = path.Split('/');
            foreach (var root in GetRootObjects())
            {
                if (root.name != segs[0]) continue;
                Transform cur = root.transform;
                bool ok = true;
                for (int i = 1; i < segs.Length && ok; i++)
                {
                    var next = cur.Find(segs[i]);
                    if (next == null) { ok = false; break; }
                    cur = next;
                }
                if (ok) return cur.gameObject;
            }
            return null;
        }

        public static IEnumerable<GameObject> GetRootObjects()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects()) yield return go;
        }

        public static string GetPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        public static float ToFloat(object o)
            => o is double d ? (float)d : o is long l ? l : float.TryParse(o?.ToString(), out var f) ? f : 0f;

        public static bool TryVec3(Dictionary<string, object> args, string key, out Vector3 v)
        {
            v = Vector3.zero;
            if (!args.TryGetValue(key, out var raw) || raw == null) return false;

            // Gerçek dizi: [x, y, z]
            if (raw is IList<object> list && list.Count == 3)
            {
                v = new Vector3(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]));
                return true;
            }

            // Bazı (özellikle küçük) modeller diziyi string olarak döndürür: "[0, 0, 0]" veya "0,0,0"
            if (raw is string str)
            {
                var parts = str.Trim().TrimStart('[', '(').TrimEnd(']', ')').Split(',');
                if (parts.Length == 3)
                {
                    v = new Vector3(ToFloat(parts[0].Trim()), ToFloat(parts[1].Trim()), ToFloat(parts[2].Trim()));
                    return true;
                }
            }
            return false;
        }

        /// <summary>Sahne değişikliğini kaydeder (dirty), böylece Unity kaydetmeyi hatırlar.</summary>
        public static void MarkSceneDirty()
            => EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}
