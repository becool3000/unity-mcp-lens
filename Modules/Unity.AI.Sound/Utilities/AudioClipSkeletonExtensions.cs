using System;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    record AudioClipSkeleton(int taskID, int counter) : AudioClipResult(new Uri($"{SkeletonExtensions.skeletonUriPath}/{taskID}/{counter}", UriKind.Absolute));
}
