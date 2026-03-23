using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    [Serializable]
    record EndpointCacheItem
    {
        public EndpointResult result = new();
        public List<string> tags;
        public QueryType queryType;

        // On-going operation
        public EndpointOperation operation { get; set; }
        public int Subscribers { get; set; }    // Don't keep through domain reloads as all subscribers will no longer exist
    }
}
