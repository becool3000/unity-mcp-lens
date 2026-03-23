using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Renders diff content for file edit/write permission requests.
    /// </summary>
    class DiffPermissionContentRenderer : IPermissionContentRenderer
    {
        const string k_NewStringField = "new_string";
        const string k_OldStringField = "old_string";
        const string k_ContentField = "content";
        const string k_FilePathField = "file_path";

        public bool CanRender(JObject rawInput)
        {
            // Edit: has old_string and new_string
            // Write: has file_path and content (no old_string)
            return rawInput?[k_NewStringField] != null || rawInput?[k_ContentField] != null;
        }

        public VisualElement Render(JObject rawInput, AssistantUIContext context)
        {
            string newCode, oldCode, filePath;

            if (rawInput[k_NewStringField] != null)
            {
                // Edit operation
                newCode = rawInput[k_NewStringField]?.ToString();
                oldCode = rawInput[k_OldStringField]?.ToString();
                filePath = rawInput[k_FilePathField]?.ToString();
            }
            else
            {
                // Write operation (new file) - empty string triggers all-green diff
                newCode = rawInput[k_ContentField]?.ToString();
                oldCode = "";
                filePath = rawInput[k_FilePathField]?.ToString();
            }

            if (string.IsNullOrEmpty(newCode))
                return null;

            var container = new ScrollView();
            container.AddToClassList("permission-content-container");
            container.style.maxHeight = 400;

            var codeBlock = new CodeBlockElement();
            codeBlock.Initialize(context);

            // Set the title to the filename (clears the default "C#" label)
            if (!string.IsNullOrEmpty(filePath))
            {
                codeBlock.SetCodePreviewTitle(Path.GetFileName(filePath));
            }

            codeBlock.SetCode(newCode, oldCode);
            codeBlock.ShowSaveButton(false);

            container.Add(codeBlock);
            return container;
        }
    }
}
