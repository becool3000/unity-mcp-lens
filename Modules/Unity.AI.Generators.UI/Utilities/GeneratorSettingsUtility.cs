using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit;

namespace Unity.AI.Generators.UI.Utilities
{
    static class GeneratorSettingsUtility
    {
        public static async Task DebounceSettingsSave(CancellationTokenSource saveTokenSource, Action saveAction, Action<Exception> handleError, int delayMilliseconds = 250)
        {
            // Cancel any previous pending save
            if (saveTokenSource != null)
            {
                saveTokenSource.Cancel();
                saveTokenSource.Dispose();
            }

            // Create new token source
            var newTokenSource = new CancellationTokenSource();
            var token = newTokenSource.Token;

            try
            {
                await EditorTask.Delay(delayMilliseconds, token);
                saveAction();
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellations
            }
            catch (Exception ex)
            {
                handleError(ex);
            }
        }
    }
}
