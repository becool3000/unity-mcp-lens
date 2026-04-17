using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record VisualBoundsSnapshotParams
    {
        [McpDescription("Target runtime GameObject, hierarchy path, or instance id.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include inactive objects when resolving targets.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("Optional camera GameObject used to compute screen-space footprint. Defaults to Camera.main or the first enabled camera.", Required = false)]
        public string CameraTarget { get; set; }

        [McpDescription("How to find the optional camera target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string CameraSearchMethod { get; set; } = "by_name";

        [McpDescription("Optional reference GameObject used to compute ratio versus another runtime object.", Required = false)]
        public string ReferenceTarget { get; set; }

        [McpDescription("How to find the optional reference target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string ReferenceSearchMethod { get; set; } = "by_name";

        [McpDescription("Include ownership and presentation-chain details such as child renderer scale, baseline fields, tint, sprite, and flip state.", Required = false)]
        public bool IncludeOwnership { get; set; } = false;

        [McpDescription("Sample the target over a short interval to detect pulsing scale, rotation changes, and tint changes.", Required = false)]
        public bool SampleOverTime { get; set; } = false;

        [McpDescription("Duration for time sampling in milliseconds when SampleOverTime is true.", Required = false)]
        public int SampleDurationMs { get; set; } = 400;

        [McpDescription("Delay between time-sample captures in milliseconds when SampleOverTime is true.", Required = false)]
        public int SampleIntervalMs { get; set; } = 50;
    }
}
