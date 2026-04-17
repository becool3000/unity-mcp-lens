using Becool.UnityMcpLens.Editor.Models;
using Becool.UnityMcpLens.Editor.Security;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Extension methods for working with ConnectionIdentity and connection records.
    /// </summary>
    static class ConnectionIdentityExtensions
    {
        /// <summary>
        /// Get structured display parts for a connection (title and process info separately).
        /// Returns plain text without any markup for security.
        /// Title should be styled bold, processInfo should be normal weight.
        /// </summary>
        public static (string title, string processInfo) GetIdentityDisplayParts(this ConnectionRecord record)
        {
            if (record?.Info == null)
                return ("Unknown Connection", null);

            var clientInfo = record.Info.ClientInfo;
            var client = record.Info.Client;

            // Get the title - prefer ClientInfo.Title, fallback to ClientInfo.Name
            // Sanitize by using plain text only (no HTML/rich text markup)
            string title = null;
            if (clientInfo != null)
            {
                title = !string.IsNullOrEmpty(clientInfo.Title) ? clientInfo.Title : clientInfo.Name;
            }

            // If no title from ClientInfo, use process name
            if (string.IsNullOrEmpty(title))
            {
                title = client?.ProcessName ?? "Unknown";
            }

            // Get the process/publisher info (normal text)
            string processInfo = null;
            var processName = client?.ProcessName;

            // If client is signed, show publisher
            if (client?.Identity != null && client.Identity.IsSigned && client.Identity.SignatureValid)
            {
                var publisherName = client.Identity.GetDisplayName();
                processInfo = $"{processName} (by {publisherName})";
            }
            else if (!string.IsNullOrEmpty(processName))
            {
                // Only show process name if it's different from title
                if (title != processName)
                {
                    processInfo = processName;
                }
            }

            return (title, processInfo);
        }

        /// <summary>
        /// Get a display-friendly name for a connection based on its identity.
        /// Returns plain text without any markup for security.
        /// Format: Title process (by publisher)
        /// </summary>
        public static string GetIdentityDisplayName(this ConnectionRecord record)
        {
            var (title, processInfo) = GetIdentityDisplayParts(record);

            // Combine as plain text (no markup for security)
            if (!string.IsNullOrEmpty(processInfo))
            {
                return $"{title} {processInfo}";
            }

            return title;
        }

        /// <summary>
        /// Get a user-friendly status description.
        /// </summary>
        public static string GetStatusDescription(this ConnectionRecord record)
        {
            if (record == null) return "Unknown";
            return record.Status switch
            {
                ValidationStatus.CapacityLimit => "Capacity limit",
                _ => record.Status.ToString()
            };
        }
    }
}
