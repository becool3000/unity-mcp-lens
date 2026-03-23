using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Checkpoint;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Editor
{
    static class ChatResponseUtils
    {
        internal const string k_NoMessageBlocksError = "Cannot process tool result: no message blocks exist. This may indicate incomplete message recovery after a domain reload.";
        internal const string k_NoMatchingToolCallBlockError = "Cannot process tool result: no matching tool call block found for call ID '{0}'.";


        enum ContentType
        {
            None,
            Thought,
            ToolCall,
            ToolResult
        }

        struct BlockContent
        {
            public ContentType Tag;
            public string Content;
        }

        struct TagMapping
        {
            public ContentType Tag;
            public string Prefix;
            public string Suffix;
        }

        static TagMapping[] s_TagMappings = new[]
        {
            new TagMapping { Tag = ContentType.Thought, Prefix = "<THOUGHT>", Suffix = "</THOUGHT>" },
            new TagMapping { Tag = ContentType.ToolCall, Prefix = "<TOOL_CALL>", Suffix = "</TOOL_CALL>" },
            new TagMapping { Tag = ContentType.ToolResult, Prefix = "<TOOL_RESULT>", Suffix = "</TOOL_RESULT>" }
        };

        [Serializable]
        struct ThoughtData
        {
            [JsonProperty("content")]
            public string Content;

            [JsonProperty("agent", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
            public string Agent;
        }

        [Serializable]
        struct ToolCallData
        {
            [JsonProperty("tool_id")]
            public string ToolId;

            [JsonProperty("call_id")]
            public string CallId;

            [JsonProperty("args")]
            public JObject Args;

            [JsonProperty("agent", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
            public string Agent;
        }

        [Serializable]
        struct ToolResultData
        {
            [Serializable]
            public struct Output
            {
                [JsonProperty("raw_output")]
                public JToken RawOutput;
            }

            [JsonProperty("tool_id")]
            public string ToolId;

            [JsonProperty("call_id")]
            public string CallId;

            [JsonProperty("result")]
            public string Result;

            [JsonProperty("error")]
            public string Error;

            [JsonProperty("agent", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
            public string Agent;
        }

        public static void Parse(this ChatResponseFragment fragment, AssistantConversationId conversationId, AssistantMessage message, StringBuilder responseBuilder)
        {
            if(message.Id.FragmentId != fragment.Id)
            {
                var oldFragmentId = message.Id.FragmentId;
                message.Id = new AssistantMessageId(conversationId, fragment.Id, AssistantMessageIdType.External);

                if (!string.IsNullOrEmpty(oldFragmentId))
                {
                    if (AssistantCheckpoints.HasPendingCheckpoint(conversationId, oldFragmentId))
                    {
                        _ = AssistantCheckpoints.CompletePendingCheckpointAsync(conversationId, oldFragmentId, fragment.Id);
                    }
                    else if (AssistantCheckpoints.HasCheckpointForMessage(conversationId, oldFragmentId))
                    {
                        _ = UpdateCheckpointTagAsync(conversationId, oldFragmentId, fragment.Id);
                    }
                }
            }

            // Make sure we properly close the response on the last fragment
            if (string.IsNullOrEmpty(fragment.Fragment) && fragment.IsLastFragment)
            {
                CloseLastResponse(message, responseBuilder);
                return;
            }

            using var pooledBlockContents = ListPool<BlockContent>.Get(out var blockContents);
            ParseTags(fragment.Fragment, blockContents);

            foreach (var blockContent in blockContents)
            {
                switch (blockContent.Tag)
                {
                    case ContentType.None:
                        HandleResponse(blockContent.Content, fragment.IsLastFragment, message, responseBuilder);
                        break;

                    case ContentType.Thought:
                        CloseLastResponse(message, responseBuilder);
                        HandleThought(blockContent.Content, message);
                        break;

                    case ContentType.ToolCall:
                        CloseLastResponse(message, responseBuilder);
                        HandleFunctionCall(blockContent.Content, message);
                        break;

                    case ContentType.ToolResult:
                        CloseLastResponse(message, responseBuilder);
                        HandleFunctionResult(blockContent.Content, message);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException($"Unsupported content type: {blockContent.Tag}");
                }
            }
        }

        static void ParseTags(string text, List<BlockContent> blockContents)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var span = text.AsSpan();
            var pos = 0;
            var length = span.Length;
            var nonTagStart = 0;

            while (pos < length)
            {
                TagMapping? matchedTag = null;

                // Check for exact prefix match
                foreach (var mapping in s_TagMappings)
                {
                    if (pos + mapping.Prefix.Length <= length && span.Slice(pos, mapping.Prefix.Length).SequenceEqual(mapping.Prefix.AsSpan()))
                    {
                        matchedTag = mapping;
                        break;
                    }
                }

                if (matchedTag == null)
                {
                    pos++;
                    continue;
                }

                // Flush non-tagged text (skip empty ones)
                if (pos > nonTagStart)
                {
                    var nonTagSpan = span.Slice(nonTagStart, pos - nonTagStart);
                    if (nonTagSpan.Length > 0)
                    {
                        blockContents.Add(new BlockContent
                        {
                            Tag = ContentType.None,
                            Content = nonTagSpan.ToString()
                        });
                    }
                }

                var tagContentStart = pos + matchedTag.Value.Prefix.Length;

                // Exact match for suffix
                var closeTagStart = span.Slice(tagContentStart).IndexOf(matchedTag.Value.Suffix.AsSpan());
                if (closeTagStart == -1)
                {
                    var remaining = span.Slice(pos);
                    if (remaining.Length > 0)
                    {
                        blockContents.Add(new BlockContent
                        {
                            Tag = ContentType.None,
                            Content = remaining.ToString()
                        });
                    }
                    return;
                }

                closeTagStart += tagContentStart;

                var tagContent = span.Slice(tagContentStart, closeTagStart - tagContentStart);
                blockContents.Add(new BlockContent
                {
                    Tag = matchedTag.Value.Tag,
                    Content = tagContent.ToString()
                });

                pos = closeTagStart + matchedTag.Value.Suffix.Length;
                nonTagStart = pos;
            }

            // Flush remaining non-tagged text
            if (pos > nonTagStart)
            {
                var remaining = span.Slice(nonTagStart, pos - nonTagStart);
                if (remaining.Length > 0)
                {
                    blockContents.Add(new BlockContent
                    {
                        Tag = ContentType.None,
                        Content = remaining.ToString()
                    });
                }
            }
        }

        static void CloseLastResponse(AssistantMessage message, StringBuilder responseBuilder)
        {
            if (message.Blocks.Count == 0)
                return;

            if (message.Blocks[^1] is not AnswerBlock responseBlock)
                return;

            // Complete any pending response and clear buffer
            responseBlock.Content = responseBuilder.ToString();
            responseBlock.IsComplete = true;
            responseBuilder.Clear();
        }

        static void HandleResponse(string content, bool isComplete, AssistantMessage message, StringBuilder responseBuilder)
        {
            var responseBlock =  message.Blocks.Count > 0 ? message.Blocks[^1] as AnswerBlock : null;
            if (responseBlock == null)
            {
                responseBlock = new AnswerBlock();
                message.Blocks.Add(responseBlock);
            }

            if (!string.IsNullOrEmpty(content))
                responseBuilder.Append(content);

            responseBlock.Content = responseBuilder.ToString();
            responseBlock.IsComplete = isComplete;
            message.IsComplete =  isComplete;
        }

        static void HandleThought(string content, AssistantMessage message)
        {
            var thoughtData = AssistantJsonHelper.Deserialize<ThoughtData>(content);
            var thoughtBlock = new ThoughtBlock{ Content = thoughtData.Content };
            message.Blocks.Add(thoughtBlock);
        }

        static void HandleFunctionCall(string content, AssistantMessage message)
        {
            var callData = AssistantJsonHelper.Deserialize<ToolCallData>(content);
            var call = new AssistantFunctionCall
            {
                CallId = new Guid(callData.CallId),
                FunctionId = callData.ToolId,
                Parameters = callData.Args,
                Result = default
            };

            var functionCallBlock = new FunctionCallBlock{ Call = call };
            message.Blocks.Add(functionCallBlock);
        }

        static void HandleFunctionResult(string content, AssistantMessage message)
        {
            if (message.Blocks.Count == 0)
                throw new Exception(k_NoMessageBlocksError);

            var resultData = AssistantJsonHelper.Deserialize<ToolResultData>(content);
            var outputData = AssistantJsonHelper.Deserialize<ToolResultData.Output>(resultData.Result);

            var callId = new Guid(resultData.CallId);
            if (!TryFindFunctionCallBlock(message, callId, out var functionCallBlock))
                throw new Exception(string.Format(k_NoMatchingToolCallBlockError, callId));

            functionCallBlock.Call.Result = string.IsNullOrEmpty(resultData.Error) ?
                IFunctionCaller.CallResult.SuccessfulResult(outputData.RawOutput) :
                IFunctionCaller.CallResult.FailedResult(resultData.Error);
        }

        static bool TryFindFunctionCallBlock(AssistantMessage message, Guid callId, out FunctionCallBlock functionCallBlock)
        {
            functionCallBlock = null;

            if (message.Blocks == null || message.Blocks.Count == 0)
                return false;

            // Go backward as the matching block is more likely to be near the end
            for (var i = message.Blocks.Count - 1; i >= 0; i--)
            {
                if (message.Blocks[i] is FunctionCallBlock fcb && fcb.Call.CallId == callId)
                {
                    functionCallBlock = fcb;
                    return true;
                }
            }

            return false;
        }

        static async Task UpdateCheckpointTagAsync(AssistantConversationId conversationId, string oldFragmentId, string newFragmentId)
        {
            var hash = await AssistantCheckpoints.GetCheckpointForMessageAsync(conversationId, oldFragmentId);
            if (!string.IsNullOrEmpty(hash))
            {
                await AssistantCheckpoints.UpdateTagAsync(hash, conversationId, oldFragmentId, newFragmentId);
            }
        }
    }
}
