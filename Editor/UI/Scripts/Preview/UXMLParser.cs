using System;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal class UXMLParser
    {
        public VisualElement Parse(string uxmlPath)
        {
            if (!File.Exists(uxmlPath))
            {
                Debug.LogError($"UXML file not found: {uxmlPath}");
                return null;
            }

            var xmlContent = File.ReadAllText(uxmlPath);
            return ParseFromString(xmlContent);
        }

        public VisualElement ParseFromString(string uxmlContent)
        {
            if (string.IsNullOrEmpty(uxmlContent))
            {
                Debug.LogError("UXML content is null or empty");
                return null;
            }

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(uxmlContent);

                var root = xmlDoc.DocumentElement;
                return root != null ? ParseElement(root) : null;
            }
            catch (XmlException ex)
            {
                Debug.LogError($"Failed to parse UXML: {ex.Message}");
                return CreateErrorElement($"UXML Parse Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected error parsing UXML: {ex.Message}");
                return CreateErrorElement($"Unexpected Error: {ex.Message}");
            }
        }

        static VisualElement CreateErrorElement(string errorMessage)
        {
            var errorContainer = new VisualElement();
            errorContainer.AddToClassList("uxml-parse-error");

            var errorLabel = new Label($"Error: {errorMessage}")
            {
                style =
                {
                    color = Color.red,
                    whiteSpace = WhiteSpace.Normal,
                    fontSize = 14,
                    marginTop = 10,
                    marginRight = 10,
                    marginBottom = 10,
                    marginLeft = 10
                }
            };

            errorContainer.Add(errorLabel);
            return errorContainer;
        }

        VisualElement ParseElement(XmlElement xmlElement)
        {
            var element = CreateElement(xmlElement);
            if (element == null) return null;

            ApplyAttributes(element, xmlElement);
            AddChildren(element, xmlElement);

            return element;
        }

        static VisualElement CreateElement(XmlElement xmlElement)
        {
            var tagName = xmlElement.LocalName;

            if (IsSpecialElement(tagName))
            {
                return HandleSpecialElement(tagName);
            }

            var elementType = ElementTypeRegistry.GetElementType(tagName);
            try
            {
                return (VisualElement)Activator.CreateInstance(elementType);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to create element {tagName}: {ex.Message}");
                return new VisualElement();
            }
        }

        static bool IsSpecialElement(string tagName)
        {
            return tagName.ToLower() switch
            {
                "uxml" or "template" or "instance" or "style" => true,
                _ => false
            };
        }

        static VisualElement HandleSpecialElement(string tagName)
        {
            return tagName.ToLower() switch
            {
                "style" => null,
                _ => new VisualElement()
            };
        }

        static void ApplyAttributes(VisualElement element, XmlElement xmlElement)
        {
            foreach (XmlAttribute attr in xmlElement.Attributes)
            {
                if (attr.Name.StartsWith(PreviewConstants.XmlnsPrefix) || attr.Name.StartsWith(PreviewConstants.XsiPrefix) ||
                    attr.Name == PreviewConstants.EngineAttribute || attr.Name == PreviewConstants.EditorAttribute ||
                    attr.Name == PreviewConstants.NoNamespaceSchemaLocation || attr.Name == PreviewConstants.EditorExtensionMode)
                {
                    continue;
                }

                ReflectionAttributeProcessor.ApplyAttribute(element, attr.Name, attr.Value);
            }
        }


        void AddChildren(VisualElement parent, XmlElement xmlElement)
        {
            foreach (XmlNode childNode in xmlElement.ChildNodes)
            {
                if (childNode is XmlElement childElement)
                {
                    var childVisualElement = ParseElement(childElement);
                    if (childVisualElement != null)
                    {
                        parent.Add(childVisualElement);
                    }
                }
            }
        }
    }
}
