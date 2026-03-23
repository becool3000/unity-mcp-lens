using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Utils
{
    static class DelayUtility
    {
        const int k_ReasonableResponsiveDelay = 1000/60;

        public static async Task ReasonableResponsiveDelay() =>
            await Task.Delay(k_ReasonableResponsiveDelay);
    }
}
