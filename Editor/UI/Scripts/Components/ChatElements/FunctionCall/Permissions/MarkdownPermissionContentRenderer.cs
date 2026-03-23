using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Renders markdown content from rawInput.plan field.
    /// </summary>
    class MarkdownPermissionContentRenderer : IPermissionContentRenderer
    {
        const string k_PlanField = "plan";

        public bool CanRender(JObject rawInput)
        {
            return rawInput?[k_PlanField] != null;
        }

        public VisualElement Render(JObject rawInput, AssistantUIContext context)
        {
            var markdown = rawInput[k_PlanField]?.ToString();
            if (string.IsNullOrEmpty(markdown))
                return null;

            var container = new ScrollView();
            container.AddToClassList("permission-content-container");
            container.style.maxHeight = 400;

            var elements = new List<VisualElement>();
            MarkdownAPI.MarkupText(context, markdown, null, elements, null);

            foreach (var element in elements)
            {
                container.Add(element);
            }

            return container;
        }
    }
}
