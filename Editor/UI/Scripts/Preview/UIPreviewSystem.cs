using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal class UIPreviewSystem
    {
        const float k_DefaultAspectRatio = 16f / 9f;

        readonly USSParser m_USSParser;
        readonly UXMLParser m_Parser;

        public UIPreviewSystem()
        {
            m_USSParser = new USSParser();
            m_Parser = new UXMLParser();
        }


        public UIPreviewContainer CreatePreviewFromMemory(string uxmlContent, string[] ussContents = null, Vector2Int? resolution = null)
        {
            if (string.IsNullOrEmpty(uxmlContent))
            {
                Debug.LogError("UXML content is null or empty");
                return null;
            }

            try
            {
                var container = CreateContainer(resolution);

                var content = m_Parser.ParseFromString(uxmlContent);
                if (content != null)
                {
                    content.style.flexGrow = 1;
                    container.Add(content);
                }
                else
                {
                    Debug.LogWarning("UXML parsing returned null - check UXML content validity");
                }

                if (ussContents != null && ussContents.Length > 0)
                {
                    m_USSParser.ApplyStyleSheetsFromContent(container, ussContents);
                }

                return container;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create UXML preview from memory: {ex.Message}");
                return CreateErrorPreview(ex.Message, resolution);
            }
        }

        UIPreviewContainer CreateErrorPreview(string errorMessage, Vector2Int? resolution = null)
        {
            try
            {
                var container = CreateContainer(resolution);

                var errorElement = new VisualElement();
                errorElement.AddToClassList("preview-error");

                var errorLabel = new Label($"Preview Error: {errorMessage}");
                errorLabel.style.color = Color.red;
                errorLabel.style.fontSize = 16;
                errorLabel.style.whiteSpace = WhiteSpace.Normal;
                errorLabel.style.marginTop = 20;
                errorLabel.style.marginRight = 20;
                errorLabel.style.marginBottom = 20;
                errorLabel.style.marginLeft = 20;

                errorElement.Add(errorLabel);
                container.Add(errorElement);

                return container;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create error preview: {ex.Message}");
                return null;
            }
        }


        UIPreviewContainer CreateContainer(Vector2Int? resolution)
        {
            var container = new UIPreviewContainer();

            if (resolution.HasValue)
            {
                container.TargetResolution = resolution.Value;
            }
            else
            {
                container.style.flexGrow = 1;
                container.AspectRatio = k_DefaultAspectRatio;
            }

            return container;
        }

    }
}
