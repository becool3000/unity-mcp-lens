using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;
using Component = UnityEngine.Component;

namespace Unity.AI.Assistant.Tools.Editor
{
    class SceneTools
    {
        const string k_FindSceneObjectsFunctionId = "Unity.FindSceneObjects";
        const string k_GetVisibleObjectsFunctionId = "Unity.Camera.GetVisibleObjects";
        const string k_GetSceneInfoFunctionId = "Unity.GetSceneInfo";

        [Serializable]
        internal class ComponentInfo
        {
            public Type Type = null;
            public int InstanceID = 0;
        }

        [Serializable]
        internal class SceneObject
        {
            public string Name = null;
            public int InstanceID = 0;

            public List<SceneObject> Children = new();
            public List<ComponentInfo> Components = new();
        }

        [Serializable]
        internal class SceneHierarchy
        {
            public List<SceneObject> Roots = new();
        }

        [Serializable]
        public class FindSceneObjectsOutput
        {
            public string Hierarchy = string.Empty;

            public string Info = string.Empty;
        }

        const string k_SearchProviderId = "scene";
        static internal int MaxResultsPerCall { get; set; } = 20;

        [AgentTool(
            "Returns scene information including the entry scene from build settings and the currently loaded scenes in the editor.",
            k_GetSceneInfoFunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag)]
        internal static async Task<Dictionary<string, string>> GetSceneInfo(ToolExecutionContext context)
        {
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, Application.dataPath);

            var result = new Dictionary<string, string>();

            var scenes = EditorBuildSettings.scenes;
            foreach (var scene in scenes)
            {
                if (scene.enabled)
                {
                    result["First Scene"] = scene.path;
                    break;
                }
            }

