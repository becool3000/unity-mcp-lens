using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class ValidateUIAssetFunctionCallElementBase : VisualElement, IFunctionCallRenderer, IAssistantUIContextAware
    {
        JObject m_Parameters;
        AssistantFunctionCall m_FunctionCall;

        public virtual string Title => "UI Asset Validation";
        public virtual string TitleDetails => m_FunctionCall.GetDefaultTitleDetails();
        public virtual bool Expanded { get; private set; }
        public AssistantUIContext Context { get; set; }

        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            m_FunctionCall = functionCall;
            m_Parameters = functionCall.Parameters;
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<UITools.UIAssetValidationResult>();

            if (string.IsNullOrEmpty(typedResult.filePath))
            {
                Add(FunctionCallUtils.CreateContentLabel("UI asset validation failed. No file path returned."));
                return;
            }

            try
            {
                var sourceCode = m_Parameters?["sourceCode"]?.ToString();
                if (string.IsNullOrEmpty(sourceCode))
                {
                    Add(FunctionCallUtils.CreateContentLabel("No source code found in function parameters."));
                    return;
                }

                var preview = FunctionCallUIPreviewHandler.ProcessUIAsset(Context, typedResult.filePath, sourceCode);

                if (preview != null)
                {
                    Add(preview);
                    Expanded = true;
                }
                else
                {
                    Add(FunctionCallUtils.CreateContentLabel($"Validated {typedResult.assetType} asset: {typedResult.filePath}"));
                }
            }
            catch (Exception ex)
            {
                Add(FunctionCallUtils.CreateContentLabel($"Error creating preview: {ex.Message}"));
            }
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            Add(FunctionCallUtils.CreateContentLabel(error));
        }
    }
}
