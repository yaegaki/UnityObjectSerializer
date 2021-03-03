﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace ScriptableObjectSerializer
{
    public interface IPatcher
    {
        void PatchTo(ref object obj, IObjectNode patch);
        IObjectNode PatchFrom(object obj, string name);
    }

    public static class Patcher
    {
        public static IPatcher Create<T>()
            => Create(typeof(T));

        public static IPatcher Create(Type type)
            => new ComplexPatcher(type);
    }

    class PrimitivePatcher : IPatcher
    {
        private readonly Type type;
        private readonly NodeType nodeType;

        public PrimitivePatcher(Type type, NodeType nodeType)
        {
            this.type = type;
            this.nodeType = nodeType;
        }

        public void PatchTo(ref object obj, IObjectNode patch)
        {
            // string is only nullable primitive type.
            if (obj == null && this.nodeType != NodeType.String) return;
            if (this.nodeType != patch.Type) return;

            if (patch.IsNull)
            {
                if (this.nodeType == NodeType.String)
                {
                    obj = null;
                }
                return;
            }

            obj = patch.Value;
        }

        public IObjectNode PatchFrom(object obj, string name)
        {
            if (obj == null)
            {
                if (this.nodeType == NodeType.String)
                {
                    return new PrimitiveObjectNode(NodeType.String, name, null);
                }

                return null;
            }

            if (obj.GetType().ToNodeType() != this.nodeType)
            {
                return null;
            }

            return new PrimitiveObjectNode(this.nodeType, name, obj);
        }
    }

    class ComplexPatcher : IPatcher
    {
        private readonly Type type;
        private readonly PatcherInfo[] childPatchers;

        readonly struct PatcherInfo
        {
            public bool IsValid => Patcher != null;
            public string Name => FieldInfo.Name;

            public readonly FieldInfo FieldInfo;
            public readonly IPatcher Patcher;

            public PatcherInfo(FieldInfo fieldInfo, IPatcher patcher)
                => (this.FieldInfo, this.Patcher) = (fieldInfo, patcher);

            public void SetValue(object parent, IObjectNode patch)
            {
                var instance = this.FieldInfo.GetValue(parent);
                this.Patcher.PatchTo(ref instance, patch);
                this.FieldInfo.SetValue(parent, instance);
            }
        }

        public ComplexPatcher(Type type)
        {
            this.type = type;
            this.childPatchers = ReflectionHelper.GetAllFields(type)
                .Where(SerializeHelper.IsSerializable)
                .Select(CreatePatcher)
                .Where(p => p.IsValid)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray();
        }

        private PatcherInfo CreatePatcher(FieldInfo fieldInfo)
        {
            var type = fieldInfo.FieldType;
            var nodeType = type.ToNodeType();
            if (type.IsArray || type.IsGenericType)
            {
                return new PatcherInfo(fieldInfo, new ListPatcher(type, nodeType));
            }

            if (nodeType == NodeType.Complex)
            {
                return new PatcherInfo(fieldInfo, new ComplexPatcher(fieldInfo.FieldType));
            }

            return new PatcherInfo(fieldInfo, new PrimitivePatcher(fieldInfo.FieldType, nodeType));
        }

        public void PatchTo(ref object obj, IObjectNode patch)
        {
            if (patch.Type != NodeType.Complex) return;
            if (patch.IsList) return;

            if (patch.IsNull)
            {
                obj = null;
                return;
            }

            if (obj == null)
            {
                obj = ReflectionHelper.CreateInstance(this.type);
            }

            var index = 0;
            foreach (var childNode in patch.Children)
            {
                var patcher = FindChildPatcher(ref index, childNode.Name);
                if (!patcher.IsValid) continue;
                patcher.SetValue(obj, childNode);
            }
        }

        public IObjectNode PatchFrom(object obj, string name)
        {
            if (obj == null)
            {
                return new ComplexObjectNode(name, true, null);
            }

            var childNodes = this.childPatchers
                .Select(p =>
                {
                    var v = p.FieldInfo.GetValue(obj);
                    return p.Patcher.PatchFrom(v, p.Name);
                })
                .Where(c => c != null);

            return new ComplexObjectNode(name, false, childNodes);
        }

        private PatcherInfo FindChildPatcher(ref int index, string name)
        {
            for (; index < this.childPatchers.Length; index++)
            {
                var p = this.childPatchers[index];
                var comp = string.CompareOrdinal(p.Name, name);
                if (comp > 0) break;
                if (comp == 0)
                {
                    return p;
                }
            }

            return default;
        }
    }

    class ListPatcher : IPatcher
    {
        private readonly Type type;
        private readonly NodeType nodeType;
        private readonly IPatcher childPatcher;

        public ListPatcher(Type type, NodeType nodeType)
        {
            this.type = type;
            this.nodeType = nodeType;
            this.childPatcher = CreatePatcher(type);
        }

        private IPatcher CreatePatcher(Type listType)
        {
            var elemType = listType.IsArray ? listType.GetElementType() : listType.GenericTypeArguments.First();
            var nodeType = elemType.ToNodeType();
            if (nodeType == NodeType.Complex)
            {
                return new ComplexPatcher(elemType);
            }

            return new PrimitivePatcher(elemType, nodeType);
        }

        public void PatchTo(ref object obj, IObjectNode patch)
        {
            if (patch.Type != this.nodeType) return;
            if (!patch.IsList) return;
            if (patch.ListCount < 0) return;

            if (obj == null)
            {
                obj = ReflectionHelper.CreateArrayOrList(this.type, patch.ListCount);
            }

            if (!(obj is IList list)) return;

            foreach (var childNode in patch.Children)
            {
                if (!(int.TryParse(childNode.Name, out var index))) continue;
                if (index < 0 || index >= list.Count) continue;

                var v = list[index];
                this.childPatcher.PatchTo(ref v, childNode);
                list[index] = v;
            }
        }

        public IObjectNode PatchFrom(object obj, string name)
        {
            if (obj == null)
            {
                return new ComplexObjectNode(this.nodeType, name, 0, true, null);
            }

            if (!(obj is IList list)) return null;

            var children = list
                .OfType<object>()
                .Select((c, i) => this.childPatcher.PatchFrom(c, i.ToString()))
                .Where(c => c != null);
            
            return new ComplexObjectNode(this.nodeType, name, list.Count, false, children);
        }
    }
}
