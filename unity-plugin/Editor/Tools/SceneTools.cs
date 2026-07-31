using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    public class DeleteGameObjectTool : ITool
    {
        public string Name => "DeleteGameObject";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            var go = UnityToolUtil.FindByPath(path);
            if (go == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundObject", path));
            Undo.DestroyObjectImmediate(go);
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success(NovaLocale.T("tool.objectDeleted", path));
        }
    }

    public class SetTransformTool : ITool
    {
        public string Name => "SetTransform";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            var go = UnityToolUtil.FindByPath(path);
            if (go == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundObject", path));
            Undo.RecordObject(go.transform, "UnityAI: SetTransform");
            if (UnityToolUtil.TryVec3(args, "position", out var pos)) go.transform.position = pos;
            if (UnityToolUtil.TryVec3(args, "rotation", out var rot)) go.transform.eulerAngles = rot;
            if (UnityToolUtil.TryVec3(args, "scale", out var scl)) go.transform.localScale = scl;
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success(NovaLocale.T("tool.transformUpdated", path));
        }
    }

    public class CreatePrimitiveTool : ITool
    {
        public string Name => "CreatePrimitive";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string prim = args.TryGetValue("primitive", out var pr) ? pr?.ToString() : "Cube";
            string name = args.TryGetValue("name", out var n) ? n?.ToString() : prim;
            if (!System.Enum.TryParse(prim, out PrimitiveType pt))
                return ToolResult.Failure(NovaLocale.T("tool.invalidPrimitive", prim));
            var go = GameObject.CreatePrimitive(pt);
            go.name = name;
            if (UnityToolUtil.TryVec3(args, "position", out var pos)) go.transform.position = pos;
            Undo.RegisterCreatedObjectUndo(go, "UnityAI: CreatePrimitive");
            Selection.activeGameObject = go;
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success(NovaLocale.T("tool.primitiveCreated", name, prim),
                new Dictionary<string, object> { { "path", go.name } });
        }
    }

    public class InstantiatePrefabTool : ITool
    {
        public string Name => "InstantiatePrefab";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string prefabPath = args.TryGetValue("prefabPath", out var pp) ? pp?.ToString() : null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return ToolResult.Failure(NovaLocale.T("tool.prefabNotFound", prefabPath));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (UnityToolUtil.TryVec3(args, "position", out var pos)) go.transform.position = pos;
            Undo.RegisterCreatedObjectUndo(go, "UnityAI: InstantiatePrefab");
            Selection.activeGameObject = go;
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success(NovaLocale.T("tool.prefabInstantiated", prefabPath),
                new Dictionary<string, object> { { "path", go.name } });
        }
    }

    public class ReadSceneHierarchyTool : ITool
    {
        public string Name => "ReadSceneHierarchy";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            int maxDepth = args.TryGetValue("maxDepth", out var md) && md != null
                ? (int)UnityToolUtil.ToFloat(md) : int.MaxValue;
            var sb = new StringBuilder();
            foreach (var root in UnityToolUtil.GetRootObjects())
                Append(sb, root.transform, 0, maxDepth);
            string tree = sb.Length == 0 ? NovaLocale.T("tool.sceneEmpty") : sb.ToString();
            return ToolResult.Success(tree, new Dictionary<string, object> { { "hierarchy", tree } });
        }

        private static void Append(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            sb.Append(' ', depth * 2).Append("- ").Append(t.name).Append('\n');
            for (int i = 0; i < t.childCount; i++) Append(sb, t.GetChild(i), depth + 1, maxDepth);
        }
    }
}
