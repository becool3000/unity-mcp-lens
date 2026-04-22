#nullable disable
using System;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectRequestNormalizer
    {
        public GameObjectQueryRequest NormalizeQuery(JObject parameters, JToken targetToken, string searchMethod)
        {
            return new GameObjectQueryRequest
            {
                target = NormalizeTarget(targetToken),
                searchMethod = NormalizeSearchMethod(searchMethod),
                searchTerm = parameters?["search_term"]?.ToString() ?? parameters?["searchTerm"]?.ToString() ?? targetToken?.ToString(),
                findAll = parameters?["find_all"]?.ToObject<bool>() ?? parameters?["findAll"]?.ToObject<bool>() ?? false,
                searchInChildren = parameters?["search_in_children"]?.ToObject<bool>() ?? parameters?["searchInChildren"]?.ToObject<bool>() ?? false,
                searchInactive = parameters?["search_inactive"]?.ToObject<bool>() ?? parameters?["searchInactive"]?.ToObject<bool>() ?? false
            };
        }

        public GameObjectBoundsRequest NormalizeBounds(JToken targetToken, string searchMethod)
        {
            return new GameObjectBoundsRequest
            {
                target = NormalizeTarget(targetToken),
                searchMethod = NormalizeSearchMethod(searchMethod)
            };
        }

        public GameObjectSimpleModifyRequest NormalizeSimpleModify(JObject parameters, JToken targetToken, string searchMethod)
        {
            return new GameObjectSimpleModifyRequest
            {
                target = NormalizeTarget(targetToken),
                searchMethod = NormalizeSearchMethod(searchMethod),
                name = parameters?["name"]?.ToString(),
                hasSetActive = parameters?["set_active"] != null || parameters?["setActive"] != null,
                setActive = parameters?["set_active"]?.ToObject<bool>() ?? parameters?["setActive"]?.ToObject<bool>() ?? false,
                hasTag = parameters?["tag"] != null,
                tag = parameters?["tag"]?.ToString(),
                layer = parameters?["layer"]?.ToString(),
                position = ParseVector3(parameters?["position"] as JArray),
                positionType = parameters?["positionType"]?.ToString() ?? parameters?["position_type"]?.ToString(),
                rotation = ParseVector3(parameters?["rotation"] as JArray),
                scale = ParseVector3(parameters?["scale"] as JArray),
                hasParent = parameters?["parent"] != null,
                parent = NormalizeTarget(parameters?["parent"])
            };
        }

        public GameObjectComponentListRequest NormalizeComponentList(JObject parameters, JToken targetToken, string searchMethod)
        {
            return new GameObjectComponentListRequest
            {
                target = NormalizeTarget(targetToken),
                searchMethod = NormalizeSearchMethod(searchMethod),
                searchInactive = parameters?["search_inactive"]?.ToObject<bool>() ?? parameters?["searchInactive"]?.ToObject<bool>() ?? false
            };
        }

        public GameObjectComponentGetRequest NormalizeComponentGet(JObject parameters, JToken targetToken, string searchMethod)
        {
            return new GameObjectComponentGetRequest
            {
                target = NormalizeTarget(targetToken),
                searchMethod = NormalizeSearchMethod(searchMethod),
                searchInactive = parameters?["search_inactive"]?.ToObject<bool>() ?? parameters?["searchInactive"]?.ToObject<bool>() ?? false,
                componentName = parameters?["component_name"]?.ToString() ?? parameters?["componentName"]?.ToString(),
                componentIndex = ParseNullableInt(parameters?["component_index"] ?? parameters?["componentIndex"]),
                includeNonPublicSerialized = parameters?["include_non_public_serialized"]?.ToObject<bool>() ?? parameters?["includeNonPublicSerialized"]?.ToObject<bool>() ?? false
            };
        }

        static string NormalizeSearchMethod(string searchMethod)
        {
            return string.IsNullOrWhiteSpace(searchMethod) ? null : searchMethod.ToLowerInvariant();
        }

        static GameObjectTargetRef NormalizeTarget(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return new GameObjectTargetRef
                {
                    isNull = true
                };
            }

            string text = token.ToString();
            return new GameObjectTargetRef
            {
                text = text,
                isInteger = token.Type == JTokenType.Integer || int.TryParse(text, out _),
                isEmptyString = token.Type == JTokenType.String && string.IsNullOrEmpty(text)
            };
        }

        static Vector3Value ParseVector3(JArray array)
        {
            if (array == null)
                return null;

            if (array.Count != 3)
                throw new ArgumentException("Vector values must contain exactly 3 numbers.");

            return new Vector3Value
            {
                x = array[0].ToObject<float>(),
                y = array[1].ToObject<float>(),
                z = array[2].ToObject<float>()
            };
        }

        static int? ParseNullableInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.ToObject<int>();

            return int.TryParse(token.ToString(), out int value) ? value : null;
        }
    }
}
