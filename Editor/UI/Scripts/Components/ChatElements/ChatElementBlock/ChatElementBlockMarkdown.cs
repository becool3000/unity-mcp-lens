using System;
using System.Collections.Generic;
using System.Text;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class ChatElementBlockMarkdown<T> : ChatElementBlockBase<T> where T : IMessageBlockModel
    {
        // Note: We may have to tweak this dynamically based on what content we intend to add to the text element
        const int k_MessageChunkSize = 80000;
        const string k_ActionCursorClassName = "mui-action-cursor";
        static readonly string[] k_NewLineTokens = { "\r\n", "\n", "\r" };

        protected IList<SourceBlock> m_SourceBlocks;

        IList<string> MarkdownChunks { get; } = new List<string>();

        protected enum LinkType
        {
            Reference,
            GameObject,
            Url
        }

        protected void BuildMarkdownChunks(string content, bool isCompleted)
        {
            m_SourceBlocks?.Clear();
            MarkdownChunks.Clear();

            if (string.IsNullOrEmpty(content))
                return;

            MessageUtils.ProcessContent(content, isCompleted, ref m_SourceBlocks, out var messageContent);

            var lines = messageContent.Split(k_NewLineTokens, StringSplitOptions.None);
            var chunk = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var chunkContent = SplitChunkContent(lines[i]);
                for (var ic = 0; ic < chunkContent.Length; ic++)
                {
                    if (chunk.Length > k_MessageChunkSize)
                    {
                        MarkdownChunks.Add(chunk.ToString());
                        chunk.Clear();
                    }

                    if (ic < chunkContent.Length - 1)
                    {
                        chunk.Append(chunkContent[ic]);
                    }
                    else
                    {
                        chunk.AppendLine(chunkContent[ic]);
                    }
                }
            }

            if (chunk.Length > 0)
            {
                MarkdownChunks.Add(chunk.ToString());
            }
        }

        static string[] SplitChunkContent(string content)
        {
            var lineChunks = 1 + (int)Mathf.Floor(content.Length / (float)k_MessageChunkSize);
            var result = new string[lineChunks];
            for(var i = 0; i < lineChunks; i++)
            {
                var start = i * k_MessageChunkSize;
                var length = Mathf.Min(content.Length - start, k_MessageChunkSize);
                result[i] = content.Substring(start, length);
            }

            return result;
        }

        protected void RefreshText(VisualElement root)
        {
            using var pooledTextFields = ListPool<VisualElement>.Get(out var textFields);
            using var pooledTextElements = ListPool<VisualElement>.Get(out var textElements);

            // Parse chunks and add text elements with formatting/rich text tags where applicable
            textFields.Clear();
            root.Clear();

            VisualElement lastElement = null;
            for (var i = 0; i < MarkdownChunks.Count; i++)
            {
                var text = MarkdownChunks[i];

                textElements.Clear();
                MarkdownAPI.MarkupText(Context, text, m_SourceBlocks, textElements, lastElement);

                for (var id = 0; id < textElements.Count; id++)
                {
                    var visualElement = textElements[id];

                    if (textFields.Count <= id)
                    {
                        if (visualElement is Label labelElement)
                            labelElement.selection.isSelectable = true;

                        // Note: click event is not propagated by containers so we need to setup each child instead
                        // But over / out events are properly propagated
                        RegisterLinkCallbacks(visualElement, OnLinkClicked);

                        visualElement.RegisterCallback<PointerOverLinkTagEvent>(OnLinkOver);
                        visualElement.RegisterCallback<PointerOutLinkTagEvent>(OnLinkOut);

                        textFields.Add(visualElement);
                        root.Add(visualElement);
                        lastElement = visualElement;
                    }
                }
            }

            // Clear out obsolete fields
            for (var id = textFields.Count - 1; id >= textElements.Count; id--)
            {
                var obsoleteField = textFields[id];
                obsoleteField.RemoveFromHierarchy();
                UnregisterLinkCallbacks(obsoleteField, OnLinkClicked);
                obsoleteField.UnregisterCallback<PointerOverLinkTagEvent>(OnLinkOver);
                obsoleteField.UnregisterCallback<PointerOutLinkTagEvent>(OnLinkOut);
                textFields.RemoveAt(id);
            }
        }

        void OnLinkOut(PointerOutLinkTagEvent evt)
        {
            if (evt.target is not VisualElement visualElement)
                return;

            visualElement.RemoveFromClassList(k_ActionCursorClassName);
        }

        void OnLinkOver(PointerOverLinkTagEvent evt)
        {
            if (evt.target is not VisualElement visualElement)
                return;

            visualElement.AddToClassList(k_ActionCursorClassName);
        }

        void OnLinkClicked(PointerDownLinkTagEvent evt)
        {
            if (evt.linkID?.IndexOf(AssistantConstants.SourceReferencePrefix) >= 0)
            {
                HandleLinkClick(LinkType.Reference, evt.linkID.Replace(AssistantConstants.SourceReferencePrefix, ""));
                return;
            }

            if (evt.linkID != null && evt.linkID.Contains("://"))
            {
                HandleLinkClick(LinkType.Url, evt.linkID);
                return;
            }

            HandleLinkClick(LinkType.GameObject, evt.linkID);
        }

        protected void HandleLinkClick(LinkType type, string id)
        {
            switch (type)
            {
                case LinkType.Reference:
                {
                    if (!int.TryParse(id, out var sourceId) || m_SourceBlocks.Count <= sourceId || sourceId < 0)
                    {
                        Debug.LogError("Invalid Source ID: " + sourceId);
                        return;
                    }

                    var sourceBlock = m_SourceBlocks[sourceId];
                    Application.OpenURL(sourceBlock.source);

                    return;
                }

                case LinkType.GameObject:
                {
                    if (!MessageUtils.GetAssetFromLink(id, out var asset))
                    {
                        Debug.LogWarning("Asset not found: " + id);
                        return;
                    }

                    Selection.activeObject = asset;
                    return;
                }

                case LinkType.Url:
                {
                    var conversationId = Context.Blackboard.ActiveConversationId.Value;
                    if (!LinkHandlerRegistry.TryHandle(conversationId, id))
                        Application.OpenURL(id);
                    break;
                }

                default:
                {
                    Debug.LogError("Unhandled link type: " + type + " == " + id);
                    return;
                }
            }
        }

        static void RegisterLinkCallbacks(VisualElement root, EventCallback<PointerDownLinkTagEvent> onLinkClicked)
        {
            if (root == null || onLinkClicked == null)
                return;

            foreach (var child in root.Children())
            {
                RegisterLinkCallbacks(child, onLinkClicked);
            }

            if (root is TextElement or Label)
                root.RegisterCallback(onLinkClicked);
        }

        static void UnregisterLinkCallbacks(VisualElement root, EventCallback<PointerDownLinkTagEvent> onLinkClicked)
        {
            if (root == null || onLinkClicked == null)
                return;

            foreach (var child in root.Children())
            {
                RegisterLinkCallbacks(child, onLinkClicked);
            }

            if (root is TextElement or Label)
                root.UnregisterCallback(onLinkClicked);
        }
    }
}
