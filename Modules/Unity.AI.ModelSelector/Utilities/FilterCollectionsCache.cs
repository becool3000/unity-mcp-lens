using System.Collections.Generic;
using System.Linq;
using Unity.AI.ModelSelector.Services.Stores.States;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    static class FilterCollectionsCache
    {
        static readonly Dictionary<int, List<KeyValuePair<IReadOnlyList<string>, HashSet<string>>>> k_StringHashSetCache = new();
        static readonly Dictionary<int, List<KeyValuePair<IReadOnlyList<MiscModelType>, HashSet<MiscModelType>>>> k_MiscModelTypeHashSetCache = new();
        static readonly Dictionary<int, List<KeyValuePair<IReadOnlyList<string>, Dictionary<string, string>>>> k_BaseModelsDictCache = new();
        static readonly Dictionary<string, ModelSettings> k_BaseModelLookupCache = new();

        public static IDictionary<string, ModelSettings> BaseModelLookupCache => k_BaseModelLookupCache;

        public static HashSet<T> GetOrCreateHashSet<T>(IEnumerable<T> collection)
        {
            if (collection == null) return null;

            var hashCode = GetCollectionHashCode(collection);
            var collectionList = collection.ToList();

            if (typeof(T) == typeof(string))
            {
                if (k_StringHashSetCache.TryGetValue(hashCode, out var cachedEntries))
                {
                    foreach (var entry in cachedEntries)
                    {
                        if (CollectionsEqual(entry.Key, collectionList.Cast<string>()))
                            return (HashSet<T>)(object)entry.Value;
                    }
                }

                var stringSet = new HashSet<string>();
                foreach (var item in collectionList)
                    stringSet.Add((string)(object)item);

                if (stringSet.Count > 0)
                {
                    var stringList = collectionList.Cast<string>().ToList();
                    if (!k_StringHashSetCache.ContainsKey(hashCode))
                        k_StringHashSetCache[hashCode] = new List<KeyValuePair<IReadOnlyList<string>, HashSet<string>>>();

                    k_StringHashSetCache[hashCode].Add(new KeyValuePair<IReadOnlyList<string>, HashSet<string>>(stringList, stringSet));
                    return (HashSet<T>)(object)stringSet;
                }
                return null;
            }

            if (typeof(T) == typeof(MiscModelType))
            {
                if (k_MiscModelTypeHashSetCache.TryGetValue(hashCode, out var cachedEntries))
                {
                    foreach (var entry in cachedEntries)
                    {
                        if (CollectionsEqual(entry.Key, collectionList.Cast<MiscModelType>()))
                            return (HashSet<T>)(object)entry.Value;
                    }
                }

                var miscSet = new HashSet<MiscModelType>();
                foreach (var item in collectionList)
                    miscSet.Add((MiscModelType)(object)item);

                if (miscSet.Count > 0)
                {
                    var miscList = collectionList.Cast<MiscModelType>().ToList();
                    if (!k_MiscModelTypeHashSetCache.ContainsKey(hashCode))
                        k_MiscModelTypeHashSetCache[hashCode] = new List<KeyValuePair<IReadOnlyList<MiscModelType>, HashSet<MiscModelType>>>();

                    k_MiscModelTypeHashSetCache[hashCode].Add(new KeyValuePair<IReadOnlyList<MiscModelType>, HashSet<MiscModelType>>(miscList, miscSet));
                    return (HashSet<T>)(object)miscSet;
                }
                return null;
            }

            var hashSet = new HashSet<T>();
            foreach (var item in collectionList)
                hashSet.Add(item);
            return hashSet.Count > 0 ? hashSet : null;
        }

        public static Dictionary<string, string> GetOrCreateBaseModelsDict(IEnumerable<string> baseModelIds, IEnumerable<ModelSettings> allModels)
        {
            if (baseModelIds == null) return null;

            var hashCode = GetCollectionHashCode(baseModelIds);
            var baseModelIdsList = baseModelIds.ToList();

            if (k_BaseModelsDictCache.TryGetValue(hashCode, out var cachedEntries))
            {
                foreach (var entry in cachedEntries)
                {
                    if (CollectionsEqual(entry.Key, baseModelIdsList))
                        return entry.Value;
                }
            }

            var modelLookupDict = allModels.ToDictionary(m => m.id, m => m);

            var dict = new Dictionary<string, string>();
            foreach (var id in baseModelIdsList)
            {
                if (!k_BaseModelLookupCache.TryGetValue(id, out var model))
                {
                    modelLookupDict.TryGetValue(id, out model);
                    if (model != null)
                        k_BaseModelLookupCache[id] = model;
                }
                dict[id] = model?.name ?? string.Empty;
            }

            if (dict.Count > 0)
            {
                if (!k_BaseModelsDictCache.ContainsKey(hashCode))
                    k_BaseModelsDictCache[hashCode] = new List<KeyValuePair<IReadOnlyList<string>, Dictionary<string, string>>>();

                k_BaseModelsDictCache[hashCode].Add(new KeyValuePair<IReadOnlyList<string>, Dictionary<string, string>>(baseModelIdsList, dict));
                return dict;
            }
            return null;
        }

        static int GetCollectionHashCode<T>(IEnumerable<T> collection)
        {
            unchecked
            {
                var hash = 17;
                foreach (var item in collection)
                {
                    hash = hash * 31 + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        static bool CollectionsEqual<T>(IReadOnlyList<T> collection1, IEnumerable<T> collection2)
        {
            if (collection1 == null && collection2 == null) return true;
            if (collection1 == null || collection2 == null) return false;

            var list2 = collection2.ToList();
            if (collection1.Count != list2.Count) return false;

            for (int i = 0; i < collection1.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(collection1[i], list2[i]))
                    return false;
            }
            return true;
        }

        public static void ClearCaches()
        {
            k_StringHashSetCache.Clear();
            k_MiscModelTypeHashSetCache.Clear();
            k_BaseModelsDictCache.Clear();
            k_BaseModelLookupCache.Clear();
        }
    }
}
