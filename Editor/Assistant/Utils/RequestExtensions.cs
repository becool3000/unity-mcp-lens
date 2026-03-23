using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager.Requests;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class RequestExtensions
    {
        public static Task<ListRequest> GetTask(this ListRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<ListRequest>();

            EditorApplication.update += Poll;
            return tcs.Task;

            void Poll()
            {
                if (request.IsCompleted)
                {
                    EditorApplication.update -= Poll;
                    tcs.SetResult(request);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    EditorApplication.update -= Poll;
                    tcs.SetCanceled();
                }
            }
        }
    }
}
