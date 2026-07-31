using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityAI.Tools
{
    public class AddComponentTool : ITool
    {
        public string Name => "AddComponent";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            string typeName = args.TryGetValue("componentType", out var c) ? c?.ToString() : null;
            var go = UnityToolUtil.FindByPath(path);
            if (go == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundObject", path));
            var type = ComponentReflection.ResolveType(typeName);
            if (type == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundComponentType", typeName));
            var comp = Undo.AddComponent(go, type);
            if (comp == null) return ToolResult.Failure(NovaLocale.T("tool.addComponentFailed", typeName));
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success(NovaLocale.T("tool.componentAdded", typeName, path));
        }
    }

    public class SetComponentPropertyTool : ITool
    {
        public string Name => "SetComponentProperty";
        public ToolResult Execute(Dictionary<string, object> args)
        {
            string path = args.TryGetValue("path", out var p) ? p?.ToString() : null;
            string typeName = args.TryGetValue("componentType", out var c) ? c?.ToString() : null;
            string prop = args.TryGetValue("property", out var pr) ? pr?.ToString() : null;
            args.TryGetValue("value", out var value);

            var go = UnityToolUtil.FindByPath(path);
            if (go == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundObject", path));
            var type = ComponentReflection.ResolveType(typeName);
            if (type == null) return ToolResult.Failure(NovaLocale.T("tool.notFoundComponentType", typeName));
            var comp = go.GetComponent(type);
            if (comp == null) return ToolResult.Failure(NovaLocale.T("tool.componentMissing", typeName));

            Undo.RecordObject(comp, "UnityAI: SetComponentProperty");
            if (!ComponentReflection.SetMember(comp, prop, value, out string err))
                return ToolResult.Failure(err);
            UnityToolUtil.MarkSceneDirty();
            return ToolResult.Success($"{typeName}.{prop} = {value}");
        }
    }

    internal static class ComponentReflection
    {
        public static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = Type.GetType(name)
                    ?? Type.GetType("UnityEngine." + name + ", UnityEngine")
                    ?? Type.GetType("UnityEngine." + name + ", UnityEngine.CoreModule");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name) ?? asm.GetType("UnityEngine." + name);
                if (t != null && typeof(Component).IsAssignableFrom(t)) return t;
                foreach (var candidate in asm.GetTypes())
                    if (candidate.Name == name && typeof(Component).IsAssignableFrom(candidate))
                        return candidate;
            }
            return null;
        }

        public static bool SetMember(object target, string member, object value, out string err)
        {
            err = null;
            var type = target.GetType();
            var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                try { prop.SetValue(target, Convert(prop.PropertyType, value)); return true; }
                catch (Exception e) { err = e.Message; return false; }
            }
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                try { field.SetValue(target, Convert(field.FieldType, value)); return true; }
                catch (Exception e) { err = e.Message; return false; }
            }
            err = NovaLocale.T("tool.memberNotFound", member);
            return false;
        }

        private static object Convert(Type target, object value)
        {
            if (target == typeof(float)) return UnityToolUtil.ToFloat(value);
            if (target == typeof(int)) return (int)UnityToolUtil.ToFloat(value);
            if (target == typeof(bool)) return value is bool b ? b : bool.Parse(value.ToString());
            if (target == typeof(string)) return value?.ToString();
            if (target == typeof(Vector3) && value is IList<object> l && l.Count == 3)
                return new Vector3(UnityToolUtil.ToFloat(l[0]), UnityToolUtil.ToFloat(l[1]), UnityToolUtil.ToFloat(l[2]));
            return System.Convert.ChangeType(value, target);
        }
    }
}
