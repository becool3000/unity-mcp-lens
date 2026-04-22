#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects
{
    sealed class UnityPrefabResolution
    {
        public bool success { get; set; }
        public string message { get; set; }
        public string errorKind { get; set; }
        public string originalPath { get; set; }
        public string resolvedPath { get; set; }
        public GameObject prefabAsset { get; set; }
        public bool constructedSavePath { get; set; }

        public static UnityPrefabResolution Ok(string originalPath, string resolvedPath, GameObject prefabAsset, bool constructedSavePath = false)
        {
            return new UnityPrefabResolution
            {
                success = true,
                originalPath = originalPath,
                resolvedPath = resolvedPath,
                prefabAsset = prefabAsset,
                constructedSavePath = constructedSavePath
            };
        }

        public static UnityPrefabResolution Error(string message, string errorKind)
        {
            return new UnityPrefabResolution
            {
                success = false,
                message = message,
                errorKind = errorKind
            };
        }
    }

    sealed class UnityGameObjectCreateApplyStatus
    {
        public bool success { get; set; }
        public string message { get; set; }
        public string errorKind { get; set; }
        public UnityGameObjectHandle handle { get; set; }
        public bool createdNewObject { get; set; }
        public string source { get; set; }
        public string prefabPath { get; set; }
        public bool savedAsPrefab { get; set; }
        public List<ValidationMessage> validationMessages { get; } = new List<ValidationMessage>();

        public static UnityGameObjectCreateApplyStatus Error(string message, string errorKind)
        {
            return new UnityGameObjectCreateApplyStatus
            {
                success = false,
                message = message,
                errorKind = errorKind
            };
        }
    }

    sealed class UnityGameObjectDeleteApplyStatus
    {
        public bool success { get; set; }
        public string message { get; set; }
        public string errorKind { get; set; }
        public List<GameObjectTargetSummary> deletedObjects { get; set; } = new List<GameObjectTargetSummary>();
        public List<object> legacyDeletedObjects { get; set; } = new List<object>();

        public static UnityGameObjectDeleteApplyStatus Error(string message, string errorKind)
        {
            return new UnityGameObjectDeleteApplyStatus
            {
                success = false,
                message = message,
                errorKind = errorKind
            };
        }
    }

    sealed class UnityGameObjectLifecycleAdapter
    {
        readonly UnityGameObjectAdapter m_GameObjectAdapter;

        public UnityGameObjectLifecycleAdapter(UnityGameObjectAdapter gameObjectAdapter)
        {
            m_GameObjectAdapter = gameObjectAdapter;
        }

        public string[] GetPrimitiveTypes()
        {
            return Enum.GetNames(typeof(PrimitiveType));
        }

        public object GetBuiltinAssetsData()
        {
            return new
            {
                primitiveTypes = GetPrimitiveTypes(),
                builtinResources = new[]
                {
                    "Default-Material",
                    "Sprites-Default",
                    "UI/Skin/Background.psd",
                    "UI/Skin/Knob.psd"
                },
                note = "Use create with primitive_type for primitives, or Unity.Asset.Search for project assets."
            };
        }

        public bool TryParsePrimitiveType(string primitiveType, out PrimitiveType type)
        {
            type = default;
            return !string.IsNullOrWhiteSpace(primitiveType)
                && Enum.TryParse(primitiveType, true, out type);
        }

        public bool TryResolveLayer(string layerName, out int layerId)
        {
            return m_GameObjectAdapter.TryResolveLayer(layerName, out layerId);
        }

        public bool TagExists(string tagName)
        {
            return m_GameObjectAdapter.TagExists(tagName);
        }

        public UnityGameObjectHandle FindParent(GameObjectTargetRef parent)
        {
            return m_GameObjectAdapter.FindObject(parent, "by_id_or_name_or_path", true);
        }

        public List<UnityGameObjectHandle> FindDeleteTargets(GameObjectDeleteRequest request)
        {
            return m_GameObjectAdapter.FindObjects(new GameObjectQueryRequest
            {
                target = request?.target,
                searchMethod = request?.searchMethod,
                searchTerm = request?.target?.text,
                findAll = request?.findAll ?? false,
                searchInactive = request?.searchInactive ?? true
            });
        }

        public UnityPrefabResolution ResolvePrefab(GameObjectCreateRequest request)
        {
            string prefabPath = request?.prefabPath;
            string originalPrefabPath = prefabPath;
            bool constructed = false;

            if (request?.saveAsPrefab == true)
            {
                if (string.IsNullOrEmpty(prefabPath))
                {
                    if (string.IsNullOrEmpty(request.name))
                        return UnityPrefabResolution.Error("Cannot create default prefab path: 'name' parameter is missing.", "missing_name");

                    prefabPath = $"{(string.IsNullOrEmpty(request.prefabFolder) ? "Assets/Prefabs" : request.prefabFolder)}/{request.name}.prefab".Replace("\\", "/");
                    originalPrefabPath = prefabPath;
                    constructed = true;
                    Debug.Log($"[ManageGameObject.Create] Constructed prefab path: '{prefabPath}'");
                }
                else if (!prefabPath.ToLowerInvariant().EndsWith(".prefab"))
                {
                    return UnityPrefabResolution.Error($"Invalid prefab_path: '{prefabPath}' must end with .prefab", "invalid_prefab_path");
                }
            }

            if (string.IsNullOrEmpty(prefabPath))
                return UnityPrefabResolution.Ok(originalPrefabPath, null, null, constructed);

            if (!prefabPath.Contains("/") && !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                string prefabNameOnly = prefabPath;
                Debug.Log($"[ManageGameObject.Create] Searching for prefab named: '{prefabNameOnly}'");
                string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabNameOnly}");
                if (guids.Length == 0)
                {
                    return UnityPrefabResolution.Error(
                        $"Prefab named '{prefabNameOnly}' not found anywhere in the project.",
                        "prefab_not_found");
                }

                if (guids.Length > 1)
                {
                    string foundPaths = string.Join(", ", guids.Select(AssetDatabase.GUIDToAssetPath));
                    return UnityPrefabResolution.Error(
                        $"Multiple prefabs found matching name '{prefabNameOnly}': {foundPaths}. Please provide a more specific path.",
                        "multiple_prefabs_found");
                }

                prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                Debug.Log($"[ManageGameObject.Create] Found unique prefab at path: '{prefabPath}'");
            }
            else if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[ManageGameObject.Create] Provided prefabPath '{prefabPath}' does not end with .prefab. Assuming it's missing and appending.");
                prefabPath += ".prefab";
            }

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                Debug.LogWarning($"[ManageGameObject.Create] Prefab asset not found at path: '{prefabPath}'. Will proceed to create new object if specified.");

            return UnityPrefabResolution.Ok(originalPrefabPath, prefabPath, prefabAsset, constructed);
        }

        public UnityGameObjectCreateApplyStatus BeginCreate(
            GameObjectCreateRequest request,
            UnityPrefabResolution prefab,
            UnityGameObjectHandle parent,
            int? layerId)
        {
            GameObject newGo = null;
            bool createdNewObject = false;
            string source = "empty";

            if (prefab?.prefabAsset != null)
            {
                try
                {
                    newGo = PrefabUtility.InstantiatePrefab(prefab.prefabAsset) as GameObject;
                    if (newGo == null)
                    {
                        Debug.LogError($"[ManageGameObject.Create] Failed to instantiate prefab at '{prefab.resolvedPath}', asset might be corrupted or not a GameObject.");
                        return UnityGameObjectCreateApplyStatus.Error($"Failed to instantiate prefab at '{prefab.resolvedPath}'.", "prefab_instantiate_failed");
                    }

                    if (!string.IsNullOrEmpty(request.name))
                        newGo.name = request.name;

                    Undo.RegisterCreatedObjectUndo(newGo, $"Instantiate Prefab '{prefab.prefabAsset.name}' as '{newGo.name}'");
                    Debug.Log($"[ManageGameObject.Create] Instantiated prefab '{prefab.prefabAsset.name}' from path '{prefab.resolvedPath}' as '{newGo.name}'.");
                    source = "prefab";
                }
                catch (Exception ex)
                {
                    return UnityGameObjectCreateApplyStatus.Error($"Error instantiating prefab '{prefab.resolvedPath}': {ex.Message}", "prefab_instantiate_failed");
                }
            }

            if (newGo == null)
            {
                if (!string.IsNullOrEmpty(request.primitiveType))
                {
                    if (!TryParsePrimitiveType(request.primitiveType, out var primitiveType))
                    {
                        return UnityGameObjectCreateApplyStatus.Error(
                            $"Invalid primitive type: '{request.primitiveType}'. Valid types: {string.Join(", ", GetPrimitiveTypes())}",
                            "invalid_primitive_type");
                    }

                    try
                    {
                        newGo = GameObject.CreatePrimitive(primitiveType);
                        source = "primitive";
                    }
                    catch (Exception ex)
                    {
                        return UnityGameObjectCreateApplyStatus.Error($"Failed to create primitive '{request.primitiveType}': {ex.Message}", "create_failed");
                    }
                }
                else
                {
                    newGo = new GameObject(request.name);
                    source = "empty";
                }

                if (!string.IsNullOrEmpty(request.name))
                    newGo.name = request.name;

                createdNewObject = true;
                Undo.RegisterCreatedObjectUndo(newGo, $"Create GameObject '{newGo.name}'");
            }

            if (newGo == null)
                return UnityGameObjectCreateApplyStatus.Error("Failed to create or instantiate the GameObject.", "create_failed");

            Undo.RecordObject(newGo.transform, "Set GameObject Transform");
            Undo.RecordObject(newGo, "Set GameObject Properties");

            if (request.position != null)
                newGo.transform.localPosition = ToUnityVector3(request.position);
            if (request.rotation != null)
                newGo.transform.localEulerAngles = ToUnityVector3(request.rotation);
            if (request.scale != null)
                newGo.transform.localScale = ToUnityVector3(request.scale);
            if (request.hasParent)
                newGo.transform.SetParent(parent?.GameObject?.transform, true);

            if (request.tag != null)
            {
                string tagToSet = string.IsNullOrEmpty(request.tag) ? "Untagged" : request.tag;
                if (!TrySetTag(newGo, tagToSet, out var tagError))
                {
                    DestroyCreatedObject(newGo);
                    return UnityGameObjectCreateApplyStatus.Error(tagError, "tag_apply_failed");
                }
            }

            if (layerId.HasValue)
                newGo.layer = layerId.Value;

            return new UnityGameObjectCreateApplyStatus
            {
                success = true,
                handle = new UnityGameObjectHandle(newGo),
                createdNewObject = createdNewObject,
                source = source,
                prefabPath = prefab?.resolvedPath
            };
        }

        public UnityGameObjectCreateApplyStatus FinishCreate(GameObjectCreateRequest request, UnityPrefabResolution prefab, UnityGameObjectCreateApplyStatus status)
        {
            if (status?.handle?.GameObject == null)
                return UnityGameObjectCreateApplyStatus.Error("Failed to create or instantiate the GameObject.", "create_failed");

            GameObject finalInstance = status.handle.GameObject;
            bool savedAsPrefab = false;
            string finalPrefabPath = prefab?.resolvedPath;

            if (status.createdNewObject && request.saveAsPrefab)
            {
                if (string.IsNullOrEmpty(finalPrefabPath))
                {
                    DestroyCreatedObject(finalInstance);
                    return UnityGameObjectCreateApplyStatus.Error("'prefabPath' is required when 'saveAsPrefab' is true and creating a new object.", "invalid_prefab_path");
                }

                if (!finalPrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ManageGameObject.Create] Appending .prefab extension to save path: '{finalPrefabPath}' -> '{finalPrefabPath}.prefab'");
                    finalPrefabPath += ".prefab";
                }

                try
                {
                    string directoryPath = Path.GetDirectoryName(finalPrefabPath);
                    if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                        AssetDatabase.Refresh();
                        Debug.Log($"[ManageGameObject.Create] Created directory for prefab: {directoryPath}");
                    }

                    finalInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(finalInstance, finalPrefabPath, InteractionMode.UserAction);
                    if (finalInstance == null)
                    {
                        DestroyCreatedObject(status.handle.GameObject);
                        return UnityGameObjectCreateApplyStatus.Error($"Failed to save GameObject '{request.name}' as prefab at '{finalPrefabPath}'. Check path and permissions.", "prefab_save_failed");
                    }

                    savedAsPrefab = true;
                    Debug.Log($"[ManageGameObject.Create] GameObject '{request.name}' saved as prefab to '{finalPrefabPath}' and instance connected.");
                }
                catch (Exception ex)
                {
                    DestroyCreatedObject(status.handle.GameObject);
                    return UnityGameObjectCreateApplyStatus.Error($"Error saving prefab '{finalPrefabPath}': {ex.Message}", "prefab_save_failed");
                }
            }

            Selection.activeGameObject = finalInstance;

            string messagePrefabPath = finalInstance == null
                ? prefab?.originalPath
                : AssetDatabase.GetAssetPath(
                    PrefabUtility.GetCorrespondingObjectFromSource(finalInstance)
                        ?? (UnityEngine.Object)finalInstance);

            string successMessage;
            if (!status.createdNewObject && !string.IsNullOrEmpty(messagePrefabPath))
                successMessage = $"Prefab '{messagePrefabPath}' instantiated successfully as '{finalInstance.name}'.";
            else if (status.createdNewObject && request.saveAsPrefab && !string.IsNullOrEmpty(messagePrefabPath))
                successMessage = $"GameObject '{finalInstance.name}' created and saved as prefab to '{messagePrefabPath}'.";
            else
                successMessage = $"GameObject '{finalInstance.name}' created successfully in scene.";

            return new UnityGameObjectCreateApplyStatus
            {
                success = true,
                message = successMessage,
                handle = new UnityGameObjectHandle(finalInstance),
                createdNewObject = status.createdNewObject,
                source = status.source,
                prefabPath = savedAsPrefab ? finalPrefabPath : messagePrefabPath,
                savedAsPrefab = savedAsPrefab
            };
        }

        public void DestroyCreatedObject(UnityGameObjectHandle handle)
        {
            DestroyCreatedObject(handle?.GameObject);
        }

        public UnityGameObjectDeleteApplyStatus DeleteObjects(List<UnityGameObjectHandle> targets)
        {
            if (targets == null || targets.Count == 0)
                return UnityGameObjectDeleteApplyStatus.Error("Failed to delete target GameObject(s).", "delete_failed");

            var result = new UnityGameObjectDeleteApplyStatus
            {
                success = true
            };

            foreach (var target in targets)
            {
                if (target?.GameObject == null)
                    continue;

                string name = target.GameObject.name;
                object id = UnityApiAdapter.GetObjectId(target.GameObject);
                var summary = m_GameObjectAdapter.ToTargetSummary(target);
                Undo.DestroyObjectImmediate(target.GameObject);
                result.deletedObjects.Add(summary);
                result.legacyDeletedObjects.Add(new { name, instanceID = id });
            }

            if (result.deletedObjects.Count == 0)
                return UnityGameObjectDeleteApplyStatus.Error("Failed to delete target GameObject(s).", "delete_failed");

            result.message = result.deletedObjects.Count == 1
                ? $"GameObject '{result.deletedObjects[0].name}' deleted successfully."
                : $"{result.deletedObjects.Count} GameObjects deleted successfully.";
            return result;
        }

        public GameObjectInfo ToGameObjectInfo(UnityGameObjectHandle handle)
        {
            return m_GameObjectAdapter.ToGameObjectInfo(handle);
        }

        public GameObjectTargetSummary ToTargetSummary(UnityGameObjectHandle handle)
        {
            return m_GameObjectAdapter.ToTargetSummary(handle);
        }

        public object GetLegacyGameObjectData(UnityGameObjectHandle handle)
        {
            return m_GameObjectAdapter.GetLegacyGameObjectData(handle);
        }

        static void DestroyCreatedObject(GameObject gameObject)
        {
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
        }

        static bool TrySetTag(GameObject targetGo, string tagToSet, out string error)
        {
            error = null;
            try
            {
                targetGo.tag = tagToSet;
                return true;
            }
            catch (UnityException ex)
            {
                if (!ex.Message.Contains("is not defined"))
                {
                    error = $"Failed to set tag to '{tagToSet}' during creation: {ex.Message}.";
                    return false;
                }

                Debug.LogWarning($"[ManageGameObject.Create] Tag '{tagToSet}' not found. Attempting to create it.");
                try
                {
                    InternalEditorUtility.AddTag(tagToSet);
                    targetGo.tag = tagToSet;
                    Debug.Log($"[ManageGameObject.Create] Tag '{tagToSet}' created and assigned successfully.");
                    return true;
                }
                catch (Exception innerEx)
                {
                    error = $"Failed to create or assign tag '{tagToSet}' during creation: {innerEx.Message}.";
                    return false;
                }
            }
        }

        static Vector3 ToUnityVector3(Vector3Value value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }
}
