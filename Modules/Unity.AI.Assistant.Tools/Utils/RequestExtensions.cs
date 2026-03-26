using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.PackageManager.Requests;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class RequestExtensions
    {
        public static async Task<Request> WaitForCompletion(this Request request, CancellationToken cancellationToken = default)
        {
            while (!request.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await Task.Delay(100);
            }
            return request;
        }
    }
}
