using System;
using System.Text;
using Unity.AI.Assistant.Editor.Utils;

namespace Unity.AI.Assistant.Editor.Context
{
    internal class FolderContextSelection : IContextSelection
    {
        string m_Payload;

        public FolderContextSelection(string folderPath) { FolderPath = folderPath; }
        internal string FolderPath { get; }

        string IContextSelection.Classifier => "Folder";
        string IContextSelection.Description => $"Folder: {FolderPath}";
        string IContextSelection.Payload => m_Payload ??= BuildPayload();
        string IContextSelection.DownsizedPayload => BuildPayload(maxEntries: 20, includeGuids: false);
        string IContextSelection.ContextType => "Folder";
        string IContextSelection.TargetName => FolderPath;
        bool? IContextSelection.Truncated => null;

        public bool Equals(IContextSelection other) =>
            other is FolderContextSelection otherSelection &&
            string.Equals(otherSelection.FolderPath, FolderPath, StringComparison.OrdinalIgnoreCase);

        string BuildPayload(int maxEntries = int.MaxValue, bool includeGuids = true)
        {
            var entries = FolderContextUtils.EnumerateFolderAssetInfos(FolderPath);
            var builder = new StringBuilder();
            builder.AppendLine($"Folder: {FolderPath}");
            builder.AppendLine("Contents:");

            var contentStartLength = builder.Length;
            var index = 0;
            foreach (var entry in entries)
            {
                if (index >= maxEntries)
                {
                    builder.AppendLine("... [truncated]");
                    break;
                }

                builder.Append("- Name: ").Append(entry.DisplayName)
                    .Append(" | Type: ").Append(entry.TypeName)
                    .Append(" | Path: ").Append(entry.Path);

                if (includeGuids && !string.IsNullOrEmpty(entry.Guid))
                    builder.Append(" | GUID: ").Append(entry.Guid);

                builder.AppendLine();
                index++;
            }

            return builder.Length > contentStartLength ? builder.ToString() : null;
        }
    }
}
