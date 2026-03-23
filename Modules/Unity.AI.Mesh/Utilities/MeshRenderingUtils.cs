using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class MeshRenderingUtils
    {
        /// <summary>
        /// Asynchronously renders a single frame of a GameObject to a provided RenderTexture.
        /// </summary>
        public static Task RenderMeshAsync(this GameObject meshObject, float rotationY, int width, int height, RenderTexture buffer)
        {
            var tcs = new TaskCompletionSource<bool>();
            var job = new SingleFrameRenderJob(meshObject, tcs, rotationY, buffer);
            job.Start();
            return tcs.Task;
        }
    }
}
