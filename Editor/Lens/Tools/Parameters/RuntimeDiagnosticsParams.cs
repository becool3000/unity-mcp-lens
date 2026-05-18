using Newtonsoft.Json.Linq;
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

    public record PointerInputSmokeParams
    {
        [McpDescription("Screen-space X coordinate in pixels.", Required = true)]
        public float ScreenX { get; set; }

        [McpDescription("Screen-space Y coordinate in pixels.", Required = true)]
        public float ScreenY { get; set; }

        [McpDescription("Mouse button name to press while queueing input: left, right, middle, or none.", Required = false)]
        public string Button { get; set; } = "left";

        [McpDescription("Synthetic mouse wheel X scroll value to queue through MouseState.scroll.", Required = false)]
        public float ScrollX { get; set; } = 0f;

        [McpDescription("Synthetic mouse wheel Y scroll value to queue through MouseState.scroll.", Required = false)]
        public float ScrollY { get; set; } = 0f;

        [McpDescription("Queue a synthetic Input System mouse state before sampling. Uses reflection and reports unsupported state when Input System APIs are unavailable.", Required = false)]
        public bool QueueInput { get; set; } = true;

        [McpDescription("Advance this many editor frames after queueing input when play mode is paused.", Required = false)]
        public int StepFrames { get; set; } = 1;

        [McpDescription("Advance or wait this many runtime frames after queueing input before sampling state.", Required = false)]
        public int AdvanceFrames { get; set; } = 0;

        [McpDescription("Delay after queueing input before reading observed state.", Required = false)]
        public int SettleMs { get; set; } = 100;

        [McpDescription("Pixel/control tolerance for proving observed Mouse.current state matches the queued position, button, and scroll.", Required = false)]
        public float ObservationTolerance { get; set; } = 2f;

        [McpDescription("Optional UI root scope for raycast evidence.", Required = false)]
        public string UiTarget { get; set; }

        [McpDescription("How to find the optional UI root ('by_name', 'by_id', 'by_path').", Required = false)]
        public string UiSearchMethod { get; set; } = "by_name";

        [McpDescription("Include inactive UI elements while evaluating raycast evidence.", Required = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Optional camera target used for world raycast evidence. Defaults to Camera.main or first enabled camera.", Required = false)]
        public string CameraTarget { get; set; }

        [McpDescription("How to find the optional camera target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string CameraSearchMethod { get; set; } = "by_name";

        [McpDescription("Layer mask for optional physics raycast evidence. Defaults to all layers.", Required = false)]
        public int LayerMask { get; set; } = -1;

        [McpDescription("Optional runtime state targets to sample before and after input.", Required = false)]
        public PointerSmokeStateTarget[] StateTargets { get; set; } = new PointerSmokeStateTarget[0];

        [McpDescription("Optional assertions over sampled runtime state.", Required = false)]
        public PointerSmokeStateAssertion[] StateAssertions { get; set; } = new PointerSmokeStateAssertion[0];
    }

    public record PointerSmokeStateTarget
    {
        [McpDescription("Stable key used by state assertions.", Required = true)]
        public string Key { get; set; }

        [McpDescription("Runtime GameObject, hierarchy path, or instance id.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Relative child path under the resolved target. Use '.' or omit for the root.", Required = false)]
        public string TargetPath { get; set; } = ".";

        [McpDescription("Include inactive objects when resolving the target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("Optional component type to read from the target object.", Required = false)]
        public string ComponentType { get; set; }

        [McpDescription("0-based component index when multiple matching components exist.", Required = false)]
        public int ComponentIndex { get; set; } = 0;

        [McpDescription("Field/property path to read via reflection.", Required = false)]
        public string MemberPath { get; set; }

        [McpDescription("Serialized property path to read from the component.", Required = false)]
        public string PropertyPath { get; set; }
    }

    public record PointerSmokeStateAssertion
    {
        [McpDescription("Assertion type: changed, equals, not_equals, contains, greater_than, or less_than.", Required = true)]
        public string Type { get; set; } = "changed";

        [McpDescription("State target key this assertion evaluates.", Required = true)]
        public string TargetKey { get; set; }

        [McpDescription("Expected value for equals, not_equals, greater_than, or less_than.", Required = false)]
        public JToken Value { get; set; }

        [McpDescription("Expected substring for contains.", Required = false)]
        public string Contains { get; set; }

        [McpDescription("Numeric comparison tolerance.", Required = false)]
        public float Tolerance { get; set; } = 0.001f;
    }
}
