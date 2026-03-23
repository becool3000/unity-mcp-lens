using System;
using Unity.AI.Generators.Contexts;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    record WindowSettingsContext(bool replaceAssetOnSelect, bool disablePrecaching)
    {
        public bool replaceAssetOnSelect { get; } = replaceAssetOnSelect;
        public bool disablePrecaching { get; } = disablePrecaching;
    }

    namespace Selectors
    {
        static class Selectors
        {
            public static bool SelectWindowSettingsReplaceAssetOnSelect(this VisualElement element) => element.GetContext<WindowSettingsContext>()?.replaceAssetOnSelect ?? false;
            public static bool SelectWindowSettingsDisablePrecaching(this VisualElement element) => element.GetContext<WindowSettingsContext>()?.disablePrecaching ?? false;
        }
    }
}
