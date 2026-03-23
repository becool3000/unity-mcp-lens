using System;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    [Serializable]
    record ApiDefaultKey(object queryArgs, string label, int endpointId) : ApiCacheKey
    {
        [field: SerializeField] public int endpointId { get; set; } = endpointId;
        [field: SerializeField] public string label { get; set; } = label;
        [field: SerializeReference] public object queryArgs { get; set; } = queryArgs;
    }
}
