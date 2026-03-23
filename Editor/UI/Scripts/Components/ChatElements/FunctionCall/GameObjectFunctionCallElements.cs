using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [FunctionCallRenderer(CreateGameObjectTool.k_FunctionId)]
    class CreateGameObjectFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Create GameObject";
    }

    [FunctionCallRenderer(ModifyGameObjectTool.k_FunctionId)]
    class ModifyGameObjectFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Modify GameObject";
    }

    [FunctionCallRenderer(RemoveGameObjectTool.k_FunctionId)]
    class RemoveGameObjectFunctionCallElement : DefaultFunctionCallRenderer
    {
        public override string Title => "Remove GameObject";
    }
}
