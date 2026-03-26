using System.IO;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Renders ACP file edit/write tool calls with a diff view,
    /// mirroring the native <see cref="CodeEditFunctionCallElement"/> appearance.
    /// Handles both Claude Code built-in tools (snake_case params) and
    /// Unity MCP tools (camelCase params).
    /// </summary>
    [AcpToolCallRenderer("Edit")]
    [AcpToolCallRenderer("Write")]
    [AcpToolCallRenderer("Unity_CodeEdit")]
    class AcpCodeEditRenderer : VisualElement, IAcpToolCallRenderer, IAssistantUIContextAware
    {
        const string k_CodeEditTitle = "Code Edit";

        CodeBlockElement m_CodeDiff;

        public string Title => k_CodeEditTitle;
        public string TitleDetails { get; set; }
        public bool Expanded => true;
        public AssistantUIContext Context { get; set; }

        public void OnToolCall(AcpToolCallInfo info)
        {
            if (info == null)
                return;

            var rawInput = info.RawInput;
            if (rawInput == null)
                return;

            // Extract fields, checking both snake_case (Claude Code) and camelCase (Unity MCP)
            var newString = rawInput["new_string"]?.ToString() ?? rawInput["newString"]?.ToString();
            var oldString = rawInput["old_string"]?.ToString() ?? rawInput["oldString"]?.ToString();
            var filePath = rawInput["file_path"]?.ToString() ?? rawInput["filePath"]?.ToString();
            var content = rawInput["content"]?.ToString();

            // Determine operation: edit (has newString) vs write (has content, no newString)
            string newCode, oldCode;
            bool isEdit;

            if (newString != null)
            {
                newCode = newString;
                oldCode = oldString;
                isEdit = true;
            }
            else if (content != null)
            {
                newCode = content;
                oldCode = "";
                isEdit = false;
            }
            else
            {
                // First tool_call event may have rawInput: {} — nothing to render yet
                return;
            }

            var filename = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : null;
            TitleDetails = filename != null
                ? $"{(isEdit ? "Edit" : "Create")} {filename}"
                : string.Empty;

            // Instantiate CodeBlockElement once, but allow SetCode on every call
            // so the diff updates progressively as tool arguments stream in.
            if (m_CodeDiff == null)
            {
                m_CodeDiff = new CodeBlockElement();
                m_CodeDiff.Initialize(Context);
                m_CodeDiff.ShowSaveButton(false);
                Add(m_CodeDiff);

                if (filename != null)
                {
                    m_CodeDiff.SetCustomTitle(filename);
                    m_CodeDiff.SetFilename(filename);
                }
                else
                {
                    m_CodeDiff.SetCustomTitle("");
                    m_CodeDiff.SetFilename("");
                }
            }

            if (!string.IsNullOrEmpty(newCode) || !string.IsNullOrEmpty(oldCode))
                m_CodeDiff.SetCode(newCode, oldCode);
        }

        public void OnToolCallUpdate(AcpToolCallUpdate update)
        {
            // Diff is already displayed from OnToolCall — no additional content needed
        }

        public void OnConversationCancelled()
        {
            // No-op — diff content remains as-is, status indicator is handled by AcpToolCallElement
        }
    }
}
