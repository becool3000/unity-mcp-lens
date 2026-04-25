using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Becool.UnityMcpLens.Editor.Services.GameObjects;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Compatibility facade for locating scene GameObjects.
    /// The Unity API boundary lives in UnityGameObjectAdapter.
    /// </summary>
    public static class ObjectsHelper
    {
        static readonly UnityGameObjectAdapter GameObjectAdapter = new UnityGameObjectAdapter();
        static readonly GameObjectRequestNormalizer RequestNormalizer = new GameObjectRequestNormalizer();

        /// <summary>
        /// Finds a single GameObject based on token (ID, name, path) and search method.
        /// </summary>
        public static GameObject FindObject(
            JToken targetToken,
            string searchMethod,
            JObject findParams = null
        )
        {
            bool findAll = findParams?["find_all"]?.ToObject<bool>() ?? findParams?["findAll"]?.ToObject<bool>() ?? false;
            if (IsIdLookup(targetToken, searchMethod))
                findAll = false;

            return FindObjects(targetToken, searchMethod, findAll, findParams).FirstOrDefault();
        }

        /// <summary>
        /// Finds GameObjects using the shared TSAM GameObject adapter search semantics.
        /// </summary>
        public static List<GameObject> FindObjects(
            JToken targetToken,
            string searchMethod,
            bool findAll,
            JObject findParams = null
        )
        {
            GameObjectQueryRequest request = RequestNormalizer.NormalizeQuery(findParams, targetToken, searchMethod);
            request.findAll = findAll;

            return GameObjectAdapter.FindObjects(request)
                .Select(handle => handle.GameObject)
                .Where(gameObject => gameObject != null)
                .Distinct()
                .ToList();
        }

        static bool IsIdLookup(JToken targetToken, string searchMethod)
        {
            return targetToken?.Type == JTokenType.Integer
                || (string.Equals(searchMethod, "by_id", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(targetToken?.ToString(), out _));
        }
    }
}
