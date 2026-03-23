using System;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class AssistantMessageUtils
    {
        public static void AddMessageForRole(this AssistantMessage message, string role, string content, bool isComplete)
        {
            switch (role.ToLower())
            {
                case Assistant.k_UserRole:
                    if (!isComplete)
                        throw new ArgumentException("User message must always be complete.");
                    message.Blocks.Add(new PromptBlock{Content = content});
                    break;

                case Assistant.k_AssistantRole:
                    message.Blocks.Add(new AnswerBlock{Content = content, IsComplete = isComplete});
                    break;

                default:
                    throw new NotImplementedException($"Role is not supported: {role}");
            }
        }
    }
}
