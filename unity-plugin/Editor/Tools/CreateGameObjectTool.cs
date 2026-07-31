using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    /// <summary>
    /// MVP aracı: sahnede GameObject oluşturur. Undo desteklidir (geri alınabilir).
    /// </summary>
    public class CreateGameObjectTool : ITool
    {
        public string Name => "CreateGameObject";

        public ToolResult Execute(Dictionary<string, object> args)
        {
            string name = args.TryGetValue("name", out var n) ? n?.ToString() : "GameObject";
            string primitive = args.TryGetValue("primitive", out var p) ? p?.ToString() : "None";

            GameObject go;
            if (!string.IsNullOrEmpty(primitive) && primitive != "None"
                && System.Enum.TryParse(primitive, out PrimitiveType pt))
            {
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            if (args.TryGetValue("position", out var posObj) && posObj is IList<object> pos && pos.Count == 3)
            {
                go.transform.position = new Vector3(
                    ToFloat(pos[0]), ToFloat(pos[1]), ToFloat(pos[2]));
            }

            Undo.RegisterCreatedObjectUndo(go, "UnityAI: Create " + name);
            Selection.activeGameObject = go;

            return ToolResult.Success(NovaLocale.T("tool.objectCreated", name), new Dictionary<string, object>
            {
                { "path", go.name },
            });
        }

        private static float ToFloat(object o)
            => o is double d ? (float)d : o is long l ? l : float.TryParse(o?.ToString(), out var f) ? f : 0f;
    }
}
