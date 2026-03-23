using System;
using System.Reflection;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class TypeDef<T>
    {
        public static readonly Type Value = typeof(T);
        public static readonly string Name = Value.Name;
        public static Assembly Assembly => Value.Assembly;
    }
}
