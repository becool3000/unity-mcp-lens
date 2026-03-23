using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class ProgressUtils
    {
        internal static async Task RunFuzzyProgress(float startValue, float endValue, Action<float> onStep, int workSize, CancellationToken token, int intervalMs = 50)
        {
            var value = startValue;
            var rate = 0.33f / (intervalMs * Mathf.Sqrt(workSize));

            while (!token.IsCancellationRequested)
            {
                value += (endValue - value) * rate;
                onStep(value);
                await EditorTask.Delay(intervalMs, token);
            }
        }
    }
}
