using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// A generic re-usable code block element, keep it as such, this is not to be made into a specific implementation
    /// Any additions here should be able to be used in any scenario that we want to use a code block
    /// </summary>
    internal class CodeBlockElement : ManagedTemplate
    {
        const string k_DefaultCodePreviewTitle = "C#";
        const string k_CollapsedControlsStyle = "mui-code-block-buttons-collapsed";
        const string k_MarkingStylePrefix = "mui-code-block-marking-";

        static CancellationTokenSource s_CodeCopyButtonActiveTokenSource;
        static CancellationTokenSource s_SaveButtonActiveTokenSource;

        readonly IDictionary<int, LineMarkingInfo> m_LineMarkings = new Dictionary<int, LineMarkingInfo>();

        readonly IDictionary<int, CodeChangeType> m_CodeLineChanges = new Dictionary<int, CodeChangeType>();

        VisualElement m_Controls;
        ScrollView m_Content;
        VisualElement m_ContentBackground;
        VisualElement m_ContentForeground;
        Label m_CodeBlockTitle;
        AssistantImage m_CodeBlockTitleIcon;
        ScrollView m_CodeScrollView;
        Label m_CodeText;
        Foldout m_Toggle;
        Button m_SaveButton;
        AssistantImage m_SaveButtonImage;
        Button m_CopyButton;
        AssistantImage m_CopyButtonImage;

        FileSystemWatcher m_FileWatcher;

        bool m_AnalyticsEnabled;

        string m_Code;

        // Optional old code to calculate and present diff for
        string m_PreviousCodeForDiff;

        string m_CodeType = CodeFormat.CSharp;
        string m_Filename;

        bool m_ReformatCode;

        string m_TempEditedFilePath;

        LineNumberController m_LineNumberController;
        Label m_LineNumberText;

        public Action<string> OnCodeChanged;

        struct LineMarkingInfo
        {
            public int Line;
            public string Tooltip;
            public CodeLineMarkingType Type;
        }

        public CodeBlockElement()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        public bool StripNameSpaces { get; set; }
        public bool SaveWithDisclaimerOnly { get; set; }

        public void SetCodePreviewTitle(string title)
        {
            m_Toggle.text = "";
            SetCustomTitle(title);
        }

        public void SetCustomTitle(string title = null)
        {
            m_CodeBlockTitle.text = string.IsNullOrEmpty(title) ? k_DefaultCodePreviewTitle : title;
        }

        public void SetToggle(bool value)
        {
            ToggleDisplay(value);
        }

        public void SetCode(string code, string previousCodeForDiff = null)
        {
            m_PreviousCodeForDiff = previousCodeForDiff;
            m_Code = code;

            RefreshCodeDisplay();
        }

        public void SetCodeReformatting(bool reformatCode)
        {
            m_ReformatCode = reformatCode;
        }

        public void SetCodeType(string codeType)
        {
            m_CodeType = codeType;

            // Refresh display if code has already been set
            if (m_Code != null)
            {
                RefreshCodeDisplay();
            }
        }

        public void SetFilename(string filename)
        {
            m_Filename = filename;
        }

        public void ShowSaveButton(bool show)
        {
            m_SaveButton.SetDisplay(show);
        }

        public void ShowHeader(bool show)
        {
            m_Controls.SetDisplay(show);
        }

        public void SetEmbeddedMode()
        {
            m_Controls.SetDisplay(false);
            m_Toggle.SetValueWithoutNotify(true);
            m_Content.SetDisplay(true);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            RegisterCallback<DetachFromPanelEvent>(OnDetach);

            m_Controls = view.Q<VisualElement>("codeBlockControls");
            m_Content = view.Q<ScrollView>("codeBlockContent");
            m_ContentBackground = view.Q<VisualElement>("codeTextBackground");
            m_ContentForeground = view.Q<VisualElement>("codeTextForeground");

            m_CodeScrollView = view.Q<ScrollView>("codeScrollView");

            // Work around UI Toolkit bug: Prevent vertical scroll events from being applied to horizontal-only ScrollView
            // The horizontal ScrollView should only respond to horizontal scroll deltas
            m_CodeScrollView.RegisterCallback<WheelEvent>(PreventVerticalScrollOnHorizontalScrollView, TrickleDown.TrickleDown);

            m_CodeText = view.Q<Label>("codeText");
            m_CodeText.selection.isSelectable = true;

            m_Toggle = view.Q<Foldout>("codeBlockDisplayToggle");
            m_Toggle.RegisterValueChangedCallback(x =>
            {
                ToggleDisplay(x.newValue, true);
            });

            view.Q<VisualElement>("actionHeaderContainer").RegisterCallback<PointerUpEvent>(_ =>
            {
                ToggleDisplay(!m_Toggle.value, true);
            });

            // We are showing by default
            ToggleDisplay(true);

            m_SaveButton = view.SetupButton("saveCodeButton", OnSaveCodeClicked);
            m_SaveButtonImage = m_SaveButton.SetupImage("saveCodeButtonImage", "save");

            m_CopyButton = view.SetupButton("copyCodeButton", OnCopyCodeClicked);
            m_CopyButtonImage = m_CopyButton.SetupImage("copyCodeButtonImage", "copy");

            m_CodeBlockTitle = view.Q<Label>("codeBlockTitle");
            m_CodeBlockTitle.text = k_DefaultCodePreviewTitle;
            m_CodeBlockTitleIcon = view.SetupImage("codeBlockTitleIcon", "error");
            m_CodeBlockTitleIcon.SetDisplay(false);

            m_LineNumberText = view.Q<Label>("lineNumberText");
            m_LineNumberController = new LineNumberController(m_CodeText, m_LineNumberText, m_CodeLineChanges);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            DeleteTempEditFile();
            m_FileWatcher?.Dispose();
        }

        internal void SetTitleIcon(bool isEnabled = true)
        {
            m_CodeBlockTitleIcon.SetDisplay(isEnabled);
        }

        public void SetActions(bool copy, bool save, bool select, bool edit)
        {
            m_CopyButton.SetDisplay(copy);
            m_SaveButton.SetDisplay(save);
            m_CodeText.selection.isSelectable = select;
        }

        public void MarkLine(int lineNumber, CodeLineMarkingType type = CodeLineMarkingType.Error, string lineTooltip = "", bool refresh = true)
        {
            var info = new LineMarkingInfo { Line = lineNumber, Tooltip = lineTooltip, Type = type, };
            m_LineMarkings[lineNumber] = info;

            if (refresh)
            {
                RefreshCodeDisplay();
            }
        }

        public void UnmarkLine(int lineNumber, bool refresh = true)
        {
            m_LineMarkings.Remove(lineNumber);

            if (refresh)
            {
                RefreshCodeDisplay();
            }
        }

        public void EnableAnalytics()
        {
            m_AnalyticsEnabled = true;
        }

        public void RefreshCodeDisplay()
        {
            // Update Code preview with AI disclaimer
            string code = m_Code;

            // Clear previous line backgrounds before calculating new diff
            m_CodeLineChanges.Clear();

            if (m_ReformatCode)
            {
                if (StripNameSpaces)
                {
                    // Remove namespaces from display
                    var tree = SyntaxFactory.ParseSyntaxTree(code);
                    code = tree.RemoveNamespaces().GetText().ToString();
                }
                else
                {
                    code = CodeBlockUtils.Format(code);
                }
            }

            code = MarkupUtil.QuoteRichTextTags(code);

            string[] lines;
            if (!string.IsNullOrEmpty(m_PreviousCodeForDiff))
            {
                var diffResult = CodeBlockUtils.CreateDiffCodeLines(m_PreviousCodeForDiff, code);
                lines = diffResult.Lines;
                foreach (var kvp in diffResult.LineChanges)
                {
                    m_CodeLineChanges[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                lines = code.Split(new[] { AssistantConstants.NewLineCRLF, AssistantConstants.NewLineLF }, StringSplitOptions.None);
            }

            var isDiffMode = m_CodeLineChanges?.Count > 0;
            EnableInClassList("mui-code-block-diff-mode", isDiffMode);

            string highlightedText;
            if (isDiffMode)
            {
                highlightedText = MarkupCodeDiff(code, lines);
            }
            else
            {
                switch (m_CodeType)
                {
                    case CodeFormat.Uxml:
                    case CodeFormat.Xml:
                        highlightedText = CodeSyntaxHighlight.HighlightUXML(code);
                        break;
                    case CodeFormat.Css:
                    case CodeFormat.Uss:
                        highlightedText = CodeSyntaxHighlight.HighlightUSS(code);
                        break;
                    default:
                        highlightedText = CodeSyntaxHighlight.HighlightCSharp(code);
                        break;
                }
            }

            m_CodeText.text = highlightedText;
            Context.SearchHelper?.RegisterSearchableTextElement(m_CodeText);

            RefreshMarkings();
        }

        string MarkupCodeDiff(string code, string[] lines)
        {
            var estimatedFinalCapacity = code.Length + (lines.Length * 50); // Estimate for markup overhead
            var coloredCode = new StringBuilder(estimatedFinalCapacity);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var type = m_CodeLineChanges.TryGetValue(i + 1, out var bgType) ? bgType : CodeChangeType.None;

                if (type != CodeChangeType.None)
                {
                    coloredCode.Append("<mark=");
                    coloredCode.Append(type switch
                    {
                        CodeChangeType.Added => AssistantUIConstants.CodeLineAddedColor,
                        CodeChangeType.Removed => AssistantUIConstants.CodeLineRemovedColor,
                        _ => AssistantUIConstants.CodeLineDefaultColor
                    });
                    coloredCode.Append('>');
                    coloredCode.Append(line);
                    coloredCode.Append("</mark>");
                }
                else
                {
                    coloredCode.Append(line);
                }

                if (i < lines.Length - 1)
                    coloredCode.Append(AssistantConstants.NewLineLF);
            }

            return coloredCode.ToString();
        }

        public void DisplayErrors(CompilationErrors compilationErrors)
        {
            m_LineMarkings.Clear();

            var linesWithErrors = compilationErrors.Errors
                .Where(e => e.Line != -1)
                .Select(e => (e.Line + 4, e.Message)) // + 2 for Disclaimer Lines
                .Distinct();

            foreach (var error in linesWithErrors)
            {
                MarkLine(error.Item1, CodeLineMarkingType.Error, error.Item2);
            }

            RefreshCodeDisplay();
        }

        public void ClearDisplayedErrors()
        {
            m_LineMarkings.Clear();
            RefreshCodeDisplay();
        }

        void OnCopyCodeClicked(PointerUpEvent evt)
        {
            GUIUtility.systemCopyBuffer = m_Code;

            m_CopyButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, true);
            m_CopyButtonImage.SetOverrideIconClass("checkmark");
            TimerUtils.DelayedAction(ref s_CodeCopyButtonActiveTokenSource, () =>
            {
                m_CopyButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, false);
                m_CopyButtonImage.SetOverrideIconClass(null);
            });

            AIAssistantAnalytics.ReportUITriggerLocalCopyCodeEvent(Context.Blackboard.ActiveConversation?.Id, m_Code);
        }

        void OnSaveCodeClicked(PointerUpEvent evt)
        {
            AIAssistantAnalytics.ReportUITriggerLocalSaveCodeEvent(Context.Blackboard.ActiveConversation?.Id, m_Code);

            var defaultName = AssistantConstants.DefaultCodeBlockCsharpFilename;
            var defaultExtension = AssistantConstants.DefaultCodeBlockCsharpExtension;

            bool isCSharpLanguage = true;
            var isCodeRoute = m_CodeType?.Contains(AssistantConstants.CodeBlockCsharpValidateFiletype) ?? false;

            if (!isCodeRoute)
            {
                if (m_CodeType != null)
                {
                    isCSharpLanguage = m_CodeType.StartsWith(AssistantConstants.CodeBlockCsharpFiletype,
                                           StringComparison.OrdinalIgnoreCase) ||
                                       m_CodeType.Contains(AssistantConstants.CodeBlockCsharpValidateFiletype);
                }

                bool hasFilename = !string.IsNullOrEmpty(m_Filename);
                if (hasFilename)
                {
                    defaultName = Path.GetFileNameWithoutExtension(m_Filename);
                    var extension = Path.GetExtension(m_Filename);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        defaultExtension = extension.Substring(1);
                    }
                }
                else
                {
                    defaultName = AssistantConstants.DefaultCodeBlockTextFilename;
                    defaultExtension = AssistantConstants.DefaultCodeBlockTextExtension;
                }
            }

            string file = EditorUtility.SaveFilePanel("Save Code", Application.dataPath, defaultName, defaultExtension);
            if (string.IsNullOrEmpty(file))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Saving Code", "Saving code to file", 0.5f);

            try
            {
                string output = m_Code;
                output = SaveWithDisclaimerOnly || !isCSharpLanguage
                    ? CodeBlockUtils.AddDisclaimer(m_CodeType, output)
                    : CodeBlockUtils.Format(output, Path.GetFileNameWithoutExtension(file));

                File.WriteAllText(file, output);
            }
            catch (Exception)
            {
                ErrorHandlingUtils.ShowGeneralError("Failed to save code to file");
                EditorUtility.ClearProgressBar();
                return;
            }

            m_SaveButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, true);
            m_SaveButtonImage.SetOverrideIconClass("checkmark");
            TimerUtils.DelayedAction(ref s_SaveButtonActiveTokenSource, () =>
            {
                m_SaveButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, false);
                m_SaveButtonImage.SetOverrideIconClass(null);
            });

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        void DeleteTempEditFile()
        {
            if (string.IsNullOrEmpty(m_TempEditedFilePath))
                return;

            File.Delete(m_TempEditedFilePath);
            m_TempEditedFilePath = string.Empty;
            AssemblyCSProject.ClearTemporaryFiles();
        }

        void ToggleDisplay(bool isVisible, bool sendAnalytics = false)
        {
            m_Toggle.SetValueWithoutNotify(isVisible);
            m_Content.SetDisplay(isVisible);
            m_Controls.EnableInClassList(k_CollapsedControlsStyle, !isVisible);

            if (isVisible && m_AnalyticsEnabled && sendAnalytics)
                AIAssistantAnalytics.ReportUITriggerLocalExpandCommandLogicEvent();
        }

        void RefreshMarkings()
        {
            m_ContentBackground.Clear();
            m_ContentForeground.Clear();

            if (m_LineMarkings.Count == 0)
            {
                return;
            }

            // Note: This is very specific tuned to the current font settings, any change there will need to be adjusted here
            const float lineHeight = 15.83f;

            foreach (var info in m_LineMarkings.Values)
            {
                float elementPosition = lineHeight * (info.Line - 1);

                var markingElement = new VisualElement
                {
                    style =
                    {
                        marginTop = elementPosition,
                        height = lineHeight
                    }
                };

                markingElement.AddToClassList(k_MarkingStylePrefix + "bg");
                markingElement.AddToClassList(k_MarkingStylePrefix + info.Type.ToString().ToLowerInvariant());
                m_ContentBackground.Add(markingElement);

                if (!string.IsNullOrEmpty(info.Tooltip))
                {
                    var tooltipElement = new VisualElement
                    {
                        style =
                        {
                            marginTop = elementPosition,
                            height = lineHeight
                        }
                    };

                    tooltipElement.AddToClassList(k_MarkingStylePrefix+ "tooltip");
                    tooltipElement.AddToClassList(k_MarkingStylePrefix + "tooltip-" + info.Type.ToString().ToLowerInvariant());
                    tooltipElement.tooltip = info.Tooltip;
                    m_ContentForeground.Add(tooltipElement);
                }
            }
        }


        void PreventVerticalScrollOnHorizontalScrollView(WheelEvent evt)
        {
            if (evt.altKey)
                return;

            var delta = evt.delta;
            var isVerticalScroll = Mathf.Abs(delta.y) > Mathf.Abs(delta.x);

            // This is a horizontal-only ScrollView, but UI Toolkit has a bug where it applies vertical
            // scroll deltas to it as horizontal scrolling. We stop all events and forward them appropriately.
            evt.StopImmediatePropagation();

            if (isVerticalScroll)
            {
                // Forward vertical scroll to parent vertical ScrollView
                var e = new Event(evt.imguiEvent) { delta = new Vector2(0, delta.y) };
                using var newEvent = WheelEvent.GetPooled(e);
                newEvent.target = m_Content;
                SendEvent(newEvent);
            }
            else
            {
                // For horizontal scrolls, convert X delta to Y delta with alt key for horizontal ScrollView
                var e = new Event(evt.imguiEvent) { delta = new Vector2(0, delta.x), alt = true };
                using var newEvent = WheelEvent.GetPooled(e);
                newEvent.target = m_CodeScrollView;
                SendEvent(newEvent);
            }
        }

        public void CloneActionButtons(VisualElement target)
        {
            var originalContainer = this.Q(className: "mui-action-buttons-container");
            if (originalContainer == null)
            {
                return;
            }

            var clonedContainer = new VisualElement();
            clonedContainer.AddToClassList("mui-action-buttons-container");

            foreach (var originalButton in originalContainer.Query<Button>().Build())
            {
                var clone = new Button { tooltip = originalButton.tooltip };
                foreach (var cls in originalButton.GetClasses())
                {
                    clone.AddToClassList(cls);
                }

                // Copy the display style over to match, this is hacky but no other way around it
                clone.style.display = originalButton.style.display;

                foreach (var img in originalButton.Query<Image>().Build())
                {
                    var imgClone = new Image();
                    foreach (var cls in img.GetClasses())
                    {
                        imgClone.AddToClassList(cls);
                    }
                    imgClone.pickingMode = PickingMode.Ignore;
                    clone.Add(imgClone);
                }

                // Register the original button's action directly on the clone instead of
                // forwarding via synthetic events, which fails due to UI Toolkit's event
                // dispatch not reliably processing synthesized PointerUpEvents.
                var action = GetButtonAction(originalButton);
                if (action != null)
                    clone.RegisterCallback(action);

                clonedContainer.Add(clone);
            }

            target.Add(clonedContainer);
        }

        EventCallback<PointerUpEvent> GetButtonAction(Button button)
        {
            if (button == m_CopyButton) return OnCopyCodeClicked;
            if (button == m_SaveButton) return OnSaveCodeClicked;
            return null;
        }
    }
}
