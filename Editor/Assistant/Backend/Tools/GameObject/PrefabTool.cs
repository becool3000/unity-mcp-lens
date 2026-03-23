using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class PrefabTool
    {
        internal const string k_FunctionId = "Unity.GameObject.ManagePrefab";
        internal const string k_InvalidActionMessage = "Invalid action '{0}'. Must be 'create_prefab', 'instantiate_prefab', or 'override_prefab'.";
        internal const string k_PrefabPathRequiredMessage = "Prefab path cannot be null or empty.";
        internal const string k_PrefabExtensionMessage = "Prefab path must end with .prefab extension.";
        internal const string k_GameObjectIdRequiredMessage = "gameObjectInstanceId is required for {0} action.";
        internal const string k_PrefabExistsMessage = "Prefab already exists at path: {0}";
        internal const string k_PrefabNotFoundMessage = "Prefab not found at path: {0}";
        internal const string k_ParentNotFoundMessage = "Parent GameObject '{0}' not found in scene.";
        internal const string k_NotPrefabInstanceMessage = "GameObject '{0}' is not a prefab instance and cannot be used to override a prefab.";
        internal const string k_NoPrefabOverridesMessage = "Prefab instance '{0}' has no overrides to apply to the prefab asset.";

        const string k_CreatePrefabActionName = "create_prefab";
        const string k_InstantiatePrefabActionName = "instantiate_prefab";
        const string k_OverridePrefabActionName = "override_prefab";

        [JsonConverter(typeof(StringEnumConverter))]
        public enum PrefabAction
        {
            [EnumMember(Value = k_CreatePrefabActionName)]
            CreatePrefab,
            [EnumMember(Value = k_InstantiatePrefabActionName)]
            InstantiatePrefab,
            [EnumMember(Value = k_OverridePrefabActionName)]
            OverridePrefab
        }

        [Serializable]
        public struct PrefabToolOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("prefabPath")]
            public string PrefabPath;

            [JsonProperty("gameObjectId")]
            public int GameObjectId;
        }

        [AgentTool(
            "Create a prefab from an existing GameObject, instantiate an existing prefab in the Unity scene, or apply changes from a prefab instance to the prefab asset.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static async Task<PrefabToolOutput> ManagePrefab(
            ToolExecutionContext context,
            [Parameter("Action to perform: 'create_prefab' to create a prefab from a GameObject, 'instantiate_prefab' to instantiate an existing prefab in the scene, or 'override_prefab' to apply changes from a prefab instance back to the prefab asset.")]
            PrefabAction action = PrefabAction.CreatePrefab,
            [Parameter("Optional GameObject instance ID (e.g. 12345). Required for 'create_prefab' and 'override_prefab' actions. Use the ID returned from CreateGameObject or other GameObject operations. Default: null.")]
            int? gameObjectInstanceId = null,
            [Parameter("Path relative to the Assets folder. For 'create_prefab': where the prefab should be created. For 'instantiate_prefab': path to existing prefab to instantiate. For 'override_prefab': not required (auto-detected from GameObject). Must end with .prefab extension.")]
            string prefabPath = null,
            [Parameter("Optional parent GameObject name in the scene hierarchy for 'instantiate_prefab' action. If null, the instantiated GameObject will be placed at the root level of the scene.")]
            string parentPath = null)
        {
            if (!Enum.IsDefined(typeof(PrefabAction), action))
                throw new ArgumentException(string.Format(k_InvalidActionMessage, action));

            var actionName = GetActionString(action);
            Debug.Log($"[PrefabTool] Call invoked - action: '{actionName}', gameObjectInstanceId: '{gameObjectInstanceId}', prefabPath: '{prefabPath}', parentPath: '{parentPath}'");

            if (action != PrefabAction.OverridePrefab)
                ValidatePrefabPath(prefabPath);

            return action switch
            {
                PrefabAction.CreatePrefab => await CreatePrefab(context, gameObjectInstanceId, prefabPath),
                PrefabAction.InstantiatePrefab => await InstantiatePrefab(context, prefabPath, parentPath),
                PrefabAction.OverridePrefab => await OverridePrefab(context, gameObjectInstanceId),
                _ => throw new ArgumentException(string.Format(k_InvalidActionMessage, action))
            };
        }

        static async Task<PrefabToolOutput> CreatePrefab(ToolExecutionContext context, int? gameObjectInstanceId, string prefabPath)
        {
            if (!gameObjectInstanceId.HasValue)
                throw new ArgumentException(string.Format(k_GameObjectIdRequiredMessage, k_CreatePrefabActionName));

            var (targetGo, gameObjectError) = GameObjectToolsHelper.ValidateGameObject(gameObjectInstanceId.Value);
            if (targetGo == null)
                throw new InvalidOperationException(gameObjectError ?? GameObjectToolsHelper.FormatGameObjectNotFoundMessage(gameObjectInstanceId.Value));

            // Ensure the path starts with Assets/
            var fullPrefabPath = prefabPath.StartsWith("Assets/") ? prefabPath : "Assets/" + prefabPath;

            // Check if prefab already exists using Unity's asset API
            var existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fullPrefabPath);
            if (existingAsset != null)
                throw new InvalidOperationException(string.Format(k_PrefabExistsMessage, fullPrefabPath));

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, fullPrefabPath);

            // Create directory if it doesn't exist
            var directory = Path.GetDirectoryName(fullPrefabPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            // Create the prefab
            var prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(targetGo, fullPrefabPath, InteractionMode.UserAction);

            if (prefabAsset == null)
                throw new InvalidOperationException("Failed to create prefab asset.");

            // Refresh the asset database
            AssetDatabase.Refresh();

            return new PrefabToolOutput
            {
                Message = $"Prefab '{prefabAsset.name}' created successfully at '{fullPrefabPath}' from GameObject '{targetGo.name}' (ID {targetGo.GetInstanceID()}).",
                PrefabPath = fullPrefabPath,
                GameObjectId = targetGo.GetInstanceID()
            };
        }

        static async Task<PrefabToolOutput> InstantiatePrefab(ToolExecutionContext context, string prefabPath, string parentPath)
        {
            // Ensure the path starts with Assets/
            var fullPrefabPath = prefabPath.StartsWith("Assets/") ? prefabPath : "Assets/" + prefabPath;

            // Load the prefab asset
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fullPrefabPath);
            if (prefabAsset == null)
                throw new InvalidOperationException(string.Format(k_PrefabNotFoundMessage, fullPrefabPath));

            // Find parent GameObject if specified
            Transform parentTransform = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentGo = GameObjectToolsHelper.FindGameObjectByName(parentPath);
                if (parentGo == null)
                    throw new InvalidOperationException(string.Format(k_ParentNotFoundMessage, parentPath));
                parentTransform = parentGo.transform;
            }

            await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Create, typeof(GameObject), null);

            // Instantiate the prefab
            var instantiatedGo = PrefabUtility.InstantiatePrefab(prefabAsset, parentTransform) as GameObject;
            if (instantiatedGo == null)
                throw new InvalidOperationException("Failed to instantiate prefab.");

            // Register for undo
            Undo.RegisterCreatedObjectUndo(instantiatedGo, $"Instantiate Prefab '{prefabAsset.name}'");

            context.Permissions.IgnoreUnityObject(instantiatedGo);

            // Select the instantiated GameObject
            Selection.activeGameObject = instantiatedGo;
            EditorUtility.SetDirty(instantiatedGo);

            var parentInfo = parentTransform != null ? $" under '{parentTransform.name}'" : " at root level";

            return new PrefabToolOutput
            {
                Message = $"Prefab '{prefabAsset.name}' instantiated successfully{parentInfo}. GameObject ID: {instantiatedGo.GetInstanceID()}.",
                PrefabPath = fullPrefabPath,
                GameObjectId = instantiatedGo.GetInstanceID()
            };
        }

        static async Task<PrefabToolOutput> OverridePrefab(ToolExecutionContext context, int? gameObjectInstanceId)
        {
            if (!gameObjectInstanceId.HasValue)
                throw new ArgumentException(string.Format(k_GameObjectIdRequiredMessage, k_OverridePrefabActionName));

            var (targetGo, gameObjectError) = GameObjectToolsHelper.ValidateGameObject(gameObjectInstanceId.Value);
            if (targetGo == null)
                throw new InvalidOperationException(gameObjectError ?? GameObjectToolsHelper.FormatGameObjectNotFoundMessage(gameObjectInstanceId.Value));

            // Check if the GameObject is a prefab instance
            if (!PrefabUtility.IsPartOfPrefabInstance(targetGo))
                throw new InvalidOperationException(string.Format(k_NotPrefabInstanceMessage, targetGo.name));

            // Get the prefab instance root
            var prefabInstanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(targetGo);
            if (prefabInstanceRoot == null)
                throw new InvalidOperationException("Failed to find the prefab instance root.");

            // Get the source prefab asset and its path
            var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(prefabInstanceRoot);
            if (sourcePrefab == null)
                throw new InvalidOperationException("Failed to find the source prefab asset.");

            var sourcePrefabPath = AssetDatabase.GetAssetPath(sourcePrefab);

            // Check if there are any overrides to apply
            if (!PrefabUtility.HasPrefabInstanceAnyOverrides(prefabInstanceRoot, false))
                throw new InvalidOperationException(string.Format(k_NoPrefabOverridesMessage, prefabInstanceRoot.name));

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, sourcePrefabPath);

            PrefabUtility.ApplyPrefabInstance(prefabInstanceRoot, InteractionMode.UserAction);
            AssetDatabase.Refresh();

            return new PrefabToolOutput
            {
                Message = $"Successfully applied overrides from prefab instance '{prefabInstanceRoot.name}' to prefab asset at '{sourcePrefabPath}'.",
                PrefabPath = sourcePrefabPath,
                GameObjectId = prefabInstanceRoot.GetInstanceID()
            };
        }

        static string GetActionString(PrefabAction action)
        {
            return action switch
            {
                PrefabAction.CreatePrefab => k_CreatePrefabActionName,
                PrefabAction.InstantiatePrefab => k_InstantiatePrefabActionName,
                PrefabAction.OverridePrefab => k_OverridePrefabActionName,
                _ => action.ToString()
            };
        }

        static void ValidatePrefabPath(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
                throw new ArgumentException(k_PrefabPathRequiredMessage);

            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(k_PrefabExtensionMessage);
        }
    }
}
