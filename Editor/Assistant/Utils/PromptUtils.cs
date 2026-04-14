using System;
using System.Collections.Generic;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Utils;
using Unity.AI.Tracing;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class PromptUtils
    {
        public static EditorContextReport GetContextModel(int maxLength, AssistantPrompt prompt)
        {
            var startedAt = DateTime.UtcNow;
            var span = Trace.StartSpan("assistant.prompt_context.build", new TraceEventOptions
            {
                Category = "assistant",
                Data = new { maxLength }
            });

            try
            {
                // Initialize all context, if any context has changed, add it all
                var contextBuilder = new ContextBuilder();
                GetAttachedContextString(prompt, ref contextBuilder);

                var finalContext = contextBuilder.BuildContext(maxLength);
                var json = finalContext.ToJson();
                var jsonBytes = PayloadBudgeting.GetUtf8ByteCount(json);
                var attachedCount = finalContext.AttachedContext?.Count ?? 0;
                PayloadStats.RecordText(
                    "prompt_context",
                    "PromptUtils.GetContextModel",
                    json,
                    meta: new
                    {
                        attachedCount,
                        predictedChars = contextBuilder.PredictedLength,
                        maxLength
                    },
                    options: new PayloadStatOptions
                    {
                        EventKind = "prompt_context",
                        RepresentationKind = "full",
                        PayloadClass = "attached_context",
                        DurationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        ExtraFields = new
                        {
                            attachedCount,
                            predictedChars = contextBuilder.PredictedLength,
                            maxLength
                        }
                    });
                InternalLog.Log($"Final Context items={attachedCount}, chars={contextBuilder.PredictedLength}, bytes={jsonBytes}, sha256={PayloadBudgeting.ComputeSha256(json)}, preview:\n{PayloadBudgeting.CreateTextPreview(json, 8, 1024, out _)}");
                span.End(new { attachedCount, predictedChars = contextBuilder.PredictedLength, bytes = jsonBytes, success = true });
                return finalContext;
            }
            catch (Exception ex)
            {
                span.End(new { success = false, error = ex.GetType().Name, message = ex.Message });
                throw;
            }
        }

        /// <summary>
        /// Get the context string from the selected objects and selected console logs.
        /// </summary>
        /// <param name="prompt">The prompt to get the context string for</param>
        /// <param name="contextBuilder"> The context builder reference for temporary context string creation. </param>
        /// <param name="stopAtLimit">Stop processing context once the limit has reached</param>
        /// <returns></returns>
        public static void GetAttachedContextString(AssistantPrompt prompt, ref ContextBuilder contextBuilder, bool stopAtLimit = false)
        {
            if (prompt == null)
            {
                return;
            }

            // Grab any selected objects
            var attachment = AttachmentUtils.GetValidAttachment(prompt.ObjectAttachments);
            if (attachment.Count > 0)
            {
                // Avoid duplicated attachments when items overlap (e.g., folder + direct asset).
                var injectedInstanceIds = new HashSet<int>();
                var injectedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var currentObject in attachment)
                {
                    if (currentObject == null) continue;

                    if (FolderContextUtils.IsFolder(currentObject, out var folderPath))
                    {
                        var folderContext = new FolderContextSelection(folderPath);
                        contextBuilder.InjectContext(folderContext);

                        if (stopAtLimit && contextBuilder.PredictedLength > AssistantMessageSizeConstraints.ContextLimit)
                            return;

                        continue;
                    }

                    if (AssetDatabase.Contains(currentObject))
                    {
                        var assetPath = AssetDatabase.GetAssetPath(currentObject);
                        if (!string.IsNullOrEmpty(assetPath) && !injectedAssetPaths.Add(assetPath)) continue;
                    }

                    if (!injectedInstanceIds.Add(currentObject.GetInstanceID())) continue;

                    var directObjectContext = new UnityObjectContextSelection();
                    directObjectContext.SetTarget(currentObject);
                    contextBuilder.InjectContext(directObjectContext);

                    if (stopAtLimit && contextBuilder.PredictedLength > AssistantMessageSizeConstraints.ContextLimit)
                    {
                        break;
                    }
                }
            }

            if (prompt.VirtualAttachments.Count > 0)
            {
                foreach (var virtualAttachment in prompt.VirtualAttachments)
                {
                    contextBuilder.InjectContext(virtualAttachment.ToContextSelection());

                    if (stopAtLimit && contextBuilder.PredictedLength > AssistantMessageSizeConstraints.ContextLimit)
                    {
                        break;
                    }
                }
            }

            // Grab any console logs
            var consoleAttachments = prompt.ConsoleAttachments;
            if (consoleAttachments != null)
            {
                foreach (var currentLog in consoleAttachments)
                {
                    var consoleContext = new ConsoleContextSelection();
                    consoleContext.SetTarget(currentLog);
                    contextBuilder.InjectContext(consoleContext);

                    if (stopAtLimit && contextBuilder.PredictedLength > AssistantMessageSizeConstraints.ContextLimit)
                    {
                        break;
                    }
                }
            }
        }
    }
}
