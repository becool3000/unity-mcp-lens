using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    internal static class TimerUtils
    {
        public static void DelayedAction(ref CancellationTokenSource tokenSource, Action completed, int time = 1000)
        {
            if (tokenSource != null)
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            tokenSource = new CancellationTokenSource();

            var tempSource = tokenSource;

            _ = Task.Run(() => NewChatConfirmationTask(tempSource.Token, completed, time), tokenSource.Token);
        }

        static async Task NewChatConfirmationTask(CancellationToken token, Action completed, int time = 1000)
        {
            try
            {
                await Task.Delay(time, token);

                EditorApplication.delayCall += () => completed?.Invoke();
            }
            catch (TaskCanceledException)
            {
                // silent part of the cancellation flow
            }
        }
    }
}
