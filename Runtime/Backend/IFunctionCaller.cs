using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Socket.Workflows.Chat;

namespace Unity.AI.Assistant.Backend
{
    /// <summary>
    /// Handles the calling of functions in the AI Assistant system via string arguments provided by LLM. Requires
    /// access to system functions identifiable by a functionID. LLMs provide function arguments as arbitrary JSON
    /// objects, the shape of which depends on the purpose of the function being called.
    /// </summary>
    interface IFunctionCaller
    {
        /// <summary>
        /// Responsible for calling and constructing a response by looking up a function and calling it using
        /// parameters provided by an LLM in JObject format.
        /// </summary>
        /// <param name="functionId">The key used to look up the function</param>
        /// <param name="functionParameters">The JSON Object that contains the functions parameters</param>
        /// <param name="callId">The Call ID</param>
        /// <param name="token">A cancellation token</param>
        /// <returns></returns>
        void CallByLLM(IChatWorkflow workFlow, string functionId, JObject functionParameters, Guid callId, CancellationToken token);

        public struct CallResult
        {
            public bool IsDone { get; private set; } // will default to false
            public bool HasFunctionCallSucceeded { get; private set; }
            public JToken Result { get; private set; }

            public static CallResult SuccessfulResult(JToken response) => new()
            {
                HasFunctionCallSucceeded = true,
                Result = response,
                IsDone = true
            };

            public static CallResult FailedResult(string error) => new()
            {
                HasFunctionCallSucceeded = false,
                Result = new JValue(error),
                IsDone = true
            };
        }
    }
}
