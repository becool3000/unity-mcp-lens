using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Unity.AI.Assistant.Utils
{
    static class PayloadBudgetPolicy
    {
        public const int MaxToolResultBytes = 8192;
        public const int MaxIssuesBytes = 8192;
        public const int MaxSceneQueryBytes = 4096;
        public const int MaxChangedFilesBytes = 12288;
        public const int MaxPreviewFileBytes = 8192;
        public const int MaxPreviewFileLines = 80;
        public const int MaxInlineSnippetLines = 40;
        public const int MaxProjectDataChars = 32000;
        public const int MaxProjectDataItems = 300;
        public const int MaxUiLayoutEntries = 50;
        public const int MaxDiagnosticFindings = 20;
        public const int MaxPersistedBlockBytes = 16384;
        public const int MaxPersistedPreviewBytes = 8192;
        public const double ConversationSaveDebounceSeconds = 0.35d;
        public const double HeartbeatWriteIntervalSeconds = 5d;
    }

    [Serializable]
    class BudgetedToolResult
    {
        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("meta")]
        public object Meta { get; set; }

        [JsonProperty("preview")]
        public string Preview { get; set; }

        [JsonProperty("detailAvailable")]
        public bool DetailAvailable { get; set; }

        [JsonProperty("detailRef")]
        public object DetailRef { get; set; }

        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("bytes")]
        public int Bytes { get; set; }
    }

    static class PayloadBudgeting
    {
        static readonly UTF8Encoding k_Utf8 = new UTF8Encoding(false);

        public static int GetUtf8ByteCount(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : k_Utf8.GetByteCount(text);
        }

        public static string ComputeSha256(string text)
        {
            return ComputeSha256(string.IsNullOrEmpty(text) ? Array.Empty<byte>() : k_Utf8.GetBytes(text));
        }

        public static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static int EstimateTokensFromBytes(int byteCount)
        {
            return byteCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(byteCount / 4.0d));
        }

        public static string CreateTextPreview(string text, int maxLines, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var preview = normalized;
            if (maxLines > 0 && lines.Length > maxLines)
            {
                preview = string.Join("\n", lines, 0, maxLines);
                truncated = true;
            }

            if (maxBytes > 0)
            {
                preview = TruncateToUtf8Bytes(preview, maxBytes, ref truncated);
            }

            return truncated ? preview.TrimEnd() + "\n... [truncated]" : preview;
        }

        public static string TruncateForStorage(string text, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return TruncateToUtf8Bytes(text, maxBytes, ref truncated);
        }

        public static BudgetedToolResult CreateTextResult(string summary, object meta, string text, object detailRef, int maxPreviewLines = PayloadBudgetPolicy.MaxPreviewFileLines, int maxPreviewBytes = PayloadBudgetPolicy.MaxToolResultBytes)
        {
            var rawBytes = GetUtf8ByteCount(text);
            var preview = CreateTextPreview(text, maxPreviewLines, maxPreviewBytes, out var truncated);
            return new BudgetedToolResult
            {
                Summary = summary,
                Meta = meta,
                Preview = preview,
                DetailAvailable = detailRef != null,
                DetailRef = detailRef,
                Truncated = truncated,
                Sha256 = ComputeSha256(text),
                Bytes = rawBytes
            };
        }

        static string TruncateToUtf8Bytes(string text, int maxBytes, ref bool truncated)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0)
                return text ?? string.Empty;

            if (k_Utf8.GetByteCount(text) <= maxBytes)
                return text;

            truncated = true;
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (k_Utf8.GetByteCount(text.Substring(0, mid)) <= maxBytes)
                    low = mid;
                else
                    high = mid - 1;
            }

            return text.Substring(0, Math.Max(0, low));
        }
    }

    static class StableContextCache
    {
        static readonly Dictionary<string, string> s_LastHashByKey = new(StringComparer.Ordinal);
        static readonly object s_Lock = new();

        public static bool ShouldSendFull(string key, string sha256)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sha256))
                return true;

            lock (s_Lock)
            {
                if (s_LastHashByKey.TryGetValue(key, out var previousHash) && previousHash == sha256)
                    return false;

                s_LastHashByKey[key] = sha256;
                return true;
            }
        }

        public static void Clear()
        {
            lock (s_Lock)
            {
                s_LastHashByKey.Clear();
            }
        }
    }

    static class PayloadStats
    {
        static readonly object s_Lock = new();
        static string s_FilePath;

        public static void Record(string stage, string name, int rawBytes, int shapedBytes, int tokenEstimate, string hash)
        {
            try
            {
                var payload = new
                {
                    timestampUtc = DateTime.UtcNow.ToString("O"),
                    stage,
                    name,
                    rawBytes,
                    shapedBytes,
                    tokenEstimate,
                    hash
                };

                var filePath = GetFilePath();
                if (string.IsNullOrEmpty(filePath))
                    return;

                var line = JsonConvert.SerializeObject(payload, Formatting.None) + Environment.NewLine;
                lock (s_Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
                    File.AppendAllText(filePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        static string GetFilePath()
        {
            if (!string.IsNullOrEmpty(s_FilePath))
                return s_FilePath;

            try
            {
                var dataPath = Application.dataPath;
                var projectRoot = !string.IsNullOrEmpty(dataPath)
                    ? Directory.GetParent(dataPath)?.FullName
                    : Directory.GetCurrentDirectory();
                if (string.IsNullOrEmpty(projectRoot))
                    projectRoot = Directory.GetCurrentDirectory();

                s_FilePath = Path.Combine(projectRoot, "Library", "AI.Gateway.PayloadStats.jsonl");
                return s_FilePath;
            }
            catch
            {
                return null;
            }
        }
    }
}
