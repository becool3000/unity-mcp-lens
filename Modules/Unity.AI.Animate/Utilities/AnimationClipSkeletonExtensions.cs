using System;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Animate.Services.Utilities
{
    record AnimationClipSkeleton(int taskID, int counter) : AnimationClipResult(new Uri($"{SkeletonExtensions.skeletonUriPath}/{taskID}/{counter}", UriKind.Absolute));
}
