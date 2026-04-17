using System;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using UnityEngine.Profiling;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public record ProfilerQueryParams
    {
        [McpDescription("Profiler action: initialize, summary, top_samples, sample, gc_summary, gc_allocations.", Required = false, Default = "summary")]
        public string Action { get; set; } = "summary";

        [McpDescription("Maximum samples to return when supported.", Required = false, Default = 25)]
        public int Limit { get; set; } = 25;

        [McpDescription("Optional sample name or marker path for future detailed profiler queries.", Required = false)]
        public string Sample { get; set; }
    }

    public static class ProfilerQueryTools
    {
        const string Description = "Queries compact local Unity profiler and memory information. Detailed profiler UI/session controls are intentionally not ported.";

        [McpTool("Unity.Profiler.Query", Description, "Query Unity Profiler", Groups = new[] { "debug", "profiler" }, EnabledByDefault = true)]
        public static object Query(ProfilerQueryParams parameters)
        {
            parameters ??= new ProfilerQueryParams();
            string action = (parameters.Action ?? "summary").Trim().ToLowerInvariant();

            return action switch
            {
                "initialize" => Response.Success("Profiler query surface initialized.", BuildProfilerSummary(includeHint: true)),
                "summary" => Response.Success("Profiler summary retrieved.", BuildProfilerSummary(includeHint: false)),
                "gc_summary" => Response.Success("GC summary retrieved.", BuildGcSummary()),
                "gc_allocations" => Response.Success("GC allocation details are not available through the compact Lens profiler surface.", new
                {
                    available = false,
                    action,
                    hint = "Use Unity.Profiler.Query gc_summary for compact memory totals."
                }),
                "top_samples" or "sample" => Response.Success("Profiler sample queries are not available through the compact Lens profiler surface.", new
                {
                    available = false,
                    action,
                    parameters.Sample,
                    limit = Math.Clamp(parameters.Limit, 1, 200),
                    hint = "Lens intentionally exposes compact local metrics and does not port legacy profiler UI session tooling."
                }),
                _ => Response.Error("INVALID_PROFILER_ACTION: action must be initialize, summary, top_samples, sample, gc_summary, or gc_allocations.")
            };
        }

        static object BuildProfilerSummary(bool includeHint)
        {
            var data = new
            {
                Profiler.supported,
                Profiler.enabled,
                memory = BuildGcSummary(),
                hint = includeHint ? "Set action to summary or gc_summary for compact metrics." : null
            };
            return data;
        }

        static object BuildGcSummary() => new
        {
            totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
            totalReservedMemory = Profiler.GetTotalReservedMemoryLong(),
            totalUnusedReservedMemory = Profiler.GetTotalUnusedReservedMemoryLong(),
            monoUsedSize = Profiler.GetMonoUsedSizeLong(),
            monoHeapSize = Profiler.GetMonoHeapSizeLong()
        };
    }
}
