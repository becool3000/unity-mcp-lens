using System.Collections.Generic;
using System.IO;
using Unity.AI.Assistant.Editor.Utils;
using Unity.Ai.Assistant.Protocol.Model;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Context
{
    /// <summary>
    /// Allows a Unity object or asset to be sent to the LLM for evaluation
    /// </summary>
    internal class UnityObjectContextSelection : IContextSelection
    {
        Object m_Target;

        static readonly List<string> k_ExtensionsToExtract = new() { ".cs", ".json", ".shader", AssistantConstants.UxmlExtension };

        public GameObject GameObject => m_Target as GameObject;
        public Object Target => m_Target;

        // The serialized json string will be limited to this length:
        public int SerializationLimit = AssistantMessageSizeConstraints.ContextLimit;

        public void SetTarget(Object target)
        {
            m_Target = target;
        }

        string IContextSelection.Classifier
        {
            get
            {
                if (m_Target == null)
                    return "Null";

                // We might want to special path for gameobjects to include all their components
                return $"UnityEngine.Object, {m_Target.GetType().Name}";
            }
        }

        string IContextSelection.Description
        {
            get
            {
                if (m_Target == null)
                    return "No object selected";

                return $"{m_Target.name} - {m_Target.GetType().Name}";
            }
        }

        string IContextSelection.Payload => GetPayload();

        string IContextSelection.DownsizedPayload => GetPayload();

        string IContextSelection.ContextType
        {
            get
            {
                if (m_Target == null)
                    return null;

                string path = AssetDatabase.GetAssetPath(m_Target);
                if (path.EndsWith(".cs"))
                {
                    return "monoscript";
                }

                if (path.EndsWith(".json"))
                {
                    return "json";
                }
                return m_Target.GetType().Name;
            }
        }

        string IContextSelection.TargetName => $"{m_Target.name}";

        bool? IContextSelection.Truncated => null;

        bool System.IEquatable<IContextSelection>.Equals(IContextSelection other)
        {
            if (other is not UnityObjectContextSelection otherSelection)
                return false;

            return otherSelection.m_Target == m_Target;
        }

        string GetPayload()
        {
            if (m_Target == null)
                return null;

            string path;
            string info = $"\n- Name: {m_Target.name}" +
                          $"\n- Type: {m_Target.GetType()}" +
                          $"\n- Instance ID: {m_Target.GetInstanceID()}";

            if (EditorUtility.IsPersistent(m_Target))
            {
                path = AssetDatabase.GetAssetPath(m_Target);

                info += $"\n- GUID: {AssetDatabase.AssetPathToGUID(path)}" +
                        $"\n- Path: {path}";
            }
            else if (m_Target is GameObject go)
            {
                path = GetHierarchyPath(go, out var sceneName);
                info += string.IsNullOrEmpty(sceneName)
                    ? $"\n- Hierarchy Path: {path}"
                    : $"\n- Scene Name: {sceneName}\nHierarchy Path: {path}";
            }

            return $"{info}\n";
        }

        string GetHierarchyPath(GameObject go, out string sceneName)
        {
            string path = go.name;
            Transform current = go.transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            sceneName = go.scene.IsValid() ? go.scene.name : "";

            return path;
        }
    }
}
