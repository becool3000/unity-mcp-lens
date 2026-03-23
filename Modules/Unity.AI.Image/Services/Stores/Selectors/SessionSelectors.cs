using Unity.AI.Generators.Contexts;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using UnityEngine.UIElements;
using Session = Unity.AI.Image.Services.Stores.States.Session;

namespace Unity.AI.Image.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static Session SelectSession(this IState state) => state.Get<Session>(SessionActions.slice) ?? new();
        public static float SelectPreviewSizeFactor(this IState state, VisualElement element) => element.GetContext<PreviewScaleFactor>()?.value ?? state.SelectSession().settings.previewSettings.sizeFactor;
    }
}
