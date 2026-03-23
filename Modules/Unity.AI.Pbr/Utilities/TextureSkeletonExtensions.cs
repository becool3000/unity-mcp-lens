using System;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    record TextureSkeleton(int taskID, int counter) : TextureResult(new Uri($"{SkeletonExtensions.skeletonUriPath}/{taskID}/{counter}", UriKind.Absolute));
}
