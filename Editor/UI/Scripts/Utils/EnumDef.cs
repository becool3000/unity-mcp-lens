using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Utils;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class EnumDef<T>
        where T : Enum, IConvertible
    {
        static Dictionary<T, string> s_EntryToName;

        static EnumDef()
        {
            Type = TypeDef<T>.Value;
            Values = (T[])Enum.GetValues(Type);
            Names = Enum.GetNames(Type);

            Count = Values.Length;

            s_EntryToName = new Dictionary<T, string>(Count);
            for (int i = 0; i < Count; ++i)
            {
                s_EntryToName[Values[i]] = Names[i];
            }
        }

        public static readonly Type Type;

        public static readonly int Count;

        public static readonly T[] Values;
        public static readonly string[] Names;

        public static string GetNameOf(T value)
        {
            return s_EntryToName[value];
        }

        public static T Parse(string value)
        {
            return (T)Enum.Parse(Type, value);
        }
    }
}
