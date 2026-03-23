using System;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;

namespace Unity.AI.Generators.UI.AIDropdownIntegrations
{
    static class AIDropdownIntegration
    {
        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMenuExtension(container => container.Add(new GenerativeMenuRoot()), 10);
    }
}