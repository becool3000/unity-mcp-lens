using System;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;

namespace Unity.AI.Image.Services.Utilities
{
    record TextureSkeleton(int taskID, int counter) : TextureResult(new Uri($"{SkeletonExtensions.skeletonUriPath}/{taskID}/{counter}", UriKind.Absolute));
}
