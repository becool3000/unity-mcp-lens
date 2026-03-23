using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// View component for displaying an individual MCP tool item
    /// </summary>
    class McpToolItemView : ManagedTemplate
    {
        Label m_ToolNameLabel;
        Label m_ToolParametersLabel;
        Label m_ToolDescriptionLabel;

        McpTool m_Tool;

        public McpToolItemView() : base(AssistantUIConstants.UIModulePath) { }

        public void SetTool(McpTool tool)
        {
            m_Tool = tool;
            m_ToolNameLabel.text = m_Tool.Name;
            m_ToolParametersLabel.text = BuildParametersString(m_Tool.InputSchema);
            m_ToolDescriptionLabel.text = m_Tool.Description ?? "No description";
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ToolNameLabel = view.Q<Label>("toolNameLabel");
            m_ToolParametersLabel = view.Q<Label>("toolParametersLabel");
            m_ToolDescriptionLabel = view.Q<Label>("toolDescriptionLabel");
        }

        static string BuildParametersString(McpToolInputSchema schema)
        {
            if (schema?.Properties == null || schema.Properties.Count == 0)
                return "(no parameters)";

            var requiredSet = new HashSet<string>(schema.Required ?? System.Array.Empty<string>());
            var parameters = new List<string>();

            foreach (var prop in schema.Properties)
            {
                var propObj = prop.Value as JObject;
                var type = propObj?["type"]?.Value<string>() ?? "any";
                var isRequired = requiredSet.Contains(prop.Key);
                var paramStr = isRequired ? $"{prop.Key}: {type}" : $"{prop.Key}?: {type}";
                parameters.Add(paramStr);
            }

            return $"({string.Join(", ", parameters)})";
        }
    }
}
