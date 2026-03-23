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
        string IContextSelection.DownsizedPayload => m_Payload ??= BuildPayload();
        string IContextSelection.ContextType => "Folder";
        string IContextSelection.TargetName => FolderPath;
        bool? IContextSelection.Truncated => null;

        public bool Equals(IContextSelection other) =>
            other is FolderContextSelection otherSelection &&
            string.Equals(otherSelection.FolderPath, FolderPath, StringComparison.OrdinalIgnoreCase);

        string BuildPayload()
        {
            var entries = FolderContextUtils.EnumerateFolderAssetInfos(FolderPath);
            var builder = new StringBuilder();
            builder.AppendLine($"Folder: {FolderPath}");
            builder.AppendLine("Contents:");

            var contentStartLength = builder.Length;
            foreach (var entry in entries)
            {
                builder.Append("- Name: ").Append(entry.DisplayName)
                    .Append(" | Type: ").Append(entry.TypeName)
                    .Append(" | Path: ").Append(entry.Path);

                if (!string.IsNullOrEmpty(entry.Guid))
                    builder.Append(" | GUID: ").Append(entry.Guid);

                builder.AppendLine();
            }

            return builder.Length > contentStartLength ? builder.ToString() : null;
        }
    }
}
