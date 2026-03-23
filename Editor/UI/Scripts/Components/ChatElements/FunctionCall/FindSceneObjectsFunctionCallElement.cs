using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.Tools.Editor
{
    [FunctionCallRenderer(typeof(SceneTools), nameof(SceneTools.FindSceneObjects))]
    class FindSceneObjectsFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Find Scene Objects";

        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<SceneTools.FindSceneObjectsOutput>();
            Add(FunctionCallUtils.CreateContentLabel(typedResult.Hierarchy));
        }
    }
}
