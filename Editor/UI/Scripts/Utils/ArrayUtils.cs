using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class ArrayUtils
    {
        public static bool ArrayEquals<T>(T[] a1, T[] a2)
            where T: IEquatable<T>
        {
            if (a1 == null && a2 == null)
            {
                return true;
            }

            if (a1 != null && a2 != null)
            {
                if (a1.Length != a2.Length)
                {
                    return false;
                }

                for (var i = 0; i < a1.Length; i++)
                {
                    if (!a1[i].Equals(a2[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }
    }
}
