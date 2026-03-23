using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record ApiOptions(Store store, string slice = Api.DefaultSlice, int? keepUnusedDataFor = null);
    record CacheSelectorInfo(string slice, ApiCacheKey key);
}
