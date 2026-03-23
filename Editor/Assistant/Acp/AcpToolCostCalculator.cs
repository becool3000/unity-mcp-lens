using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Central point for calculating costs for ACP tool calls.
    /// Implementations register themselves via the CostProvider delegate.
    /// </summary>
    static class AcpToolCostCalculator
    {
        /// <summary>
        /// Delegate for cost calculation. Implementations should return null if they can't handle the tool.
        /// </summary>
        public static Func<string, JObject, CancellationToken, Task<long?>> CostProvider { get; set; }

        /// <summary>
        /// Attempts to calculate the cost for a tool call.
        /// Returns null if no provider is registered or the tool doesn't support cost calculation.
        /// </summary>
        public static Task<long?> TryGetCostAsync(string toolName, JObject args, CancellationToken cancellationToken = default)
        {
            return CostProvider?.Invoke(toolName, args, cancellationToken) ?? Task.FromResult<long?>(null);
        }
    }
}
