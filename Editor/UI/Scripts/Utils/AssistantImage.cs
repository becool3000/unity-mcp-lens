using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    class AssistantImage
    {
        static readonly IDictionary<string, Type> k_TypeLookupCache = new Dictionary<string, Type>();

        string m_CurrentIconClass;
        string m_OverrideIconClass;
        Image m_Image;

        public AssistantImage(Image image)
        {
            Debug.Assert(image != null, "Image cannot be null");

            m_Image = image;
        }

        public void SetTexture(Texture2D texture)
        {
            if (texture != null)
            {
                m_Image.image = texture;

                if (!string.IsNullOrEmpty(m_CurrentIconClass))
                {
                    // Unset the class icon, texture overrides
                    m_Image.RemoveFromClassList(m_CurrentIconClass);
                }

                return;
            }

            // Remove the texture and restore the class icon if set
            m_Image.image = null;
            SetIconClassName(m_CurrentIconClass, true);
        }

        public void SetOverrideIconClass(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                if (!string.IsNullOrEmpty(m_OverrideIconClass))
                {
                    m_Image.RemoveFromClassList(m_OverrideIconClass);
                }

                m_OverrideIconClass = null;
                if (!string.IsNullOrEmpty(m_CurrentIconClass))
                {
                    // Restore the old icon
                    m_Image.AddToClassList(m_CurrentIconClass);
                }

                return;
            }

            string fullClassName = AssistantUIConstants.IconStylePrefix + className;
            if (m_CurrentIconClass == fullClassName)
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_CurrentIconClass))
            {
                // Remove the old icon
                m_Image.RemoveFromClassList(m_CurrentIconClass);
            }

            if (string.IsNullOrEmpty(fullClassName))
            {
                return;
            }

            m_OverrideIconClass = fullClassName;

            m_Image.AddToClassList(fullClassName);
        }

        public void SetIconClassName(string className, bool force = false)
        {
            string fullClassName = AssistantUIConstants.IconStylePrefix + className;
            if (m_CurrentIconClass == fullClassName && !force)
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_CurrentIconClass))
            {
                m_Image.RemoveFromClassList(m_CurrentIconClass);
            }

            m_CurrentIconClass = fullClassName;
            if (string.IsNullOrEmpty(fullClassName))
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_OverrideIconClass))
            {
                return;
            }

            m_Image.AddToClassList(fullClassName);
        }

        public void SetIconByTypeString(string typeString)
        {
            if (string.IsNullOrEmpty(typeString))
            {
                m_Image.image = null;
                return;
            }

            var type = FindIconTargetType(typeString);
            if (type == null)
            {
                m_Image.image = null;
                return;
            }

            m_Image.image = EditorGUIUtility.ObjectContent(null, type).image as Texture2D;
        }

        public void SetDisplay(bool isVisible)
        {
            m_Image.SetDisplay(isVisible);
        }

        static Type FindIconTargetType(string typeString)
        {
            if (k_TypeLookupCache.TryGetValue(typeString, out Type result))
            {
                return result;
            }

            var assemblies = AssemblyUtils.GetLoadedAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                foreach (Type type in assemblies[i].GetTypes())
                {
                    if (type.FullName == typeString)
                    {
                        k_TypeLookupCache.Add(typeString, type);
                        return type;
                    }
                }
            }

            return null;
        }
    }
}
