using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [FunctionCallRenderer(UITools.k_ValidateUIAssetFunctionId)]
    class ValidateUIAssetFunctionCallElement : ValidateUIAssetFunctionCallElementBase { }

    [FunctionCallRenderer(UITools.k_SaveAndValidateFunctionId)]
    class SaveAndValidateUIAssetFunctionCallElement : ValidateUIAssetFunctionCallElementBase { }
}
