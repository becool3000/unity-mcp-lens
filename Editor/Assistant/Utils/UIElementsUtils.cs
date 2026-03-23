using Unity.AI.Assistant.Bridge.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class UIElementsUtils
    {
        /// <summary>
        /// Renders a UI panel using the specified PanelSettings, VisualTreeAsset, and optional StyleSheets.
        /// </summary>
        /// <param name="panelSettings"> The PanelSettings to use for rendering. </param>
        /// <param name="visualTreeAsset"> The VisualTreeAsset defining the UI structure. </param>
        /// <param name="styleSheets"> Optional StyleSheets to apply to the panel. Do not pass stylesheets already included in the VisualTreeAsset. </param>
        internal static void RenderPanel(
            PanelSettings panelSettings,
            VisualTreeAsset visualTreeAsset,
            params StyleSheet[] styleSheets)
        {
            try
            {
                GL.PushMatrix();

                var panel = UIPanelUtils.GetPanel(panelSettings);
                var root = UIPanelUtils.GetVisualTree(panel);

                visualTreeAsset.CloneTree(root);
                foreach (var styleSheet in styleSheets)
                {
                    root.styleSheets.Add(styleSheet);
                }

                root.StretchToParentSize();
                UIPanelUtils.RepaintPanel(panel);
                UIPanelUtils.RenderPanel(panel);
            }
            finally
            {
                GL.PopMatrix();
            }
        }
    }

    static class VisualTreeAssetUtils
    {
        /// <summary>
        /// Validates UXML content using the UxmlValidator.
        /// </summary>
        /// <param name="content"> The UXML content to validate. </param>
        /// <returns> The validation result as a string. </returns>
        internal static string ValidateUxml(string content)
        {
            return UxmlValidator.ValidateUxml(content);
        }

        /// <summary>
        /// Validates asset references in UXML content.
        /// </summary>
        /// <param name="uxmlContent"> The content of the UXML to validate. </param>
        /// <param name="fixedContent"> The UXML content with fixed asset references, if any. </param>
        /// <param name="hasReplacements"> Indicates whether any asset references were replaced. </param>
        /// <returns> The validation result as a string. </returns>
        internal static string ValidateAndFixAssetReferences(
            string uxmlContent,
            out string fixedContent,
            out bool hasReplacements)
        {
            return AssetReferenceValidator.ValidateAndFixUxmlAssetReferences(
                uxmlContent,
                out fixedContent,
                out hasReplacements);
        }
    }

    static class StyleSheetUtils
    {
        /// <summary>
        /// Validates USS content using the UssValidator.
        /// </summary>
        /// <param name="content"> The USS content to validate. </param>
        /// <returns> The validation result as a string. </returns>
        internal static string ValidateUss(string content)
        {
            return UssValidator.ValidateUss(content);
        }

        /// <summary>
        /// Validates asset references in USS/TSS content.
        /// </summary>
        /// <param name="ussContent"> The content of the USS/TSS to validate. </param>
        /// <param name="fixedContent"> The USS/TSS content with fixed asset references, if any. </param>
        /// <param name="hasReplacements"> Indicates whether any asset references were replaced. </param>
        /// <returns> The validation result as a string. </returns>
        internal static string ValidateAndFixAssetReferences(
            string ussContent,
            out string fixedContent,
            out bool hasReplacements)
        {
            return AssetReferenceValidator.ValidateAndFixUssAssetReferences(
                ussContent,
                out fixedContent,
                out hasReplacements);
        }
    }
}
