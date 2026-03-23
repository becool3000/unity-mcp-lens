using Unity.AI.Pbr.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    record MaterialSkeleton(int taskID, int counter) : MaterialResult(FromPreview(new TextureSkeleton(taskID, counter)));
}
