using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal class USSParser
    {
        readonly InlineUSSProcessor m_Processor = new();

        public void ApplyStyleSheetsFromContent(VisualElement container, string[] ussContents)
        {
            if (ussContents == null) return;

            foreach (var ussContent in ussContents)
            {
                if (!string.IsNullOrEmpty(ussContent))
                {
                    try
                    {
                        m_Processor.ApplyStylesToElement(container, ussContent);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to apply USS from content: {ex.Message}");
                    }
                }
            }
        }
    }
}
