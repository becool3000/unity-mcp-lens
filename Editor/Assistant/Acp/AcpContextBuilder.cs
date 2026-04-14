using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Utils;
using Unity.AI.Tracing;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Acp
{
    static class AcpContextBuilder
    {
        public static List<AcpContentBlock> BuildPromptContent(
            string userText,
            IReadOnlyCollection<LogData> consoleAttachments,
            IReadOnlyCollection<VirtualAttachment> virtualAttachments)
        {
            var startedAt = System.DateTime.UtcNow;
            var span = Trace.StartSpan("assistant.acp_prompt.build", new TraceEventOptions
            {
                Category = "assistant",
                Data = new
                {
                    consoleCount = consoleAttachments?.Count ?? 0,
                    virtualCount = virtualAttachments?.Count ?? 0
                }
            });
            var content = new List<AcpContentBlock>();
            var imageCount = 0;
            var documentCount = 0;
            var textAttachmentCount = 0;

            // 1. Add console logs as formatted text
            if (consoleAttachments?.Count > 0)
            {
                var consoleText = FormatConsoleLogs(consoleAttachments);
                content.Add(new AcpTextContent { Text = consoleText });
            }

            // 2. Add virtual attachments (images and text documents)
            foreach (var attachment in virtualAttachments ?? Enumerable.Empty<VirtualAttachment>())
            {
                if (attachment.Metadata is ImageContextMetaData imageMeta)
                {
                    imageCount++;
                    content.Add(new AcpImageContent
                    {
                        MimeType = imageMeta.MimeType,
                        Data = attachment.Payload
                    });
                }
                else if (attachment.Type == "Document")
                {
                    documentCount++;
                    content.Add(new AcpResourceContent
                    {
                        Resource = new AcpResourceData
                        {
                            Text = attachment.Payload,
                            Uri = $"unity://{attachment.DisplayName}",
                            MimeType = "text/markdown"
                        }
                    });
                }
                else if (attachment.Type == "Text")
                {
                    textAttachmentCount++;
                    // Handle generic text attachments
                    content.Add(new AcpTextContent { Text = attachment.Payload });
                }
            }

            // 3. Add user text last
            content.Add(new AcpTextContent { Text = userText });

            try
            {
                var contentJson = JsonConvert.SerializeObject(content, Formatting.None);
                PayloadStats.RecordText(
                    "acp_prompt",
                    "AcpContextBuilder.BuildPromptContent",
                    contentJson,
                    meta: new
                    {
                        blockCount = content.Count,
                        consoleCount = consoleAttachments?.Count ?? 0,
                        virtualCount = virtualAttachments?.Count ?? 0
                    },
                    options: new PayloadStatOptions
                    {
                        EventKind = "acp_prompt",
                        RepresentationKind = imageCount > 0 ? "mixed" : "full",
                        PayloadClass = "acp_prompt_content",
                        DurationMs = (int)(System.DateTime.UtcNow - startedAt).TotalMilliseconds,
                        ExtraFields = new
                        {
                            blockCount = content.Count,
                            consoleCount = consoleAttachments?.Count ?? 0,
                            virtualCount = virtualAttachments?.Count ?? 0,
                            imageCount,
                            documentCount,
                            textAttachmentCount
                        }
                    });
            }
            catch
            {
                // Best effort metrics only.
            }

            span.End(new
            {
                success = true,
                durationMs = (int)(System.DateTime.UtcNow - startedAt).TotalMilliseconds,
                blockCount = content.Count,
                imageCount,
                documentCount,
                textAttachmentCount
            });

            return content;
        }

        static string FormatConsoleLogs(IEnumerable<LogData> logs)
        {
            // Going for a YAML format
            var sb = new StringBuilder();
            sb.AppendLine("Console Logs:");
            foreach (var log in logs)
            {
                sb.AppendLine($"- type: {log.Type}");
                sb.AppendLine("  message: |");
                foreach (var line in log.Message.Split('\n'))
                {
                    sb.AppendLine($"    {line}");
                }

                if (!string.IsNullOrEmpty(log.File))
                {
                    sb.AppendLine($"  file: {log.File}");
                    sb.AppendLine($"  line: {log.Line}");
                }
            }
            return sb.ToString();
        }
    }
}
