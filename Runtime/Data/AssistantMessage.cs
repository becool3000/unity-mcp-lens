using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Data
{
    [Serializable]
    class AssistantMessage
    {
        /// <summary>
        /// Indicates that this is an error message and should be displayed as such
        /// </summary>
        public bool IsError;

        /// <summary>
        /// Indicates that the message is complete and no longer streaming in
        /// </summary>
        public bool IsComplete;

        public AssistantMessageId Id;
        public string Role;
        public AssistantContextEntry[] Context;
        public List<IAssistantMessageBlock> Blocks = new();
        public long RevertedTimeStamp;
        public long Timestamp;
        public int MessageIndex;

        public static AssistantMessage AsError(AssistantMessageId id, string message)
        {
            var msg =  new AssistantMessage()
            {
                Id = id,
                IsError = true,
                IsComplete = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            msg.Blocks.Add(new ErrorBlock { Error = message });
            return msg;
        }

        /// <summary>
        /// Serializes the message to a JObject for JSON persistence.
        /// </summary>
        public JObject ToJson()
        {
            var json = new JObject
            {
                ["role"] = Role,
                ["isComplete"] = IsComplete,
                ["timestamp"] = Timestamp,
                ["blocks"] = SerializeBlocks()
            };

            return json;
        }

        /// <summary>
        /// Deserializes a message from a JObject.
        /// </summary>
        public static AssistantMessage FromJson(JObject json)
        {
            if (json == null) return null;

            var message = new AssistantMessage
            {
                Role = json["role"]?.ToString(),
                IsComplete = json["isComplete"]?.Value<bool>() ?? false,
                Timestamp = json["timestamp"]?.Value<long>() ?? 0
            };

            var blocksArray = json["blocks"] as JArray;
            if (blocksArray != null)
            {
                foreach (var blockToken in blocksArray)
                {
                    var blockObj = blockToken as JObject;
                    if (blockObj != null)
                    {
                        var block = DeserializeBlock(blockObj);
                        if (block != null)
                        {
                            message.Blocks.Add(block);
                        }
                    }
                }
            }

            return message;
        }

        private JArray SerializeBlocks()
        {
            var array = new JArray();
            foreach (var block in Blocks)
            {
                var serialized = SerializeBlock(block);
                if (serialized != null)
                {
                    array.Add(serialized);
                }
            }
            return array;
        }

        private static JObject SerializeBlock(IAssistantMessageBlock block)
        {
            switch (block)
            {
                case ThoughtBlock thought:
                    return new JObject
                    {
                        ["type"] = "thought",
                        ["content"] = thought.Content
                    };

                case PromptBlock prompt:
                    return new JObject
                    {
                        ["type"] = "prompt",
                        ["content"] = prompt.Content
                    };

                case AnswerBlock response:
                    return new JObject
                    {
                        ["type"] = "response",
                        ["content"] = response.Content,
                        ["isComplete"] = response.IsComplete
                    };

                case ErrorBlock error:
                    return new JObject
                    {
                        ["type"] = "error",
                        ["content"] = error.Error
                    };

                case FunctionCallBlock functionCall:
                    // Serialize function call details
                    var funcJson = new JObject
                    {
                        ["type"] = "function_call",
                        ["content"] = new JObject
                        {
                            ["id"] = functionCall.Call.CallId,
                            ["functionId"] = functionCall.Call.FunctionId,
                            ["parameters"] = functionCall.Call.Parameters,
                            ["result"] = new JObject
                            {
                                ["success"] = functionCall.Call.Result.HasFunctionCallSucceeded,
                                ["result"] = functionCall.Call.Result.Result
                            }
                        }
                    };

                    return funcJson;

                case AcpToolCallStorageBlock toolCallStorage:
                    // ACP tool call - store the raw JSON data
                    return new JObject
                    {
                        ["type"] = "tool_call",
                        ["content"] = toolCallStorage.ToolCallData
                    };

                case AcpPlanStorageBlock planStorage:
                    // ACP plan update - store the raw JSON data
                    return new JObject
                    {
                        ["type"] = "plan",
                        ["content"] = planStorage.PlanData
                    };

                default:
                    // For unknown types, skip serialization
                    return null;
            }
        }

        private static IAssistantMessageBlock DeserializeBlock(JObject blockObj)
        {
            var type = blockObj["type"]?.ToString();
            var content = blockObj["content"];

            switch (type)
            {
                case "thought":
                    return new ThoughtBlock { Content = content?.ToString() };

                case "prompt":
                    return new PromptBlock { Content = content?.ToString() };

                case "response":
                    return new AnswerBlock
                    {
                        Content = content?.ToString(),
                        IsComplete = blockObj["isComplete"]?.Value<bool>() ?? false
                    };

                case "error":
                    return new ErrorBlock { Error = content?.ToString() };

                case "function_call":
                    var call = new AssistantFunctionCall();
                    if (content is JObject funcObj)
                    {
                        call.CallId = new Guid(funcObj["id"]?.ToString());
                        call.FunctionId = funcObj["functionId"]?.ToString();
                        call.Parameters = (JObject)funcObj["parameters"];

                        var resultObj = funcObj["result"];
                        var successful = ((bool)resultObj["success"]);
                        call.Result = successful ? Backend.IFunctionCaller.CallResult.SuccessfulResult(resultObj["result"]) : Backend.IFunctionCaller.CallResult.FailedResult(resultObj["result"].ToString()); 
                    }
                    return new FunctionCallBlock { Call = call };

                case "tool_call":
                    // ACP tool call - parse the JSON data
                    var toolCallJson = content?.ToString();
                    if (!string.IsNullOrEmpty(toolCallJson))
                    {
                        try
                        {
                            var toolCallData = JObject.Parse(toolCallJson);
                            return new AcpToolCallStorageBlock { ToolCallData = toolCallData };
                        }
                        catch
                        {
                            // Failed to parse, skip this block
                            return null;
                        }
                    }
                    return null;

                case "plan":
                    // ACP plan update - parse the JSON data
                    var planJson = content?.ToString();
                    if (!string.IsNullOrEmpty(planJson))
                    {
                        try
                        {
                            var planData = JObject.Parse(planJson);
                            return new AcpPlanStorageBlock { PlanData = planData };
                        }
                        catch
                        {
                            // Failed to parse, skip this block
                            return null;
                        }
                    }
                    return null;

                default:
                    // Unknown block types are skipped during deserialization
                    return null;
            }
        }
    }
}
