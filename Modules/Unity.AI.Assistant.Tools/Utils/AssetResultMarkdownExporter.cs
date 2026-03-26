using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class AssetResultMarkdownExporter
    {
        const string k_Branch = "├── ";
        const string k_LastBranch = "└── ";
        const string k_Indent = "│   ";
        const string k_LastIndent = "    ";

        public static string ToMarkdownTree(AssetTools.AssetHierarchy result, bool includeID = true, 
            bool includeType = true)
        {
            var sb = new StringBuilder();

            for (var i = 0; i < result.Roots.Count; i++)
            {
                var root = result.Roots[i];
                sb.AppendLine(root.Name ?? "(null)");
                WriteFolderChildren(sb, root, "", includeID: includeID, includeType: includeType);
            }

            return sb.ToString();
        }

        static void WriteFolderChildren(StringBuilder sb, AssetTools.AssetFolder folder, string prefix, bool includeID,
            bool includeType)
        {
            var totalChildren = (folder.Children?.Count ?? 0) + (folder.Assets?.Count ?? 0);
            var childIndex = 0;

            // Write subfolders first
            if (folder.Children != null)
            {
                foreach (var child in folder.Children)
                {
                    childIndex++;
                    var lastChild = (childIndex == totalChildren);
                    WriteFolder(sb, child, prefix, lastChild, includeID: includeID, includeType: includeType);
                }
            }

            // Then write assets
            if (folder.Assets != null)
            {
                foreach (var asset in folder.Assets)
                {
                    childIndex++;
                    var lastChild = (childIndex == totalChildren);
                    WriteAsset(sb, asset, prefix, lastChild, includeID: includeID, includeType: includeType);
                }
            }
        }

        private static void WriteFolder(StringBuilder sb, AssetTools.AssetFolder folder, string prefix, bool isLast,
            bool includeID, bool includeType)
        {
            sb.Append(prefix);
            sb.Append(isLast ? k_LastBranch : k_Branch);
            sb.AppendLine(folder.Name ?? "(null)");
            var childPrefix = prefix + (isLast ? k_LastIndent : k_Indent);
            WriteFolderChildren(sb, folder, childPrefix, includeID: includeID, includeType: includeType);
        }

        static void WriteAsset(StringBuilder sb, AssetTools.AssetInfo asset, string prefix, bool isLast,
            bool includeID, bool includeType)
        {
            WriteInstanceInfo(sb, asset.MainAsset, prefix, isLast, includeID: includeID, includeType: includeType);

            if (asset.SubAssets != null && asset.SubAssets.Count > 0)
            {
                for (var i = 0; i < asset.SubAssets.Count; i++)
                {
                    var isLastSub = (i == asset.SubAssets.Count - 1);
                    WriteInstanceInfo(sb, asset.SubAssets[i], prefix + (isLast ? k_LastIndent : k_Indent), 
                        isLastSub, includeID: includeID, includeType: includeType);
                }
            }
        }

        static void WriteInstanceInfo(StringBuilder sb, AssetTools.InstanceInfo instance, string prefix, bool isLast,
            bool includeID, bool includeType)
        {
            var name = instance?.Name ?? "(null)";
            var instanceID = instance?.InstanceID ?? 0;
            var instanceType = GetAssetTypeName(instance?.Type);
            var instanceTags = instance?.Tags;
            var similarity = instance?.Similarity ?? -1f;

            sb.Append(prefix);
            sb.Append(isLast ? k_LastBranch : k_Branch);
            
            // Add warning symbol for low-similarity assets
            var qualityLevel = AssetMatchQuality.GetQualityLevel(similarity);
            if (qualityLevel == MatchQualityLevel.Low)
            {
                sb.Append("⚠️ ");
            }
            
            sb.Append(name);
            sb.Append(" (");
            
            var hasAny = false;
            void AppendPart(string text)
            {
                if (hasAny)
                    sb.Append(", ");
                sb.Append(text);
                hasAny = true;
            }

            if (includeID)
                AppendPart($"InstanceID: {instanceID}");
            if (includeType)
                AppendPart($"Type: {instanceType}");
            if (qualityLevel != MatchQualityLevel.None)
                AppendPart($"Match: {AssetMatchQuality.GetQualityDisplayText(qualityLevel)}");
            if (!string.IsNullOrEmpty(instanceTags))
                AppendPart($"Description: {instanceTags}");
            
            sb.AppendLine(")");
        }

        static string GetAssetTypeName(Type assetType)
        {
            return assetType?.Name ?? "(null)";
        }
    }
}
