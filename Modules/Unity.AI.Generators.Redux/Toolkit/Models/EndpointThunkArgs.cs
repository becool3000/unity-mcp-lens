using System;
using UnityEngine;


namespace Unity.AI.Generators.Redux.Toolkit
{
    record EndpointThunkArgs<TArgs>(ApiCacheKey key, TArgs args);
}
