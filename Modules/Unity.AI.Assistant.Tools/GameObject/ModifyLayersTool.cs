using System;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class ModifyLayerTool
    {
        internal const string k_FunctionId = "Unity.GameObject.ManageLayer";
        internal const string k_InvalidActionMessage = "Layer action '{0}' is an invalid option.";
        internal const string k_LayerNameRequiredMessage = "layerName is required when using action '{0}'.";
        internal const string k_LayerAlreadyExistsMessage = "Layer '{0}' already exists.";
        internal const string k_LayerDoesNotExistMessage = "Layer '{0}' does not exist.";
        internal const string k_BuiltinLayerRemoveMessage = "Layer '{0}' is a builtin layer and cannot be removed.";
        internal const string k_NoEmptyLayerSlotsMessage = "No empty layer slots available.";

        const string k_SettingsPath = "ProjectSettings/TagManager.asset";

        [JsonConverter(typeof(StringEnumConverter))]
        public enum LayerAction
        {
            [EnumMember(Value = "list_layers")]
            ListLayers,
            [EnumMember(Value = "add")]
            Add,
            [EnumMember(Value = "remove")]
            Remove
        }

        [Serializable]
        public struct ModifyLayerOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("modifiedLayer")]
            public string ModifiedLayer;
        }

        [AgentTool(
            "Add or remove a layer in the current project for use with collision logic or rendering. IMPORTANT: First call this tool with action 'list_layers', if we still have to confirm if a layer already exists, before adding it or before trying to remove it.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<ModifyLayerOutput> ManageLayer(
            ToolExecutionContext context,
            [Parameter("Layer name to add (e.g., 'PlayerCollision', 'BackgroundRendering')")]
            string layerName,
            [Parameter("Options are 'list_layers', 'add', or 'remove'. 'add' will add a new layer, 'remove' will remove an existing layer, 'list_layers' will return a list of all layers.")]
            LayerAction action = LayerAction.ListLayers)
        {
            Debug.Log($"[ModifyLayerTool] Call invoked - layerName: '{layerName}', action: '{action}'");

            var layers = InternalEditorUtility.layers;

            switch (action)
            {
                case LayerAction.ListLayers:
                    return ListLayers(layers);

                case LayerAction.Add:
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, k_SettingsPath);
                    return AddLayer(layerName, layers);

                case LayerAction.Remove:
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, k_SettingsPath);
                    return RemoveLayer(layerName, layers);

                default:
                    throw new ArgumentException(string.Format(k_InvalidActionMessage, action));
            }
        }

        static ModifyLayerOutput ListLayers(string[] layers)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < layers.Length; i++)
            {
                if (!string.IsNullOrEmpty(layers[i]))
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append(layers[i]);
                }
            }

            return new ModifyLayerOutput
            {
                Message = sb.ToString()
            };
        }

        static ModifyLayerOutput AddLayer(string layerName, string[] existingLayers)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                throw new ArgumentException(string.Format(k_LayerNameRequiredMessage, LayerAction.Add.ToString().ToLowerInvariant()));

            if (LayerExists(existingLayers, layerName))
                throw new InvalidOperationException(string.Format(k_LayerAlreadyExistsMessage, layerName));

            var tagManager = GetTagManager();
            var layersProp = tagManager.FindProperty("layers");

            var emptySlot = FindFirstEmptyUserLayerSlot(layersProp);
            if (emptySlot == -1)
                throw new InvalidOperationException(k_NoEmptyLayerSlotsMessage);

            layersProp.GetArrayElementAtIndex(emptySlot).stringValue = layerName;

            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return new ModifyLayerOutput
            {
                Message = $"Layer '{layerName}' added at slot {emptySlot}.",
                ModifiedLayer = layerName
            };
        }

        static ModifyLayerOutput RemoveLayer(string layerName, string[] existingLayers)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                throw new ArgumentException(string.Format(k_LayerNameRequiredMessage, LayerAction.Remove.ToString().ToLowerInvariant()));

            if (!LayerExists(existingLayers, layerName))
                throw new InvalidOperationException(string.Format(k_LayerDoesNotExistMessage, layerName));

            var tagManager = GetTagManager();
            var layersProp = tagManager.FindProperty("layers");

            var layerIndex = FindLayerIndex(layersProp, layerName);
            if (layerIndex == -1)
                throw new InvalidOperationException(string.Format(k_LayerDoesNotExistMessage, layerName));

            if (!IsUserEditableLayerIndex(layerIndex))
                throw new InvalidOperationException(string.Format(k_BuiltinLayerRemoveMessage, layerName));

            layersProp.GetArrayElementAtIndex(layerIndex).stringValue = string.Empty;

            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return new ModifyLayerOutput
            {
                Message = $"Layer '{layerName}' removed from slot {layerIndex}.",
                ModifiedLayer = layerName
            };
        }

        static SerializedObject GetTagManager()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(k_SettingsPath);
            if (assets == null || assets.Length == 0)
                throw new InvalidOperationException("Unable to load TagManager asset.");

            return new SerializedObject(assets[0]);
        }

        static int FindFirstEmptyUserLayerSlot(SerializedProperty layersProp)
        {
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                if (IsUserEditableLayerIndex(i) && string.IsNullOrEmpty(layersProp.GetArrayElementAtIndex(i).stringValue))
                    return i;
            }

            return -1;
        }

        static int FindLayerIndex(SerializedProperty layersProp, string layerName)
        {
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var value = layersProp.GetArrayElementAtIndex(i).stringValue;
                if (string.Equals(value, layerName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static bool IsUserEditableLayerIndex(int index)
        {
            return index == 3 || index >= 6;
        }

        static bool LayerExists(string[] layers, string layerName)
        {
            foreach (var layer in layers)
            {
                if (string.Equals(layer, layerName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

    }
}
