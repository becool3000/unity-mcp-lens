using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.Tools.Editor
{
    [FunctionCallRenderer(typeof(AssetTools), nameof(AssetTools.FindProjectAssets))]
    class FindProjectAssetsFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Find Project Assets";

        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var typedResult = result.GetTypedResult<AssetTools.FindProjectAssetsOutput>();
            Add(FunctionCallUtils.CreateContentLabel(typedResult.Hierarchy));
        }
    }
}
