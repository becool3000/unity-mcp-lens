using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.Tools.Editor
{
    [FunctionCallRenderer(typeof(AssetTools), nameof(AssetTools.GetTextAssetContent))]
    class GetTextAssetContentFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Get Text Asset Content";
        
        public override void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = functionCall.GetDefaultTitleDetails("instanceID", FunctionCallParameterFormatter.FormatInstanceID);
        }
        
        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<AssetTools.GetTextAssetContentOutput>();
            var label = FunctionCallUtils.CreateContentLabel(typedResult.Data);
            Add(label);
        }
    }
}
