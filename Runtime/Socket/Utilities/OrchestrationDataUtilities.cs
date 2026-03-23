using System;
using System.Collections.Generic;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;

namespace Unity.AI.Assistant.Socket.Utilities
{
    static class OrchestrationDataUtilities
    {
        /// <summary>
        /// Delegate for converting EditorContextReport to ChatRequestV1.AttachedContextModel list
        /// </summary>
        internal static Func<EditorContextReport, List<ChatRequestV1.AttachedContextModel>> FromEditorContextReportDelegate { get; set; }

        /// <summary>
        /// Converts EditorContextReport to attached context models. Delegates to Editor implementation.
        /// </summary>
        internal static List<ChatRequestV1.AttachedContextModel> FromEditorContextReport(
            EditorContextReport editorContextReport)
        {
            if (FromEditorContextReportDelegate != null)
            {
                return FromEditorContextReportDelegate(editorContextReport);
            }

            // Fallback - return empty list if no Editor implementation available
            return new List<ChatRequestV1.AttachedContextModel>();
        }
    }
}
