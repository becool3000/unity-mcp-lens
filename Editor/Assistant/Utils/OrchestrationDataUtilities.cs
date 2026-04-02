using System.Collections.Generic;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class OrchestrationDataUtilities
    {
        internal static List<ChatRequestV1.AttachedContextModel> FromEditorContextReport(
            EditorContextReport editorContextReport)
        {
            var contextList = new List<ChatRequestV1.AttachedContextModel>();

            if (editorContextReport?.AttachedContext == null)
                return contextList;

            // Go through each context item
            foreach (var contextItem in editorContextReport.AttachedContext)
            {
                var contextModel = new ChatRequestV1.AttachedContextModel();
                var metaDataModel = new ChatRequestV1.AttachedContextModel.MetadataModel();
                ChatRequestV1.AttachedContextModel.BodyModel bodyModel = null;
                var payloadHash = contextItem.PayloadHash ?? PayloadBudgeting.ComputeSha256(contextItem.Payload ?? string.Empty);
                contextModel.AdditionalProperties["context_hash"] = payloadHash;

                var selection = contextItem.Context as IContextSelection;
                if (selection == null)
                {
                    InternalLog.LogWarning("Context is not an IContextSelection.");
                    continue;
                }

                // There is technically two more of these, ContextSelection and StaticDatabase
                // They don't show up in these scenarios
                switch (selection)
                {
                    case UnityObjectContextSelection objectContext:
                    {
                        var contextEntry = objectContext.Target.GetContextEntry();

                        metaDataModel.DisplayValue = contextEntry.DisplayValue;
                        metaDataModel.Value = contextEntry.Value;
                        metaDataModel.ValueType = contextEntry.ValueType;
                        metaDataModel.ValueIndex = contextEntry.ValueIndex;
                        metaDataModel.EntryType = (int)contextEntry.EntryType;

                        break;
                    }

                    case VirtualContextSelection virtualContext:
                    {
                        if (virtualContext.Metadata is ImageContextMetaData imageContextMetaData)
                        {
                            // Generate a unique correlation ID for this screenshot to link with its annotation mask
                            var screenshotCorrelationId = System.Guid.NewGuid().ToString();

                            bodyModel = new ChatRequestV1.AttachedContextModel.ImageBodyModel
                            {
                                Category = imageContextMetaData.Category.ToString(),
                                Format = imageContextMetaData.Format,
                                Width = imageContextMetaData.Width,
                                Height = imageContextMetaData.Height,
                                ImageContent = contextItem.Payload,
                                Payload = "",
                            };
                            contextModel.AdditionalProperties["blob_ref"] = payloadHash;

                            // Store the correlation ID for linking with annotation mask
                            metaDataModel.Value = screenshotCorrelationId;

                            // Add the annotations mask as a separate context item if it exists
                            if (imageContextMetaData.Annotations != null && !string.IsNullOrEmpty(imageContextMetaData.Annotations.Base64))
                            {
                                var annotationHash = PayloadBudgeting.ComputeSha256(imageContextMetaData.Annotations.Base64);
                                var annotationKey = $"virtual-annotation|{virtualContext.DisplayValue}|{annotationHash}";
                                var shouldSendAnnotation = StableContextCache.ShouldSendFull(annotationKey, annotationHash);
                                var maskContextModel = new ChatRequestV1.AttachedContextModel
                                {
                                    Metadata = new ChatRequestV1.AttachedContextModel.MetadataModel
                                    {
                                        DisplayValue = $"Annotations Mask for {virtualContext.DisplayValue}",
                                        Value = screenshotCorrelationId,  // Same ID to correlate with source screenshot
                                        ValueType = virtualContext.PayloadType,
                                        ValueIndex = contextList.Count,
                                        EntryType = (int)AssistantContextType.Virtual
                                    },
                                    Body = new ChatRequestV1.AttachedContextModel.ImageBodyModel
                                    {
                                        Category = imageContextMetaData.Category.ToString(),
                                        Format = imageContextMetaData.Format,
                                        Width = imageContextMetaData.Annotations.Width > 0 ? imageContextMetaData.Annotations.Width : imageContextMetaData.Width,
                                        Height = imageContextMetaData.Annotations.Height > 0 ? imageContextMetaData.Annotations.Height : imageContextMetaData.Height,
                                        ImageContent = shouldSendAnnotation ? imageContextMetaData.Annotations.Base64 : string.Empty,
                                        Payload = shouldSendAnnotation ? string.Empty : $"Annotation mask unchanged from an earlier request in this conversation. sha256={annotationHash}."
                                    }
                                };
                                maskContextModel.AdditionalProperties["context_hash"] = annotationHash;
                                maskContextModel.AdditionalProperties["stable_key"] = annotationKey;
                                maskContextModel.AdditionalProperties["blob_ref"] = annotationHash;
                                if (!shouldSendAnnotation)
                                    maskContextModel.AdditionalProperties["cached"] = true;
                                contextList.Add(maskContextModel);
                            }
                        }
                        else
                        {
                            metaDataModel.Value = "";
                        }

                        metaDataModel.DisplayValue = virtualContext.DisplayValue;
                        metaDataModel.ValueType = virtualContext.PayloadType;
                        metaDataModel.ValueIndex = -1;
                        metaDataModel.EntryType = (int)AssistantContextType.Virtual;

                        break;
                    }

                    case ConsoleContextSelection consoleContext:
                    {
                        var contextEntry = consoleContext.Target.GetValueOrDefault().GetContextEntry();

                        metaDataModel.DisplayValue = contextEntry.DisplayValue;
                        metaDataModel.Value = contextEntry.Value;
                        metaDataModel.ValueType = contextEntry.ValueType;
                        metaDataModel.ValueIndex = contextEntry.ValueIndex;
                        metaDataModel.EntryType = (int)contextEntry.EntryType;

                        break;
                    }

                    case FolderContextSelection folderContext:
                    {
                        metaDataModel.DisplayValue = folderContext.FolderPath;
                        metaDataModel.Value = folderContext.FolderPath;
                        metaDataModel.ValueType = "Folder";
                        metaDataModel.ValueIndex = -1;
                        metaDataModel.EntryType = (int)AssistantContextType.Virtual;

                        break;
                    }

                    default:
                    {
                        InternalLog.LogWarning("Context is not attached object or console - skipping.");
                        continue;
                    }
                }

                var stableKey = contextItem.StableCacheKey ?? $"{selection.ContextType}|{selection.TargetName}|{metaDataModel.ValueType}|{metaDataModel.Value}";
                var shouldSendFull = StableContextCache.ShouldSendFull(stableKey, payloadHash);

                if (bodyModel == null)
                {
                    // No specific body model has been made, use the default one
                    bodyModel = new ChatRequestV1.AttachedContextModel.TextBodyModel
                    {
                        Payload = shouldSendFull
                            ? contextItem.Payload
                            : $"Context unchanged from an earlier request in this conversation. sha256={payloadHash}. Re-fetch only if more detail is needed.",
                        Truncated = contextItem.Truncated || !shouldSendFull
                    };
                }
                else if (bodyModel is ChatRequestV1.AttachedContextModel.ImageBodyModel imageBody && !shouldSendFull)
                {
                    imageBody.ImageContent = string.Empty;
                    imageBody.Payload = $"Image attachment unchanged from an earlier request in this conversation. sha256={payloadHash}.";
                    contextModel.AdditionalProperties["cached"] = true;
                }

                contextModel.Body = bodyModel;
                contextModel.Metadata = metaDataModel;
                contextModel.AdditionalProperties["stable_key"] = stableKey;
                if (bodyModel is ChatRequestV1.AttachedContextModel.ImageBodyModel)
                    contextModel.AdditionalProperties["blob_ref"] = payloadHash;
                if (!shouldSendFull)
                {
                    contextItem.IsCacheReference = true;
                    contextModel.AdditionalProperties["cached"] = true;
                }
                contextList.Add(contextModel);
            }

            return contextList;
        }
    }
}
