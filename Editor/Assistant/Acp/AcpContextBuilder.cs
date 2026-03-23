using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor.Context;

namespace Unity.AI.Assistant.Editor.Acp
{
    static class AcpContextBuilder
    {
        public static List<AcpContentBlock> BuildPromptContent(
            string userText,
            IReadOnlyCollection<LogData> consoleAttachments,
            IReadOnlyCollection<VirtualAttachment> virtualAttachments)
        {
            var content = new List<AcpContentBlock>();

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
                    content.Add(new AcpImageContent
                    {
                        MimeType = imageMeta.MimeType,
                        Data = attachment.Payload
                    });
                }
                else if (attachment.Type == "Document")
                {
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
                    // Handle generic text attachments
                    content.Add(new AcpTextContent { Text = attachment.Payload });
                }
            }

            // 3. Add user text last
            content.Add(new AcpTextContent { Text = userText });

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
