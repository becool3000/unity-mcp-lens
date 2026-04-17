using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Tools;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Helper class for finding and locating GameObjects in the scene using various search methods.
    /// </summary>
    public static class ObjectsHelper
    {
        /// <summary>
        /// Finds a single GameObject based on token (ID, name, path) and search method.
        /// </summary>
        /// <param name="targetToken">Token representing the target GameObject (ID, name, or path).</param>
        /// <param name="searchMethod">Search method to use (by_id, by_name, by_path, by_tag, by_layer, by_component, or by_id_or_name_or_path).</param>
        /// <param name="findParams">Optional parameters including find_all, search_term, search_in_children, and search_inactive flags.</param>
        /// <returns>The first matching GameObject, or null if no match is found.</returns>
        public static GameObject FindObject(
            JToken targetToken,
            string searchMethod,
            JObject findParams = null
        )
        {
            // If find_all is not explicitly false, we still want only one for most single-target operations.
            bool findAll = findParams?["find_all"]?.ToObject<bool>() ?? false;
            // If a specific target ID is given, always find just that one.
            if (
                targetToken?.Type == JTokenType.Integer
                || (searchMethod == "by_id" && int.TryParse(targetToken?.ToString(), out _))
            )
            {
                findAll = false;
            }
            List<GameObject> results = FindObjects(
                targetToken,
                searchMethod,
                findAll,
                findParams
            );
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>
        /// Core logic for finding GameObjects based on various criteria.
        /// </summary>
        /// <param name="targetToken">Token representing the target GameObject(s) (ID, name, or path).</param>
        /// <param name="searchMethod">Search method to use (by_id, by_name, by_path, by_tag, by_layer, by_component, or by_id_or_name_or_path).</param>
        /// <param name="findAll">Whether to return all matching GameObjects or just the first one.</param>
        /// <param name="findParams">Optional parameters including search_term, search_in_children, and search_inactive flags.</param>
        /// <returns>List of matching GameObjects (may be empty if no matches found).</returns>
        public static List<GameObject> FindObjects(
            JToken targetToken,
            string searchMethod,
            bool findAll,
            JObject findParams = null
        )
        {
            List<GameObject> results = new List<GameObject>();
            string searchTerm = findParams?["search_term"]?.ToString() ?? targetToken?.ToString(); // Use searchTerm if provided, else the target itself
            bool searchInChildren = findParams?["search_in_children"]?.ToObject<bool>() ?? false;
            bool searchInactive = findParams?["search_inactive"]?.ToObject<bool>() ?? false;

            // Default search method if not specified
            if (string.IsNullOrEmpty(searchMethod))
            {
                if (targetToken?.Type == JTokenType.Integer)
                    searchMethod = "by_id";
                else if (!string.IsNullOrEmpty(searchTerm) && searchTerm.Contains('/'))
                    searchMethod = "by_path";
                else
                    searchMethod = "by_name"; // Default fallback
            }

            GameObject rootSearchObject = null;
            // If searching in children, find the initial target first
            if (searchInChildren && targetToken != null)
            {
                rootSearchObject = FindObject(targetToken, "by_id_or_name_or_path"); // Find the root for child search
                if (rootSearchObject == null)
                {
                    Debug.LogWarning(
                        $"[ManageGameObject.Find] Root object '{targetToken}' for child search not found."
                    );
                    return results; // Return empty if root not found
                }
            }

            switch (searchMethod)
            {
                case "by_id":
                    if (int.TryParse(searchTerm, out int instanceId))
                    {
                        // EditorUtility.InstanceIDToObject is slow, iterate manually if possible
                        // GameObject obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                        var allObjects = ManageGameObject.GetAllSceneObjects(searchInactive); // More efficient
                        GameObject obj = allObjects.FirstOrDefault(go =>
                            go.GetInstanceID() == instanceId
                        );
                        if (obj != null)
                            results.Add(obj);
                    }
                    break;
                case "by_name":
                    var searchPoolName = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : ManageGameObject.GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolName.Where(go => go.name == searchTerm));
                    break;
                case "by_path":
                    // Path is relative to scene root or rootSearchObject
                    Transform foundTransform = rootSearchObject
                        ? rootSearchObject.transform.Find(searchTerm)
                        : GameObject.Find(searchTerm)?.transform;
                    if (foundTransform != null)
                        results.Add(foundTransform.gameObject);
                    break;
                case "by_tag":
                    var searchPoolTag = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : ManageGameObject.GetAllSceneObjects(searchInactive);
                    results.AddRange(searchPoolTag.Where(go => go.CompareTag(searchTerm)));
                    break;
                case "by_layer":
                    var searchPoolLayer = rootSearchObject
                        ? rootSearchObject
                            .GetComponentsInChildren<Transform>(searchInactive)
                            .Select(t => t.gameObject)
                        : ManageGameObject.GetAllSceneObjects(searchInactive);
                    if (int.TryParse(searchTerm, out int layerIndex))
                    {
                        results.AddRange(searchPoolLayer.Where(go => go.layer == layerIndex));
                    }
                    else
                    {
                        int namedLayer = LayerMask.NameToLayer(searchTerm);
                        if (namedLayer != -1)
                            results.AddRange(searchPoolLayer.Where(go => go.layer == namedLayer));
                    }
                    break;
                case "by_component":
                    Type componentType = ManageGameObject.FindType(searchTerm);
                    if (componentType != null)
                    {
                        // Determine FindObjectsInactive based on the searchInactive flag
                        FindObjectsInactive findInactive = searchInactive
                            ? FindObjectsInactive.Include
                            : FindObjectsInactive.Exclude;
                        // Replace FindObjectsOfType with FindObjectsByType, specifying the sorting mode and inactive state
                        var searchPoolComp = rootSearchObject
                            ? rootSearchObject
                                .GetComponentsInChildren(componentType, searchInactive)
                                .Select(c => (c as Component).gameObject)
                            : UnityEngine
                                .Object.FindObjectsByType(
                                    componentType,
                                    findInactive,
                                    FindObjectsSortMode.None
                                )
                                .Select(c => (c as Component).gameObject);
                        results.AddRange(searchPoolComp.Where(go => go != null)); // Ensure GO is valid
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject.Find] Component type not found: {searchTerm}"
                        );
                    }
                    break;
                case "by_id_or_name_or_path": // Helper method used internally
                    if (int.TryParse(searchTerm, out int id))
                    {
                        var allObjectsId = ManageGameObject.GetAllSceneObjects(true); // Search inactive for internal lookup
                        GameObject objById = allObjectsId.FirstOrDefault(go =>
                            go.GetInstanceID() == id
                        );
                        if (objById != null)
                        {
                            results.Add(objById);
                            break;
                        }
                    }
                    GameObject objByPath = GameObject.Find(searchTerm);
                    if (objByPath != null)
                    {
                        results.Add(objByPath);
                        break;
                    }

                    var allObjectsName = ManageGameObject.GetAllSceneObjects(true);
                    results.AddRange(allObjectsName.Where(go => go.name == searchTerm));
                    break;
                default:
                    Debug.LogWarning(
                        $"[ManageGameObject.Find] Unknown search method: {searchMethod}"
                    );
                    break;
            }

            // If only one result is needed, return just the first one found.
            if (!findAll && results.Count > 1)
            {
                return new List<GameObject> { results[0] };
            }

            return results.Distinct().ToList(); // Ensure uniqueness
        }
    }
}
