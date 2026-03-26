using System;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;
using UnityEditorInternal;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class ModifyTagTool
    {
        internal const string k_FunctionId = "Unity.GameObject.ManageTag";

        internal const string k_InvalidActionMessage = "Tag action '{0}' is an invalid option.";
        internal const string k_TagNameRequiredMessage = "tagName is required when using action '{0}'.";
        internal const string k_TagAlreadyExistsMessage = "Tag '{0}' already exists.";
        internal const string k_TagDoesNotExistMessage = "Tag '{0}' does not exist.";

        const string k_SettingsPath = "ProjectSettings/TagManager.asset";

        [JsonConverter(typeof(StringEnumConverter))]
        public enum TagAction
        {
            [EnumMember(Value = "list_tags")]
            ListTags,
            [EnumMember(Value = "add")]
            Add,
            [EnumMember(Value = "remove")]
            Remove
        }

        [Serializable]
        public struct ModifyTagOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("modifiedTag")]
            public string ModifiedTag;
        }

        [AgentTool(
            "Add or remove a tag in the current project. IMPORTANT: First call this tool with action 'list_tags', if we still have to confirm if a tag already exists, before adding it or before trying to remove it.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<ModifyTagOutput> ManageTag(
            ToolExecutionContext context,
            [Parameter("Tag name to add (e.g., 'Player', 'NPC')")]
            string tagName,
            [Parameter("Options are 'list_tags', 'add', or 'remove'. 'add' will add a new tag, 'remove' will remove an existing tag, 'list_tags' will return a list of all tags.")]
            TagAction action = TagAction.ListTags)
        {
            Debug.Log($"[ModifyTagTool] Call invoked - tagName: '{tagName}', action: '{action}'");

            var tags = InternalEditorUtility.tags;

            switch (action)
            {
                case TagAction.ListTags:
                    return ListTags(tags);

                case TagAction.Add:
                    return await AddTag(context, tagName, tags);

                case TagAction.Remove:
                    return await RemoveTag(context, tagName, tags);

                default:
                    throw new ArgumentException(string.Format(k_InvalidActionMessage, action));
            }
        }

        static ModifyTagOutput ListTags(string[] tags)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < tags.Length; i++)
            {
                if (!string.IsNullOrEmpty(tags[i]))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(tags[i]);
                }
            }

            return new ModifyTagOutput
            {
                Message = sb.ToString()
            };
        }

        static async Task<ModifyTagOutput> AddTag(ToolExecutionContext context, string tagName, string[] existingTags)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException(string.Format(k_TagNameRequiredMessage, "add"));

            if (TagExists(existingTags, tagName))
                throw new InvalidOperationException(string.Format(k_TagAlreadyExistsMessage, tagName));

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, k_SettingsPath);

            try
            {
                InternalEditorUtility.AddTag(tagName);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add tag '{tagName}'. {ex.Message}", ex);
            }

            return new ModifyTagOutput
            {
                Message = $"Tag '{tagName}' successfully added.",
                ModifiedTag = tagName
            };
        }

        static async Task<ModifyTagOutput> RemoveTag(ToolExecutionContext context, string tagName, string[] existingTags)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException(string.Format(k_TagNameRequiredMessage, "remove"));

            if (!TagExists(existingTags, tagName))
                throw new InvalidOperationException(string.Format(k_TagDoesNotExistMessage, tagName));

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, k_SettingsPath);

            try
            {
                InternalEditorUtility.RemoveTag(tagName);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove tag '{tagName}'. {ex.Message}", ex);
            }

            return new ModifyTagOutput
            {
                Message = $"Tag '{tagName}' successfully removed.",
                ModifiedTag = tagName
            };
        }

        static bool TagExists(string[] tags, string tagName)
        {
            foreach (var tag in tags)
            {
                if (string.Equals(tag, tagName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
