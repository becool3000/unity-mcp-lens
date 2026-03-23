using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// The execution context of the tool
    /// </summary>
    readonly struct ToolExecutionContext
    {
        /// <summary>
        /// Information on the function call request
        /// </summary>
        public struct CallInfo
        {
            /// <summary>
            /// The tool ID
            /// </summary>
            public string FunctionId { get; }

            /// <summary>
            /// Call ID
            /// Note that this Call ID is not the same as the LLM tool call ID !
            /// </summary>
            public Guid CallId { get; }

            /// <summary>
            /// The raw parameters of the tool
            /// </summary>
            public JObject Parameters { get; }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="functionId">The tool ID</param>
            /// <param name="callId">The call ID</param>
            /// <param name="parameters">The raw parameters of the tool</param>
            public CallInfo(string functionId, Guid callId, JObject parameters)
            {
                FunctionId = functionId;
                CallId =  callId;
                Parameters = parameters;
            }
        }

        /// <summary>
        /// The conversation context
        /// </summary>
        public ConversationContext Conversation { get; }

        /// <summary>
        /// The tool call request parameters
        /// </summary>
        public CallInfo Call { get; }

        /// <summary>
        /// The permissions of this tool in the current context
        /// </summary>
        public ToolCallPermissions Permissions { get; }

        /// <summary>
        /// The user interactions of this tool in the current context
        /// </summary>
        public ToolCallInteractions Interactions { get; }

        internal ToolExecutionContext(ConversationContext conversationContext, CallInfo callInfo, ToolCallPermissions toolPermissions, ToolCallInteractions toolInteractions)
        {
            Conversation = conversationContext;
            Call = callInfo;
            Permissions = toolPermissions;
            Interactions = toolInteractions;
        }
    }
}
