using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    static class ContextVisualElementRegistry
    {
        const double k_VisualElementRefreshRate = 2f;

        static readonly Dictionary<AssistantContextEntry, List<WeakReference<IContextReferenceVisualElement>>> k_ElementLookup = new();
        static readonly Dictionary<WeakReference<IContextReferenceVisualElement>, AssistantContextEntry> k_TargetLookup = new();

        static double s_LastRefreshTime;
        static bool s_CallbackRegistered;

        static void ClearMissingReferences()
        {
            if (k_ElementLookup.Count == 0)
            {
                return;
            }

            foreach (var target in k_ElementLookup.Keys)
            {
                var references = k_ElementLookup[target];
                for (var i = references.Count - 1; i > 0; i--)
                {
                    var reference = references[i];
                    if (!reference.TryGetTarget(out var refTarget))
                    {
                        references.RemoveAt(i);
                        k_TargetLookup.Remove(reference);
                    }
                }
            }
        }

        static void RefreshVisualElements()
        {
            if (EditorApplication.timeSinceStartup - s_LastRefreshTime < k_VisualElementRefreshRate)
            {
                return;
            }

            // Guard against invalid entries
            ClearMissingReferences();

            if (k_ElementLookup.Count == 0)
            {
                EditorApplication.update -= RefreshVisualElements;
                s_CallbackRegistered = false;
                return;
            }

            s_LastRefreshTime = EditorApplication.timeSinceStartup;

            foreach (var context in k_ElementLookup.Keys)
            {
                // We pre-check the context here and just notify the target of the new state, this saves time in the elements
                UnityEngine.Object targetObject;
                Component targetComp = null;
                switch (context.EntryType)
                {
                    case AssistantContextType.HierarchyObject:
                    case AssistantContextType.SubAsset:
                    case AssistantContextType.SceneObject:
                    {
                        targetObject = context.GetTargetObject();
                        break;
                    }

                    case AssistantContextType.Component:
                    {
                        targetObject = context.GetTargetObject();
                        if (targetObject != null)
                        {
                            targetComp = context.GetComponent();
                        }

                        break;
                    }

                    default:
                    {
                        return;
                    }
                }

                // Now notify our references with the target info
                var references = k_ElementLookup[context];
                for (var i = 0; i < references.Count; i++)
                {
                    if (references[i].TryGetTarget(out var reference))
                    {
                        reference.RefreshVisualElement(targetObject, targetComp);
                    }
                }
            }
        }

        public static void AddElement(AssistantContextEntry key, IContextReferenceVisualElement element)
        {
            switch (key.EntryType)
            {
                case AssistantContextType.Component:
                case AssistantContextType.SceneObject:
                case AssistantContextType.HierarchyObject:
                case AssistantContextType.SubAsset:
                {
                    // Only add elements with context targets that need update, all others we will not call
                    break;
                }

                default:
                {
                    return;
                }
            }

            if (!k_ElementLookup.TryGetValue(key, out var references))
            {
                references = new List<WeakReference<IContextReferenceVisualElement>>();
                k_ElementLookup.Add(key, references);
            }

            var reference = new WeakReference<IContextReferenceVisualElement>(element);
            references.Add(reference);
            k_TargetLookup.Add(reference, key);

            if (!s_CallbackRegistered)
            {
                EditorApplication.update += RefreshVisualElements;
                s_CallbackRegistered = true;
            }
        }

        public static void RemoveElement(IContextReferenceVisualElement element)
        {
            foreach (var reference in k_TargetLookup.Keys)
            {
                if (!reference.TryGetTarget(out var target) || target != element)
                {
                    continue;
                }

                var context = k_TargetLookup[reference];
                if (k_ElementLookup.TryGetValue(context, out var refList))
                {
                    refList.Remove(reference);
                    if (refList.Count == 0)
                    {
                        k_ElementLookup.Remove(context);
                    }
                }

                if (k_ElementLookup.Count == 0)
                {
                    // Have to defer the clear and reset to after the loop, but we already know theres nothing left
                    break;
                }
            }

            if (k_ElementLookup.Count == 0)
            {
                ClearAndReset();
            }
        }

        static void ClearAndReset()
        {
            k_ElementLookup.Clear();
            k_TargetLookup.Clear();

            EditorApplication.update -= RefreshVisualElements;
            s_CallbackRegistered = false;
        }
    }
}
