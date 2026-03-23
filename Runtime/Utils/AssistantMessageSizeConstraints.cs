using System;
using Unity.AI.Assistant.Socket.Protocol;
using UnityEngine;

namespace Unity.AI.Assistant.Utils
{
    static class AssistantMessageSizeConstraints
    {
        const int k_MinPromptLimit = 4000;

        static int s_ContextLimit;
        static int s_ContextLimitOverride;

        static AssistantMessageSizeConstraints()
        {
            // "default" value until we hear from the server
            SetMessageSizeLimit(34000);
        }

        // Note: This limit gets overridden by the backend, this is just an initial fallback
        internal static int MessageLimit { get; private set;}

        internal static int MessageLimitRaw { get; private set; }

        internal static int ContextLimit => s_ContextLimitOverride == 0 ? s_ContextLimit : s_ContextLimitOverride;
        internal static int PromptLimit { get; private set; }
        internal static int FeedbackLimit { get; private set; }

        public static int GetDynamicContextLimitForPrompt(string prompt)
        {
            return Mathf.Min(MessageLimit - prompt.Length, ContextLimit);
        }

        public static int GetMessageSizeLimitForV1Request()
        {
            return MessageLimit - ProtocolOverheadMeasures.MessageV1Overhead;
        }

        public static void SetMessageSizeLimit(int newLimit)
        {
            // POC Hack, no limit!!!
            newLimit = int.MaxValue;

            if (newLimit <= 0)
            {
                // Invalid value, probably a unit test
                return;
            }

            // These deductions are arbitrary but we need to leave room, the server limit is considered a hard-ceiling of data

            // 5% deducted for overhead of JSON headers during send
            MessageLimitRaw = newLimit;
            MessageLimit = (int)(newLimit * 0.95f);

            // Prompt limit is either 10% of message limit or the default up top, whichever is higher
            PromptLimit = Mathf.Max(k_MinPromptLimit, (int)(newLimit * 0.1f));
            FeedbackLimit = PromptLimit;

            // 10% deducted for context to make room for prompt and more json overhead
            s_ContextLimit = MessageLimit - PromptLimit;
        }

        public static void SetContextLimitOverride(int value)
        {
            s_ContextLimitOverride = value;
        }

        public static void RevertContextLimitOverride()
        {
            s_ContextLimitOverride = 0;
        }
    }
}
