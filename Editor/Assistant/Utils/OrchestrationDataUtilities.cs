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

                            // Store the correlation ID for linking with annotation mask
                            metaDataModel.Value = screenshotCorrelationId;

                            // Add the annotations mask as a separate context item if it exists
                            if (imageContextMetaData.Annotations != null && !string.IsNullOrEmpty(imageContextMetaData.Annotations.Base64))
                            {
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
                                        ImageContent = imageContextMetaData.Annotations.Base64,
                                        Payload = ""
                                    }
                                };
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

                if (bodyModel == null)
                {
                    // No specific body model has been made, use the default one
                    bodyModel = new ChatRequestV1.AttachedContextModel.TextBodyModel
                    {
                        Payload = contextItem.Payload,
                        Truncated = contextItem.Truncated
                    };
                }

                contextModel.Body = bodyModel;
                contextModel.Metadata = metaDataModel;
                contextList.Add(contextModel);
            }

            return contextList;
        }
    }
}
