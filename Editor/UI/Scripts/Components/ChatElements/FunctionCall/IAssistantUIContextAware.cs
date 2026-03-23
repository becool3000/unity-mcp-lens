using Unity.AI.Assistant.UI.Editor.Scripts;

namespace Unity.AI.Assistant.Tools.Editor
{
    interface IAssistantUIContextAware
    {
        AssistantUIContext Context { get; set; }
    }
}
