using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.Tools.Editor
{
    [FunctionCallRenderer(typeof(SettingsTools), nameof(SettingsTools.GetProjectSettings))]
    class GetProjectSettingFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Get Project Setting";

        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<SettingsTools.GetProjectSettingsOutput>();

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
            
            Add(FunctionCallUtils.CreateContentLabel(text));
        }
    }
}
