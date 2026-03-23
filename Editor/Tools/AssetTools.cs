using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit.Compliance;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.Pool;
using Unity.AI.Search.Editor.Knowledge;
using AssetKnowledge = Unity.AI.Assistant.Editor.AssetKnowledge;


namespace Unity.AI.Assistant.Tools.Editor
{
    class AssetTools
    {
        const string k_FindProjectAssetsFunctionId = "Unity.FindProjectAssets";
        const string k_GetTextAssetContentFunctionId = "Unity.GetTextAssetContent";
        const string k_GetImageAssetContentFunctionId = "Unity.GetImageAssetContent";
        const string k_GetAssetLabelsFunctionId = "Unity.GetAssetLabels";

        const string k_UnityAILabel = Legal.UnityAIGeneratedLabel;

        [Serializable]
        internal class InstanceInfo
        {
            public string Name = null;
            public Type Type = null;
            public int InstanceID = 0;
            public string Tags;
            public float Similarity = -1f; // Optional semantic similarity (0..1)
            public bool HasKeywordMatch = false; // Whether this asset was returned by Unity Search (keyword match)
        }

        [Serializable]
        internal class AssetInfo
        {
            public InstanceInfo MainAsset = null;
            public List<InstanceInfo> SubAssets = new();
        }

        [Serializable]
        internal class AssetFolder
        {
            public string Name = null;
            public List<AssetFolder> Children = new();
            public List<AssetInfo> Assets = new();
        }

        [Serializable]
        internal class AssetHierarchy
        {
            public List<AssetFolder> Roots = new();
        }

        [Serializable]
        public class FindProjectAssetsOutput
        {
            public string Hierarchy = string.Empty;
            public string Info = string.Empty;
            public string ResponseGuidance = string.Empty;
        }

        [Serializable]
        public class GetTextAssetContentOutput
        {
            public string Data = string.Empty;

            public string Info = string.Empty;
        }

        [Serializable]
        public class GetAssetLabelsOutput
        {
            public List<string> Labels = new();
        }

        const string k_SearchProviderId = "asset";
        const int k_MaxAssetsPerCall = 50;
        const int k_MaxCharactersPerCall = 16384;

