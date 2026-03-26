using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Manages persistent storage of ACP conversations as JSON files.
    /// Stores conversations in {ProjectRoot}/Library/AcpConversations/{providerId}/{agentSessionId}.json
    /// The Library folder is excluded from version control and asset processing.
    /// </summary>
    static class AcpConversationStorage
    {
        const string StorageFolderName = "AI.Gateway.Conversations";
        const string MaxConversationsKey = "Unity.AI.Assistant.Acp.MaxStoredConversations";
        const int DefaultMaxConversations = 20;
        const long WarningFileSizeBytes = 10 * 1024 * 1024; // 10MB


        static string StorageRootPath => Path.Combine(new DirectoryInfo(Application.dataPath).Parent.FullName, "Library", StorageFolderName);

        /// <summary>
        /// Fired when a session is saved to storage.
        /// Parameters: sessionId, providerId
        /// </summary>
        public static event Action<string, string> OnSessionSaved;

        /// <summary>
        /// Fired when all storage is cleared.
        /// </summary>
        public static event Action OnStorageCleared;

        /// <summary>
        /// Saves a conversation to disk using explicit JSON serialization.
        /// </summary>
        public static void Save(AssistantConversation conversation, bool silent = false)
        {
            if (conversation == null || string.IsNullOrEmpty(conversation.AgentSessionId) || string.IsNullOrEmpty(conversation.ProviderId))
            {
                Debug.LogWarning($"[AcpConversationStorage] Cannot save conversation - Null: {conversation == null}, AgentSessionId: {conversation?.AgentSessionId}, ProviderId: {conversation?.ProviderId}");
                return;
            }

            for (var messageIndex = 0; messageIndex < conversation.Messages.Count; messageIndex++)
            {
                var message = conversation.Messages[messageIndex];
                if (message?.Blocks == null)
                    continue;

                for (var blockIndex = 0; blockIndex < message.Blocks.Count; blockIndex++)
                {
                    var block = message.Blocks[blockIndex];
                    if (block == null)
                    {
                        Debug.LogWarning($"[AcpConversationStorage] Null block will not be persisted. Conversation: {conversation.AgentSessionId}, Provider: {conversation.ProviderId}, MessageIndex: {messageIndex}, BlockIndex: {blockIndex}, Role: {message?.Role}, Timestamp: {message?.Timestamp}");
                        continue;
                    }

                    if (!IsSupportedPersistentBlock(block))
                    {
                        Debug.LogWarning($"[AcpConversationStorage] Block type '{block.GetType().Name}' is not supported for ACP persistence and will be skipped. Conversation: {conversation.AgentSessionId}, Provider: {conversation.ProviderId}, MessageIndex: {messageIndex}, BlockIndex: {blockIndex}, Role: {message?.Role}, Timestamp: {message?.Timestamp}");
                    }
                }
            }

            try
            {
                var filePath = GetConversationFilePath(conversation.ProviderId, conversation.AgentSessionId);
                var directory = Path.GetDirectoryName(filePath);

                // Ensure directory exists
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Serialize to JSON using explicit serialization
                var json = conversation.ToJson();
                File.WriteAllText(filePath, json);

                // Check file size and warn if large
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > WarningFileSizeBytes)
                {
                    Debug.LogWarning($"ACP: Conversation file is large ({fileInfo.Length / (1024 * 1024)}MB): {filePath}");
                }

                if (!silent)
                {
                    // Enforce conversation limit per provider
                    EnforceLimitForProvider(conversation.ProviderId);

                    OnSessionSaved?.Invoke(conversation.AgentSessionId, conversation.ProviderId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AcpConversationStorage] Failed to save conversation {conversation.AgentSessionId}: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Updates only the IsFavorite field in a stored conversation without deserializing the full message history.
        /// </summary>
        public static void SetFavorite(string providerId, string agentSessionId, bool isFavorite)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(agentSessionId))
                return;

            try
            {
                var filePath = GetConversationFilePath(providerId, agentSessionId);
                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var jObject = JObject.Parse(json);
                jObject["isFavorite"] = isFavorite;
                File.WriteAllText(filePath, jObject.ToString(Newtonsoft.Json.Formatting.Indented));

                OnSessionSaved?.Invoke(agentSessionId, providerId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AcpConversationStorage] Failed to set favorite for {agentSessionId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates only the Title field in a stored conversation without deserializing the full message history.
        /// </summary>
        public static void SetTitle(string providerId, string agentSessionId, string title)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(agentSessionId))
                return;

            try
            {
                var filePath = GetConversationFilePath(providerId, agentSessionId);
                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var jObject = JObject.Parse(json);
                jObject["title"] = title;
                File.WriteAllText(filePath, jObject.ToString(Newtonsoft.Json.Formatting.Indented));

                OnSessionSaved?.Invoke(agentSessionId, providerId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AcpConversationStorage] Failed to set title for {agentSessionId}: {ex.Message}");
            }
        }

        static bool IsSupportedPersistentBlock(IAssistantMessageBlock block)
        {
            return block is ThoughtBlock
                || block is PromptBlock
                || block is AnswerBlock
                || block is ErrorBlock
                || block is FunctionCallBlock
                || block is AcpToolCallStorageBlock
                || block is AcpPlanStorageBlock;
        }

        /// <summary>
        /// Loads a full conversation from disk using explicit JSON deserialization.
        /// </summary>
        public static AssistantConversation Load(string providerId, string agentSessionId)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(agentSessionId))
                return null;

            try
            {
                var filePath = GetConversationFilePath(providerId, agentSessionId);
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                return AssistantConversation.FromJson(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to load conversation {agentSessionId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads metadata only (without full message history) for a conversation.
        /// This is faster than loading the full conversation.
        /// </summary>
        public static StoredAcpConversationMetadata LoadMetadata(string providerId, string agentSessionId)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(agentSessionId))
                return null;

            try
            {
                var filePath = GetConversationFilePath(providerId, agentSessionId);
                if (!File.Exists(filePath))
                    return null;

                // Read just enough JSON to extract metadata
                var json = File.ReadAllText(filePath);
                var conversation = AssistantConversation.FromJson(json);

                if (conversation == null)
                    return null;

                return new StoredAcpConversationMetadata
                {
                    AgentSessionId = conversation.AgentSessionId,
                    ProviderId = conversation.ProviderId,
                    Title = conversation.Title,
                    LastMessageTimestamp = conversation.LastMessageTimestamp,
                    IsFavorite = conversation.IsFavorite
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to load metadata for {agentSessionId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads metadata for all conversations from a specific provider.
        /// </summary>
        public static List<StoredAcpConversationMetadata> LoadAllMetadata(string providerId)
        {
            var metadata = new List<StoredAcpConversationMetadata>();

            try
            {
                var providerDir = GetProviderDirectory(providerId);
                if (!Directory.Exists(providerDir))
                    return metadata;

                var jsonFiles = Directory.GetFiles(providerDir, "*.json");
                foreach (var filePath in jsonFiles)
                {
                    try
                    {
                        var agentSessionId = Path.GetFileNameWithoutExtension(filePath);
                        var meta = LoadMetadata(providerId, agentSessionId);
                        if (meta != null)
                            metadata.Add(meta);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"ACP: Skipping corrupted conversation file {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to load metadata for provider {providerId}: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Loads metadata for all conversations across all providers.
        /// </summary>
        public static List<StoredAcpConversationMetadata> LoadAllMetadata()
        {
            var allMetadata = new List<StoredAcpConversationMetadata>();

            try
            {
                if (!Directory.Exists(StorageRootPath))
                    return allMetadata;

                var providerDirs = Directory.GetDirectories(StorageRootPath);
                foreach (var providerDir in providerDirs)
                {
                    var providerId = Path.GetFileName(providerDir);
                    var metadata = LoadAllMetadata(providerId);
                    allMetadata.AddRange(metadata);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to load all metadata: {ex.Message}");
            }

            // Sort by timestamp (newest first)
            allMetadata.Sort((a, b) => b.LastMessageTimestamp.CompareTo(a.LastMessageTimestamp));
            return allMetadata;
        }

        /// <summary>
        /// Deletes a conversation from storage.
        /// </summary>
        public static void Delete(string providerId, string agentSessionId)
        {
            if (string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(agentSessionId))
                return;

            try
            {
                var filePath = GetConversationFilePath(providerId, agentSessionId);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to delete conversation {agentSessionId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes all conversations for all providers.
        /// </summary>
        public static void ClearAll()
        {
            try
            {
                if (Directory.Exists(StorageRootPath))
                {
                    Directory.Delete(StorageRootPath, recursive: true);
                    Debug.Log("ACP: Cleared all conversation history");
                }

                OnStorageCleared?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to clear all conversations: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets storage information (total size and conversation count).
        /// </summary>
        public static (long usedBytes, int conversationCount) GetStorageInfo()
        {
            long totalBytes = 0;
            int count = 0;

            try
            {
                if (!Directory.Exists(StorageRootPath))
                    return (0, 0);

                var jsonFiles = Directory.GetFiles(StorageRootPath, "*.json", SearchOption.AllDirectories);
                count = jsonFiles.Length;

                foreach (var file in jsonFiles)
                {
                    var fileInfo = new FileInfo(file);
                    totalBytes += fileInfo.Length;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to get storage info: {ex.Message}");
            }

            return (totalBytes, count);
        }

        /// <summary>
        /// Gets the file path for a conversation.
        /// </summary>
        static string GetConversationFilePath(string providerId, string agentSessionId)
        {
            // Sanitize agentSessionId to be filesystem-safe
            var safeSessionId = SanitizeFileName(agentSessionId);
            return Path.Combine(GetProviderDirectory(providerId), $"{safeSessionId}.json");
        }

        /// <summary>
        /// Gets the directory for a provider's conversations.
        /// </summary>
        static string GetProviderDirectory(string providerId)
        {
            var safeProviderId = SanitizeFileName(providerId);
            return Path.Combine(StorageRootPath, safeProviderId);
        }

        /// <summary>
        /// Sanitizes a string for use as a filename.
        /// </summary>
        static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Enforces the maximum conversation limit for a provider by deleting oldest conversations.
        /// </summary>
        static void EnforceLimitForProvider(string providerId)
        {
            var maxConversations = EditorPrefs.GetInt(MaxConversationsKey, DefaultMaxConversations);
            if (maxConversations <= 0)
                return; // No limit

            try
            {
                var metadata = LoadAllMetadata(providerId);
                if (metadata.Count <= maxConversations)
                    return;

                // Only delete non-favorite conversations; favorites are protected
                var nonFavorites = metadata.Where(m => !m.IsFavorite).ToList();
                nonFavorites.Sort((a, b) => a.LastMessageTimestamp.CompareTo(b.LastMessageTimestamp));
                var toDelete = metadata.Count - maxConversations;

                for (int i = 0; i < toDelete && i < nonFavorites.Count; i++)
                {
                    Delete(providerId, nonFavorites[i].AgentSessionId);
                    InternalLog.Log($"ACP: Deleted old conversation {nonFavorites[i].Title} to maintain limit");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ACP: Failed to enforce conversation limit: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lightweight metadata for a stored conversation.
    /// Used for blackboard initialization without loading full message history.
    /// </summary>
    internal class StoredAcpConversationMetadata
    {
        public string AgentSessionId;
        public string ProviderId;
        public string Title;
        public long LastMessageTimestamp;
        public bool IsFavorite;

        /// <summary>
        /// Converts this metadata to AssistantConversationInfo for UI display.
        /// </summary>
        public AssistantConversationInfo ToConversationInfo()
        {
            return new AssistantConversationInfo
            {
                Id = new AssistantConversationId(AgentSessionId),
                Title = Title ?? "Untitled Conversation",
                LastMessageTimestamp = LastMessageTimestamp,
                IsFavorite = IsFavorite,
                IsContextual = false
            };
        }
    }
}
