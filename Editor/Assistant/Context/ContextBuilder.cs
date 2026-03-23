using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Context
{
    /// <summary>
    /// Context builder is used to build a context string from a list of context selections.
    /// </summary>
    class ContextBuilder
    {
        readonly EditorContextReport m_ContextList = new(
            attachedContext: new List<ContextItem>(),
            extractedContext: new List<ContextItem>(),
            characterLimit: AssistantMessageSizeConstraints.ContextLimit);

        internal int PredictedLength { get; private set; }

        /// <summary>
        /// Adds the given piece of context to the context list.
        /// </summary>
        /// <param name="contextSelection">The context to add.</param>
        /// <param name="userSelected">True if this context was added by the user.</param>
        /// <param name="priority">Higher priority values make it more likely to be included when the limit is reached.</param>
        internal ContextItem InjectContext(IContextSelection contextSelection, int priority = 0)
        {
            var payload = contextSelection?.Payload;

            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            if (m_ContextList.AttachedContext.Any(existingContext => existingContext.Context == contextSelection))
            {
                return null;
            }

            if (contextSelection is UnityObjectContextSelection contextSelectionAsUnityObjectContextSelection)
            {
                foreach (var existingContext in m_ContextList.AttachedContext)
                {
                    if (existingContext.Context is not UnityObjectContextSelection
                        existingAsUnityObjectContextSelection) continue;

                    if (existingAsUnityObjectContextSelection.Target ==
                        contextSelectionAsUnityObjectContextSelection.Target)
                        return null;
                }
            }

            // Find if the context is already in the list and get that element:
            var existingContextItem =
                m_ContextList.AttachedContext.FirstOrDefault(existingContext => existingContext.Payload == payload);

            if (existingContextItem != null)
            {
                // TODO: Count copies here:
                // existingContextItem.NumberOfCopies += 1;
                return null;
            }

            var contextItem = new ContextItem(payload, false, contextSelection.ContextType)
            {
                Context = contextSelection
            };

            PredictedLength += contextItem.Payload.Length;

            m_ContextList.AttachedContext.Add(contextItem);
            return contextItem;
        }

        internal bool Contains(string contextString)
        {
            return m_ContextList.AttachedContext.Any(contextPiece =>
                contextPiece.Payload.Contains(contextString));
        }

        private void ProcessContextList(List<ContextItem> contextList, int contextLimit)
        {
            // Make sure we got space for everything:
            for (var i = 0; i < contextList.Count; i++)
            {
                var contextPiece = contextList[i];

                // If the new length would exceed the limit, try using the downsized payload:
                if (PredictedLength + contextPiece.Payload.Length > contextLimit)
                {
                    contextPiece.Payload = ((IContextSelection)contextPiece.Context).DownsizedPayload;
                    contextPiece.Truncated = true;

                    // If the new length still exceeds the limit, remove this piece:
                    if (string.IsNullOrEmpty(contextPiece.Payload) ||
                        PredictedLength + contextPiece.Payload.Length > contextLimit)
                    {
                        contextList.RemoveAt(i--);
                    }
                }

                PredictedLength += contextPiece.Payload?.Length ?? 0;
            }
        }

        internal EditorContextReport BuildContext(int contextLimit)
        {
            m_ContextList.Sort();

            PredictedLength = 0;
            ProcessContextList(m_ContextList.AttachedContext, contextLimit);
            PredictedLength = m_ContextList.AttachedContext.Sum(contextItem => contextItem.Payload.Length);

            return m_ContextList;
        }
    }
}
