using System;

namespace Unity.AI.Assistant.Editor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    internal class ToolPermissionIgnoreAttribute : Attribute {}
}
