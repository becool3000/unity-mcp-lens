using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    interface IAssistantHostWindow
    {
        Action FocusLost { get; set; }
    }
}