        [AgentTool(
            "Find project assets using name matching AND semantic search based on visual content. " +
 		    "IMPORTANT: The output includes 'ResponseGuidance' - follow these instructions when presenting results to users.",
            k_FindProjectAssetsFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Available)]
        internal static async Task<FindProjectAssetsOutput> FindProjectAssets(
            [Parameter("Optional: a search query to search for specific assets." +
                "Keywords perform both name matching and semantic search based on visuals. " +
                "The elements of the filter are separated by a space.\n" +
                " - Use no keyword to filter assets by name (without extension). Words separated by a space are treated as separate name searches.\n" +
                " - Use the 't' prefix to filter by built-in type such as 't:Texture2D', 't:Material', 't:MonoScript' or 't:GameObject', or by user custom types such as 't:MyCustomType'. DO NOT use 't:MonoBehaviour'. Use 't:StyleSheet' to find uss asset and 't:VisualTreeAsset' to find uxml asset.\n" +
                " - Use the 'dir' prefix to restrict the search to specific folders, such as 'dir:Assets/Models' or 'dir:Textures'.\n" +
                " - Use the 'l' prefix to filter by asset labels, such as 'l:" + k_UnityAILabel + "' to find AI-generated assets, or any other label names defined in the project.\n" +
                " - Use '-' in front of any keyword or the name filter to exclude object matching that filter, for instance '-t:Shader' will exclude shaders, or '-l:" + k_UnityAILabel + "' will exclude AI-generated assets.\n" +
                " - After the prefix, use ':' for a partial match or use '=' (exact match), '!=', '>', '<', '<=', '>=' operators to check the value, like 't:texture size<256'.\n" +
                " - Use 'or' and 'and' and grouping with '(' and ')' for more complex queries like: 't:texture and (size=64 or size=32)'\n" +
                " - Elements separated by a space are considered like a 'and' constraint, i.e. they must all match.\n" +
                "For instance, the filter '(car or truck) t:Texture2D t:GameObject' will look for assets whose names contains or visually look like EITHER 'car' or 'truck' and which are BOTH a Texture2D AND a GameObject. " +
                " - Use 'k:N' to limit semantic results to top N matches (e.g., 'dragon k:5' returns top 5). Default is 50.\n" +
                "Examples: 'cat t:Prefab' (semantic cat prefabs), 'red car k:3' (top 3 red cars), 'sword dir:Assets/Weapons' (swords in folder), 'stone t:Material' (stone materials)." +
                "Use a wildcard '*' to get all the project assets without any filtering.")]
            string query = "",

            [Parameter("Incomplete (truncated) results will return a positive index. When this is the case, use this index here to get the next results.")]
            int startIndex = 0
        )
        {
            if (startIndex < 0)
                throw new ArgumentException("The start index must be positive or zero.");

            var result = new AssetHierarchy();
            InternalLog.Log($"Search query: {query}");

            using var pooledAssetPaths = ListPool<string>.Get(out var assetPaths);
            using var pooledSemanticScores = DictionaryPool<string, float>.Get(out var semanticScores);
            using var pooledKeywordMatches = HashSetPool<string>.Get(out var keywordMatches);

            var totalCount = 0;
            var endIndex = 0;

            var useAssetKnowledge = AssetKnowledge.AssetKnowledgeUsable;

            // Wait for AssetKnowledge readiness only if enabled and ready:
            if (useAssetKnowledge)
            {
                await KnowledgeSearchProvider.WaitForReadinessAsync();
            }

            // Get all assets
            if (string.IsNullOrEmpty(query) || query == "*")
            {
                var guids = AssetDatabase.FindAssets("", new[] { "Assets" });

                // Pagination logic
                totalCount = guids.Length;
                var itemCount = Mathf.Min(k_MaxAssetsPerCall, totalCount - startIndex);
                endIndex = startIndex + itemCount;

                for (var i = startIndex; i < endIndex; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(assetPath))
                        continue;

                    assetPaths.Add(assetPath);
                }
            }
            // Do an asset search
            else
            {
                // Use BOTH providers:
                // - asset provider for name/path matching
                // - KnowledgeSearchProvider for semantic (visual/content) matching
                // The agent can then present both types of matches
                var providers = new List<SearchProvider>
                {
                    SearchService.GetProvider(k_SearchProviderId)
                };

                // Add semantic search provider only if AssetKnowledge is enabled
                if (useAssetKnowledge)
                {
                    var knowledgeProvider = KnowledgeSearchProvider.CreateProvider();
                    // KnowledgeSearchProvider supports additional custom parameters like k:N that we
                    // do not want to pass through the main context for other providers, so set the original query here:
                    knowledgeProvider.SetOriginalQuery(query);
                    providers.Add(knowledgeProvider);
                }

                // Strip custom parameters (like k:N) that Unity Search doesn't understand
                // but keep Unity Search filters (t:, dir:, etc.)
                var cleanedQuery = AssetSearchUtils.StripCustomParameters(query);

                using var context = SearchService.CreateContext(providers, cleanedQuery);
                var tcs = new TaskCompletionSource<IList<SearchItem>>();
                SearchService.Request(context, onSearchCompleted: (_, items) =>
                {
                    tcs.SetResult(items);
                });

                var searchResults = await tcs.Task;

                // Look up cached similarity scores to sort ALL results before pagination
                // This ensures high-relevance assets appear on first page
                var cachedScoresForSorting = useAssetKnowledge
                    ? KnowledgeSearchProvider.GetAllCachedSimilarities()
                    : null;

                List<SearchItem> sortedResults;

                if (cachedScoresForSorting != null)
                {
                    // Sort all results by similarity (descending) before pagination
                    sortedResults = searchResults
                        .OrderByDescending(item => TryGetAssetPathFromSearchResult(item, out var path) &&
                                                   cachedScoresForSorting.TryGetValue(path, out var score)
                            ? score
                            : -1f)
                        .ToList();

                    InternalLog.Log($"Sorted {sortedResults.Count} search results by similarity before pagination");
                }
                else
                {
                    // No cache available, use original order
                    sortedResults = searchResults.ToList();
                }

                // Pagination logic (now on sorted results)
                totalCount = sortedResults.Count;

                var itemCount = Mathf.Min(k_MaxAssetsPerCall, totalCount - startIndex);
                var items = sortedResults.GetRange(startIndex, itemCount);
                endIndex = startIndex + items.Count;

                foreach (var item in items)
                {
                    if (!TryGetAssetPathFromSearchResult(item, out var assetPath))
                        continue;

                    assetPaths.Add(assetPath);
                    AssetSearchUtils.TrackAssetProvider(item, assetPath, keywordMatches, semanticScores);
                }
            }

