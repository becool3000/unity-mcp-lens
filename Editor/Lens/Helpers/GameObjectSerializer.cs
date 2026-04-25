using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Becool.UnityMcpLens.Runtime.Serialization; // For Converters

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Provides serialization of Unity GameObjects and Components to JSON-friendly structures for MCP tool responses.
    /// Handles complex Unity types, avoids circular references, and caches reflection metadata for performance.
    /// </summary>
    /// <remarks>
    /// This class is designed for MCP tools that need to return information about scene objects to clients.
    /// It safely serializes:
    /// - GameObject hierarchy, transforms, and bounds
    /// - Component properties and fields (including private [SerializeField] members)
    /// - Unity-specific types (Vector3, Quaternion, Color, etc.)
    ///
    /// Performance optimizations:
    /// - Caches reflection metadata per component type
    /// - Skips known problematic properties (deprecated shortcuts, circular references)
    /// - Special handling for Transform and Camera to avoid crashes
    ///
    /// The output is JSON-serializable via Newtonsoft.Json with custom Unity type converters.
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpTool("get_object_info", "Gets information about a GameObject")]
    /// public static object GetObjectInfo(JObject params)
    /// {
    ///     var objName = params["name"]?.Value&lt;string&gt;();
    ///     var go = GameObject.Find(objName);
    ///
    ///     if (go == null)
    ///         return Response.Error("OBJECT_NOT_FOUND");
    ///
    ///     var data = GameObjectSerializer.GetGameObjectData(go);
    ///     return Response.Success($"Found {objName}", data);
    /// }
    /// </code>
    /// </example>
    public static class GameObjectSerializer
    {
        const int MaxSerializationDepth = 3;
        const int MaxCollectionItems = 32;
        const int MaxDictionaryEntries = 24;

        // --- Data Serialization ---

        /// <summary>
        /// Creates a JSON-serializable representation of a GameObject including transform, bounds, and component list.
        /// </summary>
        /// <remarks>
        /// The returned object includes:
        /// - Basic info: name, instanceID, tag, layer, active state
        /// - Transform data: position, rotation (euler angles), scale, direction vectors
        /// - Bounds: center, size, extents (from Collider or Renderer)
        /// - Hierarchy: parent instance ID, scene path
        /// - Components: list of component type names attached to this GameObject
        ///
        /// To get detailed component data, use <see cref="GetComponentData"/> on individual components.
        /// </remarks>
        /// <param name="go">The GameObject to serialize</param>
        /// <returns>An anonymous object containing serialized GameObject data, or null if go is null</returns>
        public static object GetGameObjectData(GameObject go)
        {
            if (go == null)
                return null;

            // Sync physics so that the bounds of the colliders are updated
            Physics.SyncTransforms();

            // Get the size and center of object based on collider or renderer
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

            return new
            {
                name = go.name,
                instanceID = UnityApiAdapter.GetObjectId(go),
                tag = go.tag,
                layer = go.layer,
                activeSelf = go.activeSelf,
                activeInHierarchy = go.activeInHierarchy,
                isStatic = go.isStatic,
                scenePath = go.scene.path, // Identify which scene it belongs to
                transform = new // Serialize transform components carefully to avoid JSON issues
                {
                    // Serialize Vector3 components individually to prevent self-referencing loops.
                    // The default serializer can struggle with properties like Vector3.normalized.
                    position = new {x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z,},
                    localPosition = new {x = go.transform.localPosition.x, y = go.transform.localPosition.y, z = go.transform.localPosition.z,},
                    rotation = new {x = go.transform.rotation.eulerAngles.x, y = go.transform.rotation.eulerAngles.y, z = go.transform.rotation.eulerAngles.z,},
                    localRotation = new {x = go.transform.localRotation.eulerAngles.x, y = go.transform.localRotation.eulerAngles.y, z = go.transform.localRotation.eulerAngles.z,},
                    scale = new {x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z,},
                    forward = new {x = go.transform.forward.x, y = go.transform.forward.y, z = go.transform.forward.z,},
                    up = new {x = go.transform.up.x, y = go.transform.up.y, z = go.transform.up.z,},
                    right = new {x = go.transform.right.x, y = go.transform.right.y, z = go.transform.right.z,},
                },
                center = new {x = bounds.center.x, y = bounds.center.y, z = bounds.center.z,},
                extents = new {x = bounds.extents.x, y = bounds.extents.y, z = bounds.extents.z,},
                size = new {x = bounds.size.x, y = bounds.size.y, z = bounds.size.z,},
                parentInstanceID = UnityApiAdapter.GetObjectIdOrZero(go.transform.parent?.gameObject),
                // Optionally include components, but can be large
                // components = go.GetComponents<Component>().Select(c => GetComponentData(c)).ToList()
                // Or just component names:
                componentNames = go.GetComponents<Component>()
                    .Select(c => c.GetType().FullName)
                    .ToList(),
            };
        }

        // --- Metadata Caching for Reflection ---
        class CachedMetadata
        {
            public readonly List<PropertyInfo> SerializableProperties;
            public readonly List<FieldInfo> SerializableFields;

            public CachedMetadata(List<PropertyInfo> properties, List<FieldInfo> fields)
            {
                SerializableProperties = properties;
                SerializableFields = fields;
            }
        }

        // Key becomes Tuple<Type, bool>
        static readonly Dictionary<Tuple<Type, bool>, CachedMetadata> _metadataCache = new();

        // --- End Metadata Caching ---

        /// <summary>
        /// Creates a JSON-serializable representation of a Component including its type and all serializable properties/fields.
        /// Uses reflection with caching to extract component data efficiently.
        /// </summary>
        /// <remarks>
        /// The method handles:
        /// - Special cases: Transform and Camera have custom serialization to avoid crashes
        /// - Reflection caching: Metadata is cached per component type for performance
        /// - Property filtering: Skips deprecated/problematic properties (generic shortcuts like .rigidbody, matrix properties)
        /// - Field inclusion: By default includes public fields and private [SerializeField] fields
        /// - Unity type conversion: Uses custom converters for Vector3, Quaternion, Color, etc.
        /// - Error handling: Silently skips properties that fail to serialize
        ///
        /// The returned structure:
        /// - typeName: Full type name of the component
        /// - instanceID: Unity instance ID
        /// - properties: Dictionary of successfully serialized properties and fields
        /// </remarks>
        /// <param name="c">The Component to serialize</param>
        /// <param name="includeNonPublicSerializedFields">If true (default), includes private fields marked with [SerializeField]. If false, only includes public fields</param>
        /// <returns>A dictionary containing serialized component data, or null if component is null</returns>
        public static object GetComponentData(Component c, bool includeNonPublicSerializedFields = true)
        {
            // --- Add Early Logging ---
            // Debug.Log($"[GetComponentData] Starting for component: {c?.GetType()?.FullName ?? "null"} (ID: {c?.GetInstanceID() ?? 0})");
            // --- End Early Logging ---

            if (c == null) return null;
            Type componentType = c.GetType();

            // --- Special handling for Transform to avoid reflection crashes and problematic properties ---
            if (componentType == typeof(Transform))
            {
                Transform tr = c as Transform;

                // Debug.Log($"[GetComponentData] Manually serializing Transform (ID: {tr.GetInstanceID()})");
                return new Dictionary<string, object>
                {
                    {"typeName", componentType.FullName},
                    {"instanceID", UnityApiAdapter.GetObjectId(tr)},

                    // Manually extract known-safe properties. Avoid Quaternion 'rotation' and 'lossyScale'.
                    {"position", CreateTokenFromValue(tr.position, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"localPosition", CreateTokenFromValue(tr.localPosition, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"eulerAngles", CreateTokenFromValue(tr.eulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject()}, // Use Euler angles
                    {"localEulerAngles", CreateTokenFromValue(tr.localEulerAngles, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"localScale", CreateTokenFromValue(tr.localScale, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"right", CreateTokenFromValue(tr.right, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"up", CreateTokenFromValue(tr.up, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"forward", CreateTokenFromValue(tr.forward, typeof(Vector3))?.ToObject<object>() ?? new JObject()},
                    {"parentInstanceID", UnityApiAdapter.GetObjectIdOrZero(tr.parent?.gameObject)},
                    {"rootInstanceID", UnityApiAdapter.GetObjectIdOrZero(tr.root?.gameObject)},
                    {"childCount", tr.childCount},

                    // Include standard Object/Component properties
                    {"name", tr.name},
                    {"tag", tr.tag},
                    {"gameObjectInstanceID", UnityApiAdapter.GetObjectIdOrZero(tr.gameObject)}
                };
            }

            // --- End Special handling for Transform ---

            // --- Special handling for Camera to avoid matrix-related crashes ---
            if (componentType == typeof(Camera))
            {
                Camera cam = c as Camera;
                var cameraProperties = new Dictionary<string, object>();

                // List of safe properties to serialize
                var safeProperties = new Dictionary<string, Func<object>>
                {
                    {"nearClipPlane", () => cam.nearClipPlane},
                    {"farClipPlane", () => cam.farClipPlane},
                    {"fieldOfView", () => cam.fieldOfView},
                    {"renderingPath", () => (int)cam.renderingPath},
                    {"actualRenderingPath", () => (int)cam.actualRenderingPath},
                    {"allowHDR", () => cam.allowHDR},
                    {"allowMSAA", () => cam.allowMSAA},
                    {"allowDynamicResolution", () => cam.allowDynamicResolution},
                    {"forceIntoRenderTexture", () => cam.forceIntoRenderTexture},
                    {"orthographicSize", () => cam.orthographicSize},
                    {"orthographic", () => cam.orthographic},
                    {"opaqueSortMode", () => (int)cam.opaqueSortMode},
                    {"transparencySortMode", () => (int)cam.transparencySortMode},
                    {"depth", () => cam.depth},
                    {"aspect", () => cam.aspect},
                    {"cullingMask", () => cam.cullingMask},
                    {"eventMask", () => cam.eventMask},
                    {"backgroundColor", () => cam.backgroundColor},
                    {"clearFlags", () => (int)cam.clearFlags},
                    {"stereoEnabled", () => cam.stereoEnabled},
                    {"stereoSeparation", () => cam.stereoSeparation},
                    {"stereoConvergence", () => cam.stereoConvergence},
                    {"enabled", () => cam.enabled},
                    {"name", () => cam.name},
                    {"tag", () => cam.tag},
                    {"gameObject", () => new {name = cam.gameObject.name, instanceID = UnityApiAdapter.GetObjectId(cam.gameObject)}}
                };

                foreach (var prop in safeProperties)
                {
                    try
                    {
                        var value = prop.Value();
                        if (value != null)
                        {
                            AddSerializableValue(cameraProperties, prop.Key, value.GetType(), value);
                        }
                    }
                    catch (Exception)
                    {
                        // Silently skip any property that fails
                        continue;
                    }
                }

                return new Dictionary<string, object> {{"typeName", componentType.FullName}, {"instanceID", UnityApiAdapter.GetObjectId(cam)}, {"properties", cameraProperties}};
            }

            // --- End Special handling for Camera ---

            var data = new Dictionary<string, object> {{"typeName", componentType.FullName}, {"instanceID", UnityApiAdapter.GetObjectId(c)}};

            // --- Get Cached or Generate Metadata (using new cache key) ---
            Tuple<Type, bool> cacheKey = new Tuple<Type, bool>(componentType, includeNonPublicSerializedFields);
            if (!_metadataCache.TryGetValue(cacheKey, out CachedMetadata cachedData))
            {
                var propertiesToCache = new List<PropertyInfo>();
                var fieldsToCache = new List<FieldInfo>();

                // Traverse the hierarchy from the component type up to MonoBehaviour
                Type currentType = componentType;
                while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(object))
                {
                    // Get properties declared only at the current type level
                    BindingFlags propFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    foreach (var propInfo in currentType.GetProperties(propFlags))
                    {
                        // Basic filtering (readable, not indexer, not transform which is handled elsewhere)
                        if (!propInfo.CanRead || propInfo.GetIndexParameters().Length > 0 || propInfo.Name == "transform") continue;

                        // Add if not already added (handles overrides - keep the most derived version)
                        if (!propertiesToCache.Any(p => p.Name == propInfo.Name))
                        {
                            propertiesToCache.Add(propInfo);
                        }
                    }

                    // Get fields declared only at the current type level (both public and non-public)
                    BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                    var declaredFields = currentType.GetFields(fieldFlags);

                    // Process the declared Fields for caching
                    foreach (var fieldInfo in declaredFields)
                    {
                        if (fieldInfo.Name.EndsWith("k__BackingField")) continue; // Skip backing fields

                        // Add if not already added (handles hiding - keep the most derived version)
                        if (fieldsToCache.Any(f => f.Name == fieldInfo.Name)) continue;

                        bool shouldInclude = false;
                        if (includeNonPublicSerializedFields)
                        {
                            // If TRUE, include Public OR NonPublic with [SerializeField]
                            shouldInclude = fieldInfo.IsPublic || (fieldInfo.IsPrivate && fieldInfo.IsDefined(typeof(SerializeField), inherit: false));
                        }
                        else // includeNonPublicSerializedFields is FALSE
                        {
                            // If FALSE, include ONLY if it is explicitly Public.
                            shouldInclude = fieldInfo.IsPublic;
                        }

                        if (shouldInclude)
                        {
                            fieldsToCache.Add(fieldInfo);
                        }
                    }

                    // Move to the base type
                    currentType = currentType.BaseType;
                }

                // --- End Hierarchy Traversal ---

                cachedData = new CachedMetadata(propertiesToCache, fieldsToCache);
                _metadataCache[cacheKey] = cachedData; // Add to cache with combined key
            }

            // --- End Get Cached or Generate Metadata ---

            // --- Use cached metadata ---
            var serializablePropertiesOutput = new Dictionary<string, object>();

            // --- Add Logging Before Property Loop ---
            // Debug.Log($"[GetComponentData] Starting property loop for {componentType.Name}...");
            // --- End Logging Before Property Loop ---

            // Use cached properties
            foreach (var propInfo in cachedData.SerializableProperties)
            {
                string propName = propInfo.Name;

                // --- Skip known obsolete/problematic Component shortcut properties ---
                bool skipProperty = false;
                if (propName == "rigidbody" || propName == "rigidbody2D" || propName == "camera" ||
                    propName == "light" || propName == "animation" || propName == "constantForce" ||
                    propName == "renderer" || propName == "audio" || propName == "networkView" ||
                    propName == "collider" || propName == "collider2D" || propName == "hingeJoint" ||
                    propName == "particleSystem" ||
                    propName == "bounds" || propName == "localBounds" ||

                    // Also skip potentially problematic Matrix properties prone to cycles/errors
                    propName == "worldToLocalMatrix" || propName == "localToWorldMatrix")
                {
                    // Debug.Log($"[GetComponentData] Explicitly skipping generic property: {propName}"); // Optional log
                    skipProperty = true;
                }

                // --- End Skip Generic Properties ---

                // --- Skip specific potentially problematic Camera properties ---
                if (componentType == typeof(Camera) &&
                    (propName == "pixelRect" ||
                        propName == "rect" ||
                        propName == "cullingMatrix" ||
                        propName == "useOcclusionCulling" ||
                        propName == "worldToCameraMatrix" ||
                        propName == "projectionMatrix" ||
                        propName == "nonJitteredProjectionMatrix" ||
                        propName == "previousViewProjectionMatrix" ||
                        propName == "cameraToWorldMatrix"))
                {
                    // Debug.Log($"[GetComponentData] Explicitly skipping Camera property: {propName}");
                    skipProperty = true;
                }

                // --- End Skip Camera Properties ---

                // --- Skip specific potentially problematic Transform properties ---
                if (componentType == typeof(Transform) &&
                    (propName == "lossyScale" ||
                        propName == "rotation" ||
                        propName == "worldToLocalMatrix" ||
                        propName == "localToWorldMatrix"))
                {
                    // Debug.Log($"[GetComponentData] Explicitly skipping Transform property: {propName}");
                    skipProperty = true;
                }

                // --- End Skip Transform Properties ---

                // MeshFilter.mesh instantiates a unique mesh in edit mode; use sharedMesh instead.
                if (componentType == typeof(MeshFilter) && propName == "mesh")
                {
                    skipProperty = true;
                }

                // Skip if flagged
                if (skipProperty)
                {
                    continue;
                }

                try
                {
                    // --- Add detailed logging ---
                    // Debug.Log($"[GetComponentData] Accessing: {componentType.Name}.{propName}");
                    // --- End detailed logging ---
                    object value = propInfo.GetValue(c);
                    Type propType = propInfo.PropertyType;
                    AddSerializableValue(serializablePropertiesOutput, propName, propType, value);
                }
                catch (Exception)
                {
                    // Debug.LogWarning($"Could not read property {propName} on {componentType.Name}");
                }
            }

            // --- Add Logging Before Field Loop ---
            // Debug.Log($"[GetComponentData] Starting field loop for {componentType.Name}...");
            // --- End Logging Before Field Loop ---

            // Use cached fields
            foreach (var fieldInfo in cachedData.SerializableFields)
            {
                try
                {
                    // --- Add detailed logging for fields ---
                    // Debug.Log($"[GetComponentData] Accessing Field: {componentType.Name}.{fieldInfo.Name}");
                    // --- End detailed logging for fields ---
                    object value = fieldInfo.GetValue(c);
                    string fieldName = fieldInfo.Name;
                    Type fieldType = fieldInfo.FieldType;
                    AddSerializableValue(serializablePropertiesOutput, fieldName, fieldType, value);
                }
                catch (Exception)
                {
                    // Debug.LogWarning($"Could not read field {fieldInfo.Name} on {componentType.Name}");
                }
            }

            // --- End Use cached metadata ---

            if (serializablePropertiesOutput.Count > 0)
            {
                data["properties"] = serializablePropertiesOutput;
            }

            return data;
        }

        // Helper function to decide how to serialize different types
        static void AddSerializableValue(Dictionary<string, object> dict, string name, Type type, object value)
        {
            // Simplified: Directly use CreateTokenFromValue which uses the serializer
            if (value == null)
            {
                dict[name] = null;
                return;
            }

            try
            {
                // Use the helper that employs our custom serializer settings
                JToken token = CreateTokenFromValue(value, type);
                if (token != null) // Check if serialization succeeded in the helper
                {
                    // Convert JToken back to a basic object structure for the dictionary
                    dict[name] = ConvertJTokenToPlainObject(token);
                }

                // If token is null, it means serialization failed and a warning was logged.
            }
            catch (Exception e)
            {
                // Catch potential errors during JToken conversion or addition to dictionary
                Debug.LogWarning($"[AddSerializableValue] Error processing value for '{name}' (Type: {type.FullName}): {e.Message}. Skipping.");
            }
        }

        // Helper to convert JToken back to basic object structure
        static object ConvertJTokenToPlainObject(JToken token)
        {
            if (token == null) return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                    var objDict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        objDict[prop.Name] = ConvertJTokenToPlainObject(prop.Value);
                    }

                    return objDict;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                    {
                        list.Add(ConvertJTokenToPlainObject(item));
                    }

                    return list;

                case JTokenType.Integer:
                    return token.ToObject<long>(); // Use long for safety
                case JTokenType.Float:
                    return token.ToObject<double>(); // Use double for safety
                case JTokenType.String:
                    return token.ToObject<string>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.Date:
                    return token.ToObject<DateTime>();
                case JTokenType.Guid:
                    return token.ToObject<Guid>();
                case JTokenType.Uri:
                    return token.ToObject<Uri>();
                case JTokenType.TimeSpan:
                    return token.ToObject<TimeSpan>();
                case JTokenType.Bytes:
                    return token.ToObject<byte[]>();
                case JTokenType.Null:
                    return null;
                case JTokenType.Undefined:
                    return null; // Treat undefined as null

                default:
                    // Fallback for simple value types not explicitly listed
                    if (token is JValue jValue && jValue.Value != null)
                    {
                        return jValue.Value;
                    }

                    // Debug.LogWarning($"Unsupported JTokenType encountered: {token.Type}. Returning null.");
                    return null;
            }
        }

        // --- Define custom JsonSerializerSettings for OUTPUT ---
        static readonly JsonSerializerSettings _outputSerializerSettings = new()
        {
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter() // Handles serialization of references
            },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,

            // ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() } // Example if needed
        };
        static readonly JsonSerializer _outputSerializer = JsonSerializer.Create(_outputSerializerSettings);

        // --- End Define custom JsonSerializerSettings ---

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // Helper to create JToken using the output serializer
        static JToken CreateTokenFromValue(object value, Type type)
        {
            return CreateTokenFromValue(value, type, 0, new HashSet<object>(ReferenceComparer.Instance));
        }

        static JToken CreateTokenFromValue(object value, Type type, int depth, HashSet<object> visited)
        {
            if (value == null) return JValue.CreateNull();
            type ??= value.GetType();

            if (value is UnityEngine.Object unityObject)
            {
                return CreateUnityObjectReferenceToken(unityObject);
            }

            if (ShouldSkipComplexType(type))
            {
                return new JObject
                {
                    ["typeName"] = type.FullName,
                    ["omitted"] = "unsupported-complex-type"
                };
            }

            if (IsSimpleSerializableType(type))
            {
                try
                {
                    return JToken.FromObject(value, _outputSerializer);
                }
                catch (JsonSerializationException e)
                {
                    Debug.LogWarning($"[GameObjectSerializer] Newtonsoft.Json Error serializing value of type {type.FullName}: {e.Message}. Skipping property/field.");
                    return null;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GameObjectSerializer] Unexpected error serializing value of type {type.FullName}: {e}. Skipping property/field.");
                    return null;
                }
            }

            if (type.IsValueType)
            {
                return new JObject
                {
                    ["typeName"] = type.FullName,
                    ["omitted"] = "unsupported-value-type"
                };
            }

            if (depth >= MaxSerializationDepth)
            {
                return new JObject
                {
                    ["typeName"] = type.FullName,
                    ["omitted"] = "max-depth"
                };
            }

            if (!type.IsValueType && !visited.Add(value))
            {
                return new JObject
                {
                    ["typeName"] = type.FullName,
                    ["omitted"] = "circular-reference"
                };
            }

            try
            {
                if (value is IDictionary dictionary)
                {
                    return CreateDictionaryToken(dictionary, depth, visited);
                }

                if (value is IEnumerable enumerable && value is not string)
                {
                    return CreateEnumerableToken(enumerable, depth, visited);
                }

                return new JObject
                {
                    ["typeName"] = type.FullName,
                    ["summary"] = SafeToString(value)
                };
            }
            catch (JsonSerializationException e)
            {
                Debug.LogWarning($"[GameObjectSerializer] Newtonsoft.Json Error serializing value of type {type.FullName}: {e.Message}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
            catch (Exception e) // Catch other unexpected errors
            {
                Debug.LogWarning($"[GameObjectSerializer] Unexpected error serializing value of type {type.FullName}: {e}. Skipping property/field.");
                return null; // Indicate serialization failure
            }
            finally
            {
                if (!type.IsValueType)
                {
                    visited.Remove(value);
                }
            }
        }

        static bool IsSimpleSerializableType(Type type)
        {
            if (type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan) ||
                type == typeof(Uri))
            {
                return true;
            }

            Type nullableUnderlyingType = Nullable.GetUnderlyingType(type);
            if (nullableUnderlyingType != null)
            {
                return IsSimpleSerializableType(nullableUnderlyingType);
            }

            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            return type == typeof(Vector2) ||
                   type == typeof(Vector2Int) ||
                   type == typeof(Vector3) ||
                   type == typeof(Vector3Int) ||
                   type == typeof(Vector4) ||
                   type == typeof(Quaternion) ||
                   type == typeof(Color) ||
                   type == typeof(Color32) ||
                   type == typeof(Rect) ||
                   type == typeof(RectInt) ||
                   type == typeof(Bounds) ||
                   type == typeof(BoundsInt) ||
                   type == typeof(LayerMask);
        }

        static bool ShouldSkipComplexType(Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type) ||
                typeof(MemberInfo).IsAssignableFrom(type) ||
                typeof(Type).IsAssignableFrom(type) ||
                typeof(Exception).IsAssignableFrom(type))
            {
                return true;
            }

            string fullName = type.FullName ?? string.Empty;
            return fullName.StartsWith("UnityEditor.", StringComparison.Ordinal) ||
                   fullName.StartsWith("System.Reflection.", StringComparison.Ordinal);
        }

        static JToken CreateEnumerableToken(IEnumerable enumerable, int depth, HashSet<object> visited)
        {
            var array = new JArray();
            int count = 0;
            foreach (object item in enumerable)
            {
                if (count >= MaxCollectionItems)
                {
                    array.Add(JValue.CreateString("<truncated>"));
                    break;
                }

                Type itemType = item?.GetType() ?? typeof(object);
                array.Add(CreateTokenFromValue(item, itemType, depth + 1, visited) ?? JValue.CreateNull());
                count++;
            }

            return array;
        }

        static JToken CreateDictionaryToken(IDictionary dictionary, int depth, HashSet<object> visited)
        {
            var obj = new JObject();
            int count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count >= MaxDictionaryEntries)
                {
                    obj["__truncated__"] = true;
                    break;
                }

                string key = entry.Key?.ToString();
                if (string.IsNullOrEmpty(key))
                {
                    key = $"entry_{count}";
                }

                Type valueType = entry.Value?.GetType() ?? typeof(object);
                obj[key] = CreateTokenFromValue(entry.Value, valueType, depth + 1, visited) ?? JValue.CreateNull();
                count++;
            }

            return obj;
        }

        static JToken CreateUnityObjectReferenceToken(UnityEngine.Object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            var token = new JObject
            {
                ["name"] = value.name,
                ["instanceID"] = JToken.FromObject(UnityApiAdapter.GetObjectId(value)),
                ["typeName"] = value.GetType().FullName
            };

#if UNITY_EDITOR
            if (AssetDatabase.Contains(value))
            {
                string assetPath = AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    token["assetPath"] = assetPath;
                }
            }
#endif

            return token;
        }

        static string SafeToString(object value)
        {
            try
            {
                string text = value?.ToString() ?? string.Empty;
                if (text.Length > 160)
                {
                    return text.Substring(0, 160) + "...";
                }

                return text;
            }
            catch
            {
                return "<unavailable>";
            }
        }
    }
}
