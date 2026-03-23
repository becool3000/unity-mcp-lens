using System;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    record MeshSkeleton(int taskID, int counter) : MeshResult(new Uri($"{SkeletonExtensions.skeletonUriPath}/{taskID}/{counter}", UriKind.Absolute));
}