            // Look up cached similarity scores from KnowledgeSearchProvider
            // KnowledgeSearchProvider caches scores from its last search, so we can reuse them
            // without rescanning all assets
            var cachedScores = useAssetKnowledge
                ? KnowledgeSearchProvider.GetAllCachedSimilarities()
                : null;

            if (cachedScores != null)
            {
                foreach (var assetPath in assetPaths)
                {
                    // Skip if we already have a score from semantic search provider
                    if (semanticScores.ContainsKey(assetPath))
                        continue;

                    // Look up cached score from KnowledgeSearchProvider
                    if (cachedScores.TryGetValue(assetPath, out var similarity))
                    {
                        semanticScores[assetPath] = similarity;
                    }
                    // If not in cache, similarity wasn't in top 1000 (very low relevance)
                }

                InternalLog.Log($"Retrieved {semanticScores.Count} similarity scores from cache");
            }

            var processedAssetPaths = new HashSet<string>();
            var rootFolders = new Dictionary<string, AssetFolder>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetPath in assetPaths)
            {
                // Only process each asset path once (because a sub-asset will have the same asset path as the main asset)
                if (!processedAssetPaths.Add(assetPath))
                    continue;

                var pathParts = assetPath.Split('/');
                if (pathParts.Length == 0)
                    continue;

                var rootName = pathParts[0];
                if (!rootFolders.TryGetValue(rootName, out var rootFolder))
                {
                    rootFolder = new AssetFolder { Name = rootName };
                    rootFolders[rootName] = rootFolder;
                }

                var folder = FileUtils.GetOrCreateFolder(rootFolder, pathParts, 1, assetPath);

                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    continue;
                }

