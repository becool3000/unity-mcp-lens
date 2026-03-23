using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Renders special content for permission requests.
    /// </summary>
    interface IPermissionContentRenderer
    {
        /// <summary>
        /// Returns true if this renderer can handle the given rawInput.
        /// </summary>
        bool CanRender(JObject rawInput);

        /// <summary>
        /// Creates visual elements for the content.
        /// </summary>
        VisualElement Render(JObject rawInput, AssistantUIContext context);
    }
}
