using System.Collections.Generic;
using System.Text;
using Markdig.Renderers;
using Markdig.Syntax;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup
{
    /// <summary>
    /// Helper class to parse text (chat responses) and reformat blocks of text to improve readability
    /// of for example quoted text, code blocks, and links.
    /// </summary>
    internal class ChatMarkdownRenderer : RendererBase
    {
        internal enum VisitingState
        {
            Text,
            Code,
            SourceContent,
            SourceBoundaryEnd,
        }

        internal VisualElement m_PreviousLastElement;
        internal readonly IList<VisualElement> m_OutputTextElements;
        internal VisitingState m_VisitingState;

        readonly AssistantUIContext m_UIContext;

        private StringBuilder m_Builder = new();
        private IList<SourceBlock> m_SourceBlocks;

        private Stack<ListBlock> m_ListBlocks = new();
        private ListBlock m_CurrentListBlock;
        private Stack<QuoteBlock> m_QuoteBlocks = new();
        private QuoteBlock m_CurrentQuoteBlock;

        public bool IsListBlock => m_CurrentListBlock != null;
        public bool IsQuoteBlock => m_CurrentQuoteBlock != null;

        private TextElement m_CurrentTextElement;

        private Stack<ChatElementQuote> m_QuoteElements = new();
        private ChatElementQuote m_CurrentQuoteElement;
        private ChatElementTable m_CurrentTableElement;

        private Stack<ChatElementList> m_ListElements = new();
        private ChatElementList m_CurrentListElement;

        public bool m_ParagraphAddNewLine = true;

        public ChatMarkdownRenderer(AssistantUIContext uiContext, IList<SourceBlock> sourceBlocks, IList<VisualElement> outTextElements, VisualElement previousLastElement)
        {
            m_UIContext = uiContext;

            m_SourceBlocks = sourceBlocks;
            m_OutputTextElements = outTextElements;
            m_PreviousLastElement = previousLastElement;

            // Required to output anything, blocks of text/markup; as default outputs text without specific parser
            ObjectRenderers.Add(new ParagraphRenderer(uiContext));

            // Required to output any kind of text
            ObjectRenderers.Add(new LiteralInlineRenderer());

            // Optional for HTML tags, which includes rich text like "<b>"
            ObjectRenderers.Add(new HtmlInlineRenderer());
            // Optional for HTML entities, which includes e.g. "&lt;" and as transcoded becomes "<"
            ObjectRenderers.Add(new HtmlEntityInlineRenderer());

            ObjectRenderers.Add(new EmphasisInlineRenderer());
            ObjectRenderers.Add(new LineBreakInlineRenderer());
            ObjectRenderers.Add(new LinkInlineRenderer());

            // Blocks for headings and quotes
            ObjectRenderers.Add(new HeadingBlockRenderer());
            ObjectRenderers.Add(new QuoteBlockRenderer());

            // Required to output block of code within "```csharp ... ```" for example
            ObjectRenderers.Add(new FencedCodeBlockRenderer(uiContext));

            ObjectRenderers.Add(new CodeInlineBlockRenderer());

            // Required so include list start/block AND list items
            ObjectRenderers.Add(new ListBlockRenderer());
            ObjectRenderers.Add(new ListItemBlockRenderer());

            // Renders rules (horizontal separators)
            ObjectRenderers.Add(new ThematicBreakRenderer());

            // Tables
            ObjectRenderers.Add(new TableRenderer());
        }

        internal void AppendText(string text)
        {
            m_Builder.Append(text);
        }

        internal string ClearText()
        {
            string text = m_Builder.ToString();
            m_Builder.Clear();
            return text;
        }

        internal void PushListBlock(ListBlock listBlock)
        {
            if (listBlock != null && !m_ListBlocks.Contains(listBlock))
            {
                m_ListBlocks.Push(listBlock);

                m_CurrentListBlock = listBlock;

                UpdateListElement(true);
            }
        }

        internal void PopListBlock()
        {
            m_ListBlocks.Pop();

            if (m_ListBlocks.Count > 0)
                m_CurrentListBlock = m_ListBlocks.Peek();
            else
                m_CurrentListBlock = null;

            UpdateListElement(false);
        }

        internal void PushListElement(ListItemBlock listElement)
        {
            UpdateListElement(true);
        }

        internal void PopListElement()
        {
            UpdateListElement(false);
        }

        private void UpdateListElement(bool pushNewElement)
        {
            CloseTextElement();

            if (pushNewElement)
            {
                var newElement = new ChatElementList();
                newElement.Initialize(m_UIContext);

                newElement.SetIndentation(m_ListBlocks.Count);

                m_ListElements.Push(newElement);

                // TODO: Could be generalized, whether a block (quote, list, text) is nested or on the top-level
                if (m_CurrentQuoteElement == null)
                {
                    m_CurrentListElement = newElement;
                    m_OutputTextElements.Add(m_CurrentListElement);
                }
                else
                {
                    m_CurrentQuoteElement.AddElement(newElement);
                    m_CurrentListElement = newElement;
                }

                StartNewTextElement();
            }
            else
            {
                m_ListElements.Pop();

                m_CurrentListElement = m_ListElements.Count > 0 ? m_ListElements.Peek() : null;
            }
        }

        internal string GetCurrentListBullet(ListItemBlock listItemBlock)
        {
            if (m_CurrentListBlock == null)
                return null;

            return m_CurrentListBlock.IsOrdered ? $"{listItemBlock.Order}. " : "\u2022 ";
        }

        public ChatElementList GetCurrentListElement()
        {
            return m_CurrentListElement;
        }

        internal void PushQuoteBlock(QuoteBlock quoteBlock)
        {
            if (quoteBlock != null && !m_QuoteBlocks.Contains(quoteBlock))
            {
                m_QuoteBlocks.Push(quoteBlock);

                m_CurrentQuoteBlock = quoteBlock;

                UpdateQuoteElement(true);
            }
        }

        internal void PopQuoteBlock()
        {
            m_QuoteBlocks.Pop();

            if (m_QuoteBlocks.Count > 0)
                m_CurrentQuoteBlock = m_QuoteBlocks.Peek();
            else
                m_CurrentQuoteBlock = null;

            UpdateQuoteElement(false);
        }

        private void UpdateQuoteElement(bool pushNewElement)
        {
            CloseTextElement();

            // Create or remove top-level quote element wrapping other elements
            if (pushNewElement)
            {
                var newElement = new ChatElementQuote();
                newElement.Initialize(m_UIContext);

                m_QuoteElements.Push(newElement);

                if (m_CurrentQuoteElement == null)
                {
                    m_CurrentQuoteElement = newElement;
                    m_OutputTextElements.Add(m_CurrentQuoteElement);
                }
                else
                {
                    m_CurrentQuoteElement.AddElement(newElement);
                    m_CurrentQuoteElement = newElement;
                }

                StartNewTextElement();
            }
            else
            {
                m_QuoteElements.Pop();

                var currentElement = m_QuoteElements.Count > 0 ? m_QuoteElements.Peek() : null;

                if (currentElement == null)
                {
                    // Avoid margin on the last TextElement, if any is contained
                    VisualElement lastElement = null;
                    foreach (var element in m_CurrentQuoteElement.NestedElements())
                    {
                        if (element is VisualElement vElement)
                        {
                            lastElement = vElement;
                        }
                    }

                    if (lastElement != null)
                    {
                        lastElement.style.marginBottom = default;
                    }
                }

                m_CurrentQuoteElement = currentElement;
            }

            Debug.Assert(m_QuoteBlocks.Count == m_QuoteElements.Count);
        }

        internal void AddSource(string source)
        {
            var sourceBlock = JsonUtility.FromJson<SourceBlock>(source);
            m_SourceBlocks.Add(sourceBlock);
        }

        public override object Render(MarkdownObject markdownObject)
        {
            Write(markdownObject);
            return null;
        }

        public TextElement GetCurrentTextElement(VisualElement customParentElement = null)
        {
            if (m_CurrentTextElement == null ||
                customParentElement != null && m_CurrentTextElement.parent != customParentElement)
            {
                StartNewTextElement(customParentElement);
            }

            return m_CurrentTextElement;
        }

        private void StartNewTextElement(VisualElement customParentElement = null)
        {
            CloseTextElement();

            m_CurrentTextElement = new TextElement();

            m_CurrentTextElement.AddToClassList("mui-chat-response-textelement");

            // Either nested in quote, or just a top-level TextElement
            if (customParentElement != null)
            {
                customParentElement.Add(m_CurrentTextElement);
            }
            else if (m_CurrentListElement != null)
            {
                m_CurrentListElement.AddRightElement(m_CurrentTextElement);
            }
            else if (m_CurrentQuoteElement != null)
            {
                m_CurrentQuoteElement.AddElement(m_CurrentTextElement);
            }
            else
            {
                m_OutputTextElements.Add(m_CurrentTextElement);
            }

            m_CurrentTextElement.selection.isSelectable = true;
        }

        public void CloseTextElement()
        {
            var remainingText = ClearText();

            // Ensure built text ends up in existing or new TextElement
            if (remainingText.Length > 0)
            {
                if (m_CurrentTextElement == null)
                {
                    StartNewTextElement();
                }

                m_CurrentTextElement.text += remainingText;
            }

            m_CurrentTextElement = null;
        }

        public void FlushText()
        {
            var remainingText = ClearText();

            if (remainingText.Length > 0 && m_CurrentTextElement != null)
            {
                m_CurrentTextElement.text += remainingText;
            }
        }

        public ChatElementTable StartNewTableElement()
        {
            CloseTextElement();
            CloseTableElement();

            m_CurrentTableElement = new ChatElementTable();
            m_CurrentTableElement.Initialize(m_UIContext);

            // Either nested in quote, or just a top-level TextElement
            if (m_CurrentQuoteElement != null)
            {
                m_CurrentQuoteElement.AddElement(m_CurrentTableElement);
            }
            else
            {
                m_OutputTextElements.Add(m_CurrentTableElement);
            }

            return m_CurrentTableElement;
        }

        public void CloseTableElement()
        {
            m_CurrentTableElement = null;
        }

        public void InsertSeparatorLine()
        {
            CloseTextElement();

            var separator = new VisualElement();
            separator.AddToClassList("mui-chat-response-separatorline");

            if (m_CurrentQuoteElement != null)
                m_CurrentQuoteElement.AddElement(separator);
            else
                m_OutputTextElements.Add(separator);
        }
    }
}