                // Load main asset
                var mainObj = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainObj == null)
                    continue;

                // Get semantic tags from embeddings (if available)
                var tags = useAssetKnowledge
                    ? KnowledgeSearchProvider.GetTags(mainObj)
                    : string.Empty;

                var assetInfo = new AssetInfo
                {
                    MainAsset = new InstanceInfo
                    {
                        Name = Path.GetFileName(assetPath),
                        Type = mainObj.GetType(),
                        InstanceID = mainObj.GetInstanceID(),
                        Tags = tags ?? string.Empty,
                        Similarity = semanticScores.GetValueOrDefault(assetPath, -1),
                        HasKeywordMatch = keywordMatches.Contains(assetPath)
                    }
                };

                // Load sub-assets
                var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
                foreach (var sub in subAssets)
                {
                    if (sub == null)
                        continue;

                    // Do not set Tags for sub assets, they are embedded with the parent.

                    assetInfo.SubAssets.Add(new InstanceInfo
                    {
                        Name = sub.name,
                        Type = sub.GetType(),
                        InstanceID = sub.GetInstanceID()
                    });
                }

                folder.Assets.Add(assetInfo);
            }

            result.Roots = rootFolders.Values.ToList();

            if (useAssetKnowledge)
            {
                // Sort assets within each folder by similarity (descending) to show most relevant first
                // This ensures high-similarity assets appear before low-similarity ones within each folder
                foreach (var root in result.Roots)
                    AssetHierarchyUtils.SortFolderAssetsBySimilarity(root);
            }

            var nextPageIndex = endIndex < totalCount ? endIndex : -1;
            var hierarchyPayload = AssetResultMarkdownExporter.ToMarkdownTree(result);
            var info = nextPageIndex != -1 ? $"Incomplete result. Use {nameof(startIndex)}={nextPageIndex} to get the next ones." : string.Empty;

            var formattedOutput = new FindProjectAssetsOutput
            {
                Hierarchy = hierarchyPayload,
                Info = info,

                // Generate response guidance based on match quality distribution (only with semantic search)
                ResponseGuidance = useAssetKnowledge
                    ? AssetHierarchyUtils.GenerateResponseGuidance(result)
                    : string.Empty
            };

            InternalLog.Log($"{formattedOutput.Hierarchy}\n\n{formattedOutput.Info}\n\n{formattedOutput.ResponseGuidance}");

            return formattedOutput;
        }

        internal static bool TryGetAssetPathFromSearchResult(SearchItem item, out string assetPath)
        {
            // Results can contain global ids or asset paths.
            // KnowledgeSearchProvider only uses asset paths because retrieving the GlobalObjectId of
            // an asset requires us to load it first, which would make the search slow just to get IDs
            // that we then convert to asset paths anyway.
            var assetPathOrGlobalObjectId = item.value as string;

            if (!string.IsNullOrEmpty(assetPathOrGlobalObjectId))
            {
                // Check if this is a valid asset path:
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPathOrGlobalObjectId)))
                {
                    assetPath = assetPathOrGlobalObjectId;
                }
                else
                {
                    assetPath = GlobalObjectId.TryParse(assetPathOrGlobalObjectId, out var globalId)
                        ? AssetDatabase.GUIDToAssetPath(globalId.assetGUID)
                        : null;
                }
            }
            else
            {
                assetPath = null;
            }

            return !string.IsNullOrEmpty(assetPath);
        }

        [AgentTool(
            "Get the text content of the asset with the given instance ID. " +
            "This is for text-based assets only, such as C# scripts, shaders, txt, json, uxml, uss, etc.",
            k_GetTextAssetContentFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static async Task<GetTextAssetContentOutput> GetTextAssetContent(
            ToolExecutionContext context,
            [Parameter("The instance ID of the asset to extract content from.")]
            int instanceID,

            [Parameter("Optional: Use this to get the next part of the content if the previous call was incomplete.")]
            int startIndex = 0
        )
        {
            if (startIndex < 0)
                throw new ArgumentException("The start index must be positive or zero.");

#if UNITY_6000_3_OR_NEWER
            var assetPath = AssetDatabase.GetAssetPath((EntityId)instanceID);
#else
            var assetPath = AssetDatabase.GetAssetPath(EditorUtility.InstanceIDToObject(instanceID));
#endif
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Cannot find asset with ID: {instanceID}");

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, assetPath);

#if UNITY_6000_3_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(instanceID);
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID);
#endif

            string fileContent = null;

            // If this is a sub-asset, extract content directly
            if (obj != null)
            {
                switch (obj)
                {
                    case TextAsset textAsset:
                        fileContent = textAsset.text;
                        break;

                    case MonoBehaviour monoBehaviour:
                    {
                        var monoScript = MonoScript.FromMonoBehaviour(monoBehaviour);
                        fileContent = monoScript.text;
                        break;
                    }

                    default:
                    {
                        if (AssetDatabase.IsSubAsset(obj))
                            throw new ArgumentException($"Sub-asset type '{obj.GetType().Name}' is not supported for content extraction.");
                        break;
                    }
                }
            }

            // Read the actual file if this is not a text asset and this is a main asset
            if (fileContent == null)
            {
                // But first quickly check if this is a text asset or not
                if (!FileUtils.IsTextFile(assetPath))
                    throw new ArgumentException($"Asset does not appear to be a text asset.");

                fileContent = File.ReadAllText(assetPath);
            }

            if (startIndex >= fileContent.Length)
                throw new ArgumentException($"Start index {{startIndex}} is out of range.");

            // Paging logic
            var charsRead = 0;
            var nextPageIndex = -1;
            var payloadBuilder = new System.Text.StringBuilder();
            using (var reader = new StringReader(fileContent.Substring(startIndex)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var lineLengthWithNewline = line.Length + 1;

                    // If adding this line would exceed max, truncate
                    if (charsRead + lineLengthWithNewline > k_MaxCharactersPerCall)
                    {
                        // Unless it's the first/only line
                        if (charsRead == 0)
                        {
                            payloadBuilder.Append(line);
                            charsRead += line.Length;
                            nextPageIndex = startIndex + charsRead;
                        }
                        break;
                    }

                    payloadBuilder.AppendLine(line);
                    charsRead += lineLengthWithNewline;
                    nextPageIndex = startIndex + charsRead;
                }
            }

            // If we've reached the end, no more paging needed
            if (startIndex + charsRead >= fileContent.Length)
                nextPageIndex = -1;

            // Build payload
            var payload = payloadBuilder.ToString();
            var info = nextPageIndex != -1 ? $"Incomplete result. Use {nameof(startIndex)}={nextPageIndex} to get the next ones." : string.Empty;

            var formattedOutput = new GetTextAssetContentOutput
            {
                Data = payload,
                Info = info
            };

            InternalLog.Log($"{formattedOutput.Data}\n\n{formattedOutput.Info}");

            return formattedOutput;
        }

        [AgentTool(
            "Get the image content of the asset with the given instance ID. " +
            "This is for image type assets only (any type inheriting from Texture, like Texture2D). " +
            "Only use when answering the user request really requires looking into the image content of the asset.",
            k_GetImageAssetContentFunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static ImageOutput GetImageAssetContent(
            [Parameter("The instance ID of the image asset. It must be a valid instance ID.")]
            int instanceID
        )
        {
#if UNITY_6000_3_OR_NEWER
            var assetPath = AssetDatabase.GetAssetPath((EntityId)instanceID);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Cannot find asset with ID: {instanceID}");

            var obj = EditorUtility.EntityIdToObject(instanceID);
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Cannot find asset with ID: {instanceID}");
#endif
            if (obj is not Texture texture)
                throw new ArgumentException($"Asset must be of a type inheriting Texture");

            var description = $"This is the image content of '{obj.name}' (instance ID: {instanceID})";
            return new ImageOutput(texture, description: description, displayName: obj.name);
        }

        [AgentTool("Get the labels assigned to an asset. " +
            "Use this to retrieve the list of labels for a specific asset that was found for example when finding project assets or when we already know its instanceID. " +
            "If we search through the project use the find project assets tool instead to find assets with specific labels.",
            k_GetAssetLabelsFunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static GetAssetLabelsOutput GetAssetLabels(
            [Parameter("The instance ID of the asset. This can be obtained from FindProjectAssets results.")]
            int instanceID
        )
        {
#if UNITY_6000_3_OR_NEWER
            var assetPath = AssetDatabase.GetAssetPath((EntityId)instanceID);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Cannot find asset with ID: {instanceID}");

            var obj = EditorUtility.EntityIdToObject(instanceID);
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID);
            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Cannot find asset with ID: {instanceID}");
#endif
            if (obj == null)
                throw new ArgumentException($"Cannot load asset with ID: {instanceID}");

            var labels = AssetDatabase.GetLabels(obj);
            var output = new GetAssetLabelsOutput
            {
                Labels = new List<string>(labels)
            };

            return output;
        }
    }
}
