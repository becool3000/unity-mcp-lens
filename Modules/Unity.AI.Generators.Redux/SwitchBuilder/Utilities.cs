using System;
using System.Reflection;

namespace Unity.AI.Generators.Redux
{
    static class Utilities
    {
        public static string DefaultName(MethodInfo method)
        {
            if (method.Name.StartsWith("<"))
                throw new ArgumentException("Lambda expressions are not supported. Please use a named method instead.");

            return method.Name;
        }
    }
}
