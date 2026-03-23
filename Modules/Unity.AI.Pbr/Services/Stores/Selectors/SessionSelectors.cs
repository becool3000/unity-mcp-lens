using System;
using Unity.AI.Generators.Contexts;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static Session SelectSession(this IState state) => state.Get<Session>(SessionActions.slice);
        public static float SelectPreviewSizeFactor(this IState state, VisualElement element) => element.GetContext<PreviewScaleFactor>()?.value ?? state.SelectSession().settings.previewSettings.sizeFactor;
    }
}