            var loadedScenes = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path))
                {
                    loadedScenes.Add(scene.path);
                }
            }

            if (loadedScenes.Count > 0)
            {
                result["Loaded Scenes"] = string.Join(", ", loadedScenes);
            }

            return result;
        }

        [AgentTool(
            "Find scene objects and returns their components if they match the given query.",
            k_FindSceneObjectsFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static FindSceneObjectsOutput FindSceneObjects(
            [Parameter("Optional: a search query to search for specific objects." +
                "The elements of the filter are separated by a space.\n" +
                " - Use no keyword to filter objects by name.\n" +
                " - Use the 't' prefix to filter by built-in or custom component name such as 't:Light', 't:Renderer', 't:Collider' or 't:MyScriptName'. DO NOT use 't:MonoBehaviour'.\n" +
                " - Use the 'path' prefix to filter objects by their scene path. Ex.: for an exact match: 'path=/Structures/Wall5/Brick'.\n" +
                " - Use the 'layer' prefix to filter by physics layer number.\n" +
                " - Use '-' in front of any keyword or the name filter to exclude object matching that filter, for instance '-t:Animator' will exclude any object with the Animator component.\n" +
                " - After the prefix, use ':' for a partial match or use '=' (exact match), '!=', '>', '<', '<=', '>=' operators to check the value.\n" +
                " - Filter by object property values using 'p(name)', like 't:SpriteRenderer p(drawmode)=Simple' or 'p(someProperty)=true'.\n" +
                " - Use 'or' and 'and' and grouping with '(' and ')' for more complex queries like: 't:Character and (layer:7 or layer:8)'\n" +
                " - Elements separated by a space are considered like a 'and' constraint, i.e. they must all match.\n" +
                "For instance, the filter 'car t:Collider t:Renderer' will look for object which names contains 'car' and which have at BOTH a component inheriting Light AND a component inheriting Renderer (Like MeshRenderer). " +
                "Use a wildcard '*' to get all the scene objects without any filtering.")]
            string query = "",

            [Parameter("Optional: Use this to resume a previous search that was incomplete.")]
            int startIndex = 0
        )
        {
            if (startIndex < 0)
                throw new ArgumentException("The start index must be positive or zero.");

            var result = new SceneHierarchy();

            InternalLog.Log($"Search query: {query}");

            using var pooledGameObjects = ListPool<GameObject>.Get(out var gameObjects);

            var totalCount = 0;
            var endIndex = 0;
            // Get all objects
            if (string.IsNullOrEmpty(query) || query == "*")
            {
                var allGOs = GameObject.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                );

                // Pagination logic
                totalCount = allGOs.Length;
                var itemCount = Mathf.Min(MaxResultsPerCall, totalCount - startIndex);
                endIndex = startIndex + itemCount;

                for (var i = startIndex; i < endIndex; i++)
                {
                    var go = allGOs[i];
                    if (go == null)
                        continue;

                    gameObjects.Add(go);
                }
            }
            // Do an object search
            else
            {
                using var context = SearchService.CreateContext(k_SearchProviderId, query);
                using var searchResults = SearchService.Request(context, SearchFlags.Synchronous);

                // Pagination logic
                totalCount = searchResults.Count;
                var itemCount = Mathf.Min(MaxResultsPerCall, totalCount - startIndex);
                var items = searchResults.GetRange(startIndex, itemCount);
                endIndex = startIndex + itemCount;

                foreach (var item in items)
                {
                    var go = item.ToObject<GameObject>();
                    if (go == null)
                        continue;

                    gameObjects.Add(go);
                }
            }

            var sceneObjectMap = new Dictionary<int, SceneObject>();
            var rootCandidates = new HashSet<int>();
            foreach (var go in gameObjects)
            {
                // Build the hierarchy from root to this object
                var hierarchy = new Stack<GameObject>();
                var current = go;
                while (current != null)
                {
                    hierarchy.Push(current);
                    current = current.transform.parent ? current.transform.parent.gameObject : null;
                }

                SceneObject parentSceneObject = null;
                while (hierarchy.Count > 0)
                {
                    var node = hierarchy.Pop();
                    var isMatch = node == go;

                    // Try get or create the SceneObject node
                    if (!sceneObjectMap.TryGetValue(node.GetInstanceID(), out var sceneObj))
                    {
                        sceneObj = new SceneObject
                        {
                            Name = node.name,
                            InstanceID = node.GetInstanceID(),
                            Components = isMatch ? GetAllComponentInfos(node) : null
                        };
                        sceneObjectMap[node.GetInstanceID()] = sceneObj;
                    }
                    else
                    {
                        // If this node is a match and previously was not, update its Components
                        if (isMatch && sceneObj.Components == null)
                        {
                            sceneObj.Components = GetAllComponentInfos(node);
                        }
                    }

                    // If this is not the root, set up parent/child relationships
                    if (parentSceneObject != null)
                    {
                        // Add as child if not already present
                        if (!parentSceneObject.Children.Any(c => c.InstanceID == sceneObj.InstanceID))
                            parentSceneObject.Children.Add(sceneObj);

                        // Remove from rootCandidates: it's not a root if it's a child
                        rootCandidates.Remove(sceneObj.InstanceID);
                    }
                    else
                    {
                        // Possibly a root, unless later found to be a child
                        rootCandidates.Add(sceneObj.InstanceID);
                    }

                    parentSceneObject = sceneObj;
                }
            }

            foreach (var rootId in rootCandidates)
            {
                if (sceneObjectMap.TryGetValue(rootId, out var rootSceneObj))
                    result.Roots.Add(rootSceneObj);
            }

            var hierarchyPayload = SceneObjectResultMarkdownExporter.ToMarkdownTree(result);
            var nextPageIndex = endIndex < totalCount ? endIndex : -1;
            var info = nextPageIndex != -1 ? $"Incomplete result. Use {nameof(startIndex)}={nextPageIndex} to get the next ones." : string.Empty;

            var formattedOutput = new FindSceneObjectsOutput
            {
                Hierarchy = hierarchyPayload,
                Info = info
            };

            InternalLog.Log($"[FindSceneObjects] {(string.IsNullOrEmpty(formattedOutput.Hierarchy) ? "<empty>" : formattedOutput.Hierarchy)}\n\n{formattedOutput.Info}");

            return formattedOutput;
        }

        [Serializable]
        public class GetVisibleObjectsOutput
        {
            public List<SceneObject> Objects = new();
            public string Info = string.Empty;
        }

        [AgentTool(
            "Returns a list of the objects that are currently visible in the Scene View. " +
            "If there are too many objects visible, only the objects closest to the camera will be returned.",
            k_GetVisibleObjectsFunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static GetVisibleObjectsOutput GetVisibleObjects(
            [Parameter("Optional: Use this to resume a previous search that was incomplete.")]
            int startIndex = 0
        )
        {
            if (startIndex < 0)
                throw new ArgumentException("The start index must be positive or zero.");

            InternalLog.Log($"GetVisibleObjects");

            var sceneObjects = new List<SceneObject>();
            var sceneObjectsWithDistances = new List<(SceneObject SceneObject, float Distance)>();

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                throw new Exception("No active Scene View found. Please open a Scene View window.");

            var sceneCamera = sceneView.camera;
            if (sceneCamera == null)
                throw new Exception("Scene View camera is not available.");

            var allGameObjects = GetVisibleGameObjects(sceneCamera);
            
            foreach (var instance in allGameObjects)
            {
                if (!TryGetSceneObject(instance, out var sceneObject))
                    continue;

                var distance = Vector3.Distance(instance.transform.position, sceneCamera.transform.position);
                sceneObjectsWithDistances.Add((sceneObject, distance));
            }

            sceneObjectsWithDistances.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            
            // Calculate pagination
            var totalCount = sceneObjectsWithDistances.Count;
            var itemCount = Mathf.Min(MaxResultsPerCall, totalCount - startIndex);
            var endIndex = startIndex + itemCount;

            // Get the paginated slice
            var objectsToReturn = sceneObjectsWithDistances.Skip(startIndex).Take(itemCount).ToList();
            foreach (var (sceneObject, _) in objectsToReturn)
            {
                sceneObjects.Add(sceneObject);
            }

            var nextPageIndex = endIndex < totalCount ? endIndex : -1;
            var info = nextPageIndex != -1 
                ? $"Showing {itemCount} objects (from index {startIndex}). Use {nameof(startIndex)}={nextPageIndex} to get the next ones. Total visible: {totalCount}."
                : $"Found {totalCount} visible objects.";

            var output = new GetVisibleObjectsOutput
            {
                Objects = sceneObjects,
                Info = info
            };

            InternalLog.Log($"Result ({sceneObjects.Count}): {info}");

            return output;
        }

        static List<GameObject> GetVisibleGameObjects(Camera camera)
        {
            var allRenderers = GameObject.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var visibleObjects = new HashSet<GameObject>();
            foreach (var renderer in allRenderers)
            {
                if (IsRendererVisible(camera, renderer))
                {
                    visibleObjects.Add(renderer.gameObject);
                }
            }
            return visibleObjects.ToList();
        }

        static bool IsRendererVisible(Camera camera, Renderer renderer)
        {
            var rendererBounds = renderer.bounds;
            
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, rendererBounds))
                return false;
            
            // Test center first
            if (IsInCameraViewport(rendererBounds.center, camera))
                return true;
            
            // Otherwise test corners
            var corners = rendererBounds.GetCorners();
            foreach (var corner in corners)
            {
                if (IsInCameraViewport(corner, camera))
                    return true;
            }
            return false;
        }

        static bool IsInCameraViewport(Vector3 worldPosition, Camera camera)
        {
            var viewportPoint = camera.WorldToViewportPoint(worldPosition);
            return viewportPoint.x >= 0f && viewportPoint.x <= 1f && viewportPoint.y >= 0f && viewportPoint.y <= 1f && viewportPoint.z >= 0f;
        }

        static bool TryGetSceneObject(GameObject go, out SceneObject sceneObject)
        {
            sceneObject = null;

            if (go == null)
                return false;

            sceneObject = new SceneObject
            {
                Name = go.name,
                InstanceID = go.GetInstanceID(),
                Components = GetAllComponentInfos(go)
            };

            return true;
        }

        static List<ComponentInfo> GetAllComponentInfos(GameObject go)
        {
            var result = new List<ComponentInfo>();
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue; // skip missing scripts
                result.Add(new ComponentInfo
                {
                    Type = comp.GetType(),
                    InstanceID = comp.GetInstanceID()
                });
            }
            return result;
        }
    }
}
