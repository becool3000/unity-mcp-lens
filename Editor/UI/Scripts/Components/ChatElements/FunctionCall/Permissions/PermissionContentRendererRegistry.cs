using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Selects the appropriate content renderer for a permission request.
    /// </summary>
    static class PermissionContentRendererRegistry
    {
        static readonly List<IPermissionContentRenderer> s_Renderers = new()
        {
            new MarkdownPermissionContentRenderer(),
            new DiffPermissionContentRenderer(),
        };

        public static IPermissionContentRenderer GetRenderer(JObject rawInput)
        {
            if (rawInput == null)
                return null;

            foreach (var renderer in s_Renderers)
            {
                if (renderer.CanRender(rawInput))
                    return renderer;
            }

            return null;
        }
    }
}
