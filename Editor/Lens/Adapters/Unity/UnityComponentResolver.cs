#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity
{
    /// <summary>
    /// Resolves Unity component types and shared component metadata without depending on tool-layer classes.
    /// </summary>
    static class UnityComponentResolver
    {
        static readonly Dictionary<string, Type> CacheByFqn = new(StringComparer.Ordinal);
        static readonly Dictionary<string, Type> CacheByName = new(StringComparer.Ordinal);
        static readonly Dictionary<string, List<string>> PropertySuggestionCache = new();

        public static Type FindType(string typeName)
        {
            if (TryResolve(typeName, out Type resolvedType, out string error))
                return resolvedType;

            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning($"[FindType] {error}");

            return null;
        }

        /// <summary>
        /// Resolve a Component/MonoBehaviour type by short or fully-qualified name.
        /// Prefers runtime (Player) script assemblies; falls back to Editor assemblies.
        /// Never uses Assembly.LoadFrom.
        /// </summary>
        public static bool TryResolve(string nameOrFullName, out Type type, out string error)
        {
            error = string.Empty;
            type = null;

            if (string.IsNullOrWhiteSpace(nameOrFullName))
            {
                error = "Component name cannot be null or empty";
                return false;
            }

            if (CacheByFqn.TryGetValue(nameOrFullName, out type))
                return true;

            if (!nameOrFullName.Contains(".") && CacheByName.TryGetValue(nameOrFullName, out type))
                return true;

            type = Type.GetType(nameOrFullName, throwOnError: false);
            if (IsValidComponent(type))
            {
                Cache(type);
                return true;
            }

            var candidates = FindCandidates(nameOrFullName);
            if (candidates.Count == 1)
            {
                type = candidates[0];
                Cache(type);
                return true;
            }

            if (candidates.Count > 1)
            {
                error = Ambiguity(nameOrFullName, candidates);
                type = null;
                return false;
            }

#if UNITY_EDITOR
            var typeCacheCandidates = TypeCache.GetTypesDerivedFrom<Component>()
                .Where(t => NamesMatch(t, nameOrFullName));

            candidates = PreferPlayer(typeCacheCandidates).ToList();
            if (candidates.Count == 1)
            {
                type = candidates[0];
                Cache(type);
                return true;
            }

            if (candidates.Count > 1)
            {
                error = Ambiguity(nameOrFullName, candidates);
                type = null;
                return false;
            }
#endif

            error = $"Component type '{nameOrFullName}' not found in loaded runtime assemblies. " +
                "Use a fully-qualified name (Namespace.TypeName) and ensure the script compiled.";
            type = null;
            return false;
        }

        static bool NamesMatch(Type type, string query)
        {
            return type.Name.Equals(query, StringComparison.Ordinal)
                || (type.FullName?.Equals(query, StringComparison.Ordinal) ?? false);
        }

        static bool IsValidComponent(Type type)
        {
            return type != null && typeof(Component).IsAssignableFrom(type);
        }

        static void Cache(Type type)
        {
            if (type.FullName != null)
                CacheByFqn[type.FullName] = type;

            CacheByName[type.Name] = type;
        }

        static List<Type> FindCandidates(string query)
        {
            bool isShort = !query.Contains('.');
            var loaded = AppDomain.CurrentDomain.GetAssemblies();

#if UNITY_EDITOR
            var playerAssemblyNames = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(assembly => assembly.name),
                StringComparer.Ordinal);

            IEnumerable<Assembly> playerAssemblies = loaded.Where(assembly => playerAssemblyNames.Contains(assembly.GetName().Name));
            IEnumerable<Assembly> editorAssemblies = loaded.Except(playerAssemblies);
#else
            IEnumerable<Assembly> playerAssemblies = loaded;
            IEnumerable<Assembly> editorAssemblies = Array.Empty<Assembly>();
#endif
            static IEnumerable<Type> SafeGetTypes(Assembly assembly)
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    return exception.Types.Where(type => type != null);
                }
            }

            Func<Type, bool> match = isShort
                ? type => type.Name.Equals(query, StringComparison.Ordinal)
                : type => type.FullName != null && type.FullName.Equals(query, StringComparison.Ordinal);

            var fromPlayer = playerAssemblies.SelectMany(SafeGetTypes)
                .Where(IsValidComponent)
                .Where(match);
            var fromEditor = editorAssemblies.SelectMany(SafeGetTypes)
                .Where(IsValidComponent)
                .Where(match);

            var results = new List<Type>(fromPlayer);
            if (results.Count == 0)
                results.AddRange(fromEditor);

            return results;
        }

