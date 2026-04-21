#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Becool.UnityMcpLens.Editor.Tools;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects
{
    sealed class UnityGameObjectHandle
    {
        internal GameObject GameObject { get; }

        public string Name => GameObject?.name;
        public object InstanceID => UnityApiAdapter.GetObjectId(GameObject);

        public UnityGameObjectHandle(GameObject gameObject)
        {
            GameObject = gameObject;
        }
    }

    sealed class UnityGameObjectSimpleChangeSet
    {
        public string name { get; set; }
        public bool? setActive { get; set; }
        public string tag { get; set; }
        public bool hasTag { get; set; }
        public int? layer { get; set; }
        public Vector3Value localPosition { get; set; }
        public Vector3Value localRotation { get; set; }
        public Vector3Value localScale { get; set; }
        public bool hasParent { get; set; }
        public UnityGameObjectHandle parent { get; set; }
    }

    sealed class UnityGameObjectAdapter
    {
        public List<UnityGameObjectHandle> FindObjects(GameObjectQueryRequest request)
        {
            var target = request?.target;
            string searchTerm = request?.searchTerm ?? target?.text;
            string searchMethod = ResolveSearchMethod(target, searchTerm, request?.searchMethod);
            bool searchInChildren = request?.searchInChildren ?? false;
            bool searchInactive = request?.searchInactive ?? false;
            bool findAll = request?.findAll ?? false;

            if (target?.isInteger == true || (searchMethod == "by_id" && int.TryParse(target?.text, out _)))
                findAll = false;

            var results = new List<GameObject>();
            GameObject rootSearchObject = null;
            if (searchInChildren && target != null && !target.isNull)
            {
                rootSearchObject = FindSingle(new GameObjectTargetRef { text = target.text, isInteger = target.isInteger }, "by_id_or_name_or_path", true);
                if (rootSearchObject == null)
                    return new List<UnityGameObjectHandle>();
            }

            switch (searchMethod)
            {
                case "by_id":
                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        var match = GetAllSceneObjects(searchInactive).FirstOrDefault(go => UnityApiAdapter.ObjectIdEquals(go, searchTerm));
                        if (match != null)
                            results.Add(match);
                    }
                    break;
                case "by_name":
                    results.AddRange(GetSearchPool(rootSearchObject, searchInactive).Where(go => go.name == searchTerm));
                    break;
                case "by_path":
                    Transform foundTransform = rootSearchObject
                        ? rootSearchObject.transform.Find(searchTerm)
                        : GameObject.Find(searchTerm)?.transform;
                    if (foundTransform != null)
                        results.Add(foundTransform.gameObject);
                    break;
                case "by_tag":
                    results.AddRange(GetSearchPool(rootSearchObject, searchInactive).Where(go => go.CompareTag(searchTerm)));
                    break;
                case "by_layer":
                    var pool = GetSearchPool(rootSearchObject, searchInactive);
                    if (int.TryParse(searchTerm, out int layerIndex))
                    {
                        results.AddRange(pool.Where(go => go.layer == layerIndex));
                    }
                    else
                    {
                        int namedLayer = LayerMask.NameToLayer(searchTerm);
                        if (namedLayer != -1)
                            results.AddRange(pool.Where(go => go.layer == namedLayer));
                    }
                    break;
                case "by_component":
                    var componentType = ManageGameObject.FindType(searchTerm);
                    if (componentType != null)
                    {
                        var componentPool = rootSearchObject
                            ? rootSearchObject.GetComponentsInChildren(componentType, searchInactive).Select(c => (c as Component)?.gameObject)
                            : UnityEngine.Object.FindObjectsByType(componentType, searchInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude).Select(c => (c as Component)?.gameObject);
                        results.AddRange(componentPool.Where(go => go != null));
                    }
                    break;
                case "by_id_or_name_or_path":
                    var byId = GetAllSceneObjects(true).FirstOrDefault(go => UnityApiAdapter.ObjectIdEquals(go, searchTerm));
                    if (byId != null)
                    {
                        results.Add(byId);
                        break;
                    }

                    var byPath = GameObject.Find(searchTerm);
                    if (byPath != null)
                    {
                        results.Add(byPath);
                        break;
                    }

                    results.AddRange(GetAllSceneObjects(true).Where(go => go.name == searchTerm));
                    break;
            }

            var distinct = results.Distinct().ToList();
            if (!findAll && distinct.Count > 1)
                distinct = new List<GameObject> { distinct[0] };

            return distinct.Select(go => new UnityGameObjectHandle(go)).ToList();
        }

        public UnityGameObjectHandle FindObject(GameObjectTargetRef target, string searchMethod, bool searchInactive = false)
        {
            var found = FindSingle(target, searchMethod, searchInactive);
            return found != null ? new UnityGameObjectHandle(found) : null;
        }

        public GameObjectSelectionResult GetSelection()
        {
            var objects = Selection.gameObjects
                .Select(ToGameObjectInfo)
                .ToArray();

            return new GameObjectSelectionResult
            {
                count = objects.Length,
                objects = objects
            };
        }

        public GameObjectBoundsInfo GetBounds(UnityGameObjectHandle handle)
        {
            var targetGo = handle?.GameObject;
            var renderers = targetGo.GetComponentsInChildren<Renderer>(includeInactive: true);
            var colliders = targetGo.GetComponentsInChildren<Collider>(includeInactive: true);
            bool hasBounds = false;
            Bounds bounds = new Bounds(targetGo.transform.position, Vector3.zero);

            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            foreach (var collider in colliders)
            {
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return new GameObjectBoundsInfo
            {
                target = targetGo.name,
                id = GetObjectIdString(targetGo),
                instanceID = UnityApiAdapter.GetObjectId(targetGo),
                hasRendererOrColliderBounds = hasBounds,
                center = ToVector3Value(bounds.center),
                size = ToVector3Value(bounds.size),
                extents = ToVector3Value(bounds.extents),
                rendererCount = renderers.Length,
                colliderCount = colliders.Length
            };
        }

        public GameObjectTargetSummary ToTargetSummary(UnityGameObjectHandle handle)
        {
            var go = handle?.GameObject;
            if (go == null)
                return null;

            return new GameObjectTargetSummary
            {
                name = go.name,
                id = GetObjectIdString(go),
                instanceID = UnityApiAdapter.GetObjectId(go),
                path = GetHierarchyPath(go.transform),
                scenePath = go.scene.path
            };
        }

        public GameObjectTargetSummary GetParentSummary(UnityGameObjectHandle handle)
        {
            var parent = handle?.GameObject?.transform.parent?.gameObject;
            return parent != null ? ToTargetSummary(new UnityGameObjectHandle(parent)) : null;
        }

        public GameObjectMutableState GetMutableState(UnityGameObjectHandle handle)
        {
            var go = handle.GameObject;
            return new GameObjectMutableState
            {
                name = go.name,
                activeSelf = go.activeSelf,
                tag = go.tag,
                layer = go.layer,
                localPosition = ToVector3Value(go.transform.localPosition),
                localRotation = ToVector3Value(go.transform.localEulerAngles),
                localScale = ToVector3Value(go.transform.localScale)
            };
        }

        public bool TryResolveLayer(string layerName, out int layerId)
        {
            layerId = LayerMask.NameToLayer(layerName);
            return layerId != -1 || layerName == "Default";
        }

        public bool TagExists(string tagName)
        {
            if (string.IsNullOrEmpty(tagName) || tagName == "Untagged")
                return true;

            return InternalEditorUtility.tags.Any(tag => string.Equals(tag, tagName, StringComparison.Ordinal));
        }

        public Vector3Value ResolvePosition(UnityGameObjectHandle target, Vector3Value requestedPosition, string positionType)
        {
            if (requestedPosition == null)
                return null;

            string normalized = string.IsNullOrEmpty(positionType) ? "center" : positionType.ToLowerInvariant();
            var positionToSet = ToUnityVector3(requestedPosition);
            if (normalized == "center")
            {
                var center = ComponentResolver.GetObjectWorldCenter(target.GameObject);
                var delta = center - target.GameObject.transform.position;
                positionToSet -= delta;
            }

            return ToVector3Value(positionToSet);
        }

        public bool WouldCreateParentLoop(UnityGameObjectHandle target, UnityGameObjectHandle parent)
        {
            return target?.GameObject != null
                && parent?.GameObject != null
                && parent.GameObject.transform.IsChildOf(target.GameObject.transform);
        }

        public bool HasParent(UnityGameObjectHandle target, UnityGameObjectHandle parent)
        {
            return target.GameObject.transform.parent == parent?.GameObject?.transform;
        }

        public GameObjectInfo ApplySimpleChanges(UnityGameObjectHandle target, UnityGameObjectSimpleChangeSet changes, out string error)
        {
            error = null;
            var targetGo = target.GameObject;

            Undo.RecordObject(targetGo.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGo, "Modify GameObject Properties");

            if (!string.IsNullOrEmpty(changes.name))
                targetGo.name = changes.name;

            if (changes.setActive.HasValue)
                targetGo.SetActive(changes.setActive.Value);

            if (changes.hasTag)
            {
                string tagToSet = string.IsNullOrEmpty(changes.tag) ? "Untagged" : changes.tag;
                if (!TrySetTag(targetGo, tagToSet, out error))
                    return null;
            }

            if (changes.layer.HasValue)
                targetGo.layer = changes.layer.Value;

            if (changes.localPosition != null)
                targetGo.transform.localPosition = ToUnityVector3(changes.localPosition);

            if (changes.localRotation != null)
                targetGo.transform.localEulerAngles = ToUnityVector3(changes.localRotation);

            if (changes.localScale != null)
                targetGo.transform.localScale = ToUnityVector3(changes.localScale);

            if (changes.hasParent)
                targetGo.transform.SetParent(changes.parent?.GameObject?.transform, true);

            EditorUtility.SetDirty(targetGo);
            return ToGameObjectInfo(targetGo);
        }

        public GameObjectInfo ToGameObjectInfo(UnityGameObjectHandle handle)
        {
            return ToGameObjectInfo(handle?.GameObject);
        }

        static string ResolveSearchMethod(GameObjectTargetRef target, string searchTerm, string searchMethod)
        {
            if (!string.IsNullOrEmpty(searchMethod))
                return searchMethod;

            if (target?.isInteger == true)
                return "by_id";

            return !string.IsNullOrEmpty(searchTerm) && searchTerm.Contains("/")
                ? "by_path"
                : "by_name";
        }

        static IEnumerable<GameObject> GetSearchPool(GameObject rootSearchObject, bool searchInactive)
        {
            return rootSearchObject
                ? rootSearchObject.GetComponentsInChildren<Transform>(searchInactive).Select(t => t.gameObject)
                : GetAllSceneObjects(searchInactive);
        }

        static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            var allObjects = new List<GameObject>();
            foreach (var root in rootObjects)
            {
                allObjects.AddRange(root.GetComponentsInChildren<Transform>(includeInactive).Select(t => t.gameObject));
            }

            return allObjects;
        }

        static GameObject FindSingle(GameObjectTargetRef target, string searchMethod, bool searchInactive)
        {
            if (target == null || target.isNull)
                return null;

            string searchTerm = target.text;
            string method = ResolveSearchMethod(target, searchTerm, searchMethod);
            switch (method)
            {
                case "by_id":
                    return GetAllSceneObjects(searchInactive).FirstOrDefault(go => UnityApiAdapter.ObjectIdEquals(go, searchTerm));
                case "by_name":
                    return GetAllSceneObjects(searchInactive).FirstOrDefault(go => go.name == searchTerm);
                case "by_path":
                    return GameObject.Find(searchTerm);
                case "by_id_or_name_or_path":
                    var byId = GetAllSceneObjects(true).FirstOrDefault(go => UnityApiAdapter.ObjectIdEquals(go, searchTerm));
                    if (byId != null)
                        return byId;

                    var byPath = GameObject.Find(searchTerm);
                    if (byPath != null)
                        return byPath;

                    return GetAllSceneObjects(true).FirstOrDefault(go => go.name == searchTerm);
                default:
                    return GetAllSceneObjects(searchInactive).FirstOrDefault(go => go.name == searchTerm);
            }
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
                    error = $"Failed to set tag to '{tagToSet}': {ex.Message}.";
                    return false;
                }

                try
                {
                    InternalEditorUtility.AddTag(tagToSet);
                    targetGo.tag = tagToSet;
                    return true;
                }
                catch (Exception innerEx)
                {
                    error = $"Failed to create or assign tag '{tagToSet}': {innerEx.Message}. Check Tag Manager and permissions.";
                    return false;
                }
            }
        }

        static GameObjectInfo ToGameObjectInfo(GameObject go)
        {
            if (go == null)
                return null;

            Physics.SyncTransforms();
            Bounds bounds;
            if (go.TryGetComponent<Collider>(out var collider))
            {
                bounds = collider.bounds;
            }
            else if (go.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                bounds = meshRenderer.bounds;
            }
            else
            {
                bounds = new Bounds(go.transform.position, go.transform.lossyScale);
            }

            return new GameObjectInfo
            {
                name = go.name,
                id = GetObjectIdString(go),
                instanceID = UnityApiAdapter.GetObjectId(go),
                tag = go.tag,
                layer = go.layer,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                isStatic = go.isStatic,
                scenePath = go.scene.path,
                transform = new GameObjectTransformInfo
                {
                    position = ToVector3Value(go.transform.position),
                    localPosition = ToVector3Value(go.transform.localPosition),
                    rotation = ToVector3Value(go.transform.rotation.eulerAngles),
                    localRotation = ToVector3Value(go.transform.localRotation.eulerAngles),
                    scale = ToVector3Value(go.transform.localScale),
                    forward = ToVector3Value(go.transform.forward),
                    up = ToVector3Value(go.transform.up),
                    right = ToVector3Value(go.transform.right)
                },
                center = ToVector3Value(bounds.center),
                extents = ToVector3Value(bounds.extents),
                size = ToVector3Value(bounds.size),
                parentId = GetObjectIdString(go.transform.parent?.gameObject),
                parentInstanceID = UnityApiAdapter.GetObjectIdOrZero(go.transform.parent?.gameObject),
                componentNames = go.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().FullName)
                    .ToList()
            };
        }

        static string GetObjectIdString(UnityEngine.Object obj)
        {
            object id = UnityApiAdapter.GetObjectId(obj);
            return id == null ? null : Convert.ToString(id, CultureInfo.InvariantCulture);
        }

        static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return null;

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        static Vector3Value ToVector3Value(Vector3 value)
        {
            return new Vector3Value
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }

        static Vector3 ToUnityVector3(Vector3Value value)
        {
            return new Vector3(value.x, value.y, value.z);
        }
    }
}
