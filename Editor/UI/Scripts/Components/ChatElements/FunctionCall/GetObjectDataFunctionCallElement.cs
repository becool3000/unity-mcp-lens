using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.Tools.Editor
{
    [FunctionCallRenderer(typeof(ObjectTools), nameof(ObjectTools.GetObjectData))]
    class GetObjectDataFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Get Object Data";

        public override void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = functionCall.GetDefaultTitleDetails("instanceID", FunctionCallParameterFormatter.FormatInstanceID);
        }
        
        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<ObjectTools.GetObjectDataOutput>();
            var data = typedResult.Data;

            string text;
            try
            {
                text = JToken.Parse(data).ToString(Formatting.Indented);
            }
            catch (Exception e)
            {
                text = "Json: \n\n" + data + "\n\nError:\n" + e;
            }
            
            var label = FunctionCallUtils.CreateContentLabel(text);
            Add(label);
        }
    }
}
