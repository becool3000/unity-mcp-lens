using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    /// <summary>
    /// Ensure cache key survive domain reload by always serializing them as Json.
    ///
    /// Ensures the cache key can easily be cloned in a reducer with `with {}`
    /// </summary>
    [Serializable]
    record ApiCacheKey
    {
//        public string json;
//        public ApiCacheKey(object key) => json = JsonConvert.SerializeObject(key);
    }
}