#if UNITY_EDITOR
        static IEnumerable<Type> PreferPlayer(IEnumerable<Type> sequence)
        {
            var playerAssemblyNames = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(assembly => assembly.name),
                StringComparer.Ordinal);

            return sequence.OrderBy(type => playerAssemblyNames.Contains(type.Assembly.GetName().Name) ? 0 : 1);
        }
#endif

        static string Ambiguity(string query, IEnumerable<Type> candidates)
        {
            var lines = candidates.Select(type => $"{type.FullName} (assembly {type.Assembly.GetName().Name})");
            return $"Multiple component types matched '{query}':\n - " + string.Join("\n - ", lines) +
                "\nProvide a fully qualified type name to disambiguate.";
        }

        public static List<string> GetAllComponentProperties(Type componentType)
        {
            if (componentType == null)
                return new List<string>();

            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.CanWrite)
                .Select(property => property.Name);

            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => !field.IsInitOnly && !field.IsLiteral)
                .Select(field => field.Name);

            var serializeFields = componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => field.GetCustomAttribute<SerializeField>() != null)
                .Select(field => field.Name);

            return properties.Concat(fields).Concat(serializeFields).Distinct().OrderBy(name => name).ToList();
        }

        public static List<string> GetAIPropertySuggestions(string userInput, List<string> availableProperties)
        {
            if (string.IsNullOrWhiteSpace(userInput) || !availableProperties.Any())
                return new List<string>();

            string cacheKey = $"{userInput.ToLowerInvariant()}:{string.Join(",", availableProperties)}";
            if (PropertySuggestionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var suggestions = GetRuleBasedSuggestions(userInput, availableProperties);
                PropertySuggestionCache[cacheKey] = suggestions;
                return suggestions;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AI Property Matching] Error getting suggestions for '{userInput}': {ex.Message}");
                return new List<string>();
            }
        }

        static List<string> GetRuleBasedSuggestions(string userInput, List<string> availableProperties)
        {
            var suggestions = new List<string>();
            string cleanedInput = CleanForComparison(userInput);

            foreach (string property in availableProperties)
            {
                string cleanedProperty = CleanForComparison(property);

                if (cleanedProperty == cleanedInput)
                {
                    suggestions.Add(property);
                    continue;
                }

                var inputWords = userInput.ToLowerInvariant().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (inputWords.All(word => cleanedProperty.Contains(word.ToLowerInvariant())))
                {
                    suggestions.Add(property);
                    continue;
                }

                if (LevenshteinDistance(cleanedInput, cleanedProperty) <= Math.Max(2, cleanedInput.Length / 4))
                    suggestions.Add(property);
            }

            return suggestions.OrderBy(suggestion => LevenshteinDistance(cleanedInput, CleanForComparison(suggestion)))
                .Take(3)
                .ToList();
        }

        static string CleanForComparison(string value)
        {
            return value.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        }

        static int LevenshteinDistance(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
                return second?.Length ?? 0;

            if (string.IsNullOrEmpty(second))
                return first.Length;

            var matrix = new int[first.Length + 1, second.Length + 1];

            for (int i = 0; i <= first.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= second.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= first.Length; i++)
            {
                for (int j = 1; j <= second.Length; j++)
                {
                    int cost = second[j - 1] == first[i - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[first.Length, second.Length];
        }

        public static Vector3 GetObjectWorldCenter(GameObject targetGo)
        {
            if (targetGo.TryGetComponent<Collider>(out var collider))
                return collider.bounds.center;

            if (targetGo.TryGetComponent<MeshRenderer>(out var meshRenderer))
                return meshRenderer.bounds.center;

            return targetGo.transform.position;
        }
    }
}
