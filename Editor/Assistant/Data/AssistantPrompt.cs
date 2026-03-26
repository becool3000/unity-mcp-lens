using System.Collections.Generic;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using UnityEngine;

namespace Unity.AI.Assistant.Data
{
    class AssistantPrompt
    {
        public AssistantPrompt(string prompt, AssistantMode mode)
        {
            Value = prompt;
            Mode = mode;
        }

        public string Value;

        public AssistantMode Mode;

        public ModelConfiguration ModelConfiguration { get; set; }

        public readonly List<Object> ObjectAttachments = new();
        public readonly List<VirtualAttachment> VirtualAttachments = new();
        public readonly List<LogData> ConsoleAttachments = new();
    }

}
