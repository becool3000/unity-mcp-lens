#nullable disable
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Tools.Parameters;

namespace Becool.UnityMcpLens.Editor.Models.UI
{
    sealed class UiEnsureHierarchyRequest
    {
        public JToken Target { get; set; }
        public string SearchMethod { get; set; } = "by_name";
        public bool IncludeInactive { get; set; } = true;
        public UiNamedHierarchyNodeSpec[] Nodes { get; set; } = new UiNamedHierarchyNodeSpec[0];
    }

    sealed class UiLayoutPropertiesRequest
    {
        public string Target { get; set; }
        public string SearchMethod { get; set; } = "by_name";
        public string TargetPath { get; set; } = ".";
        public bool IncludeInactive { get; set; } = true;
        public UiNodeLayoutSpec Layout { get; set; }
    }

    sealed class UiVerifyTargetRequest
    {
        public string key { get; set; }
        public string target { get; set; }
        public string searchMethod { get; set; } = "by_name";
        public string targetPath { get; set; } = ".";
        public bool includeInactive { get; set; } = true;
    }

    sealed class UiVerifyAssertionRequest
    {
        public string type { get; set; }
        public string targetKey { get; set; }
        public string otherTargetKey { get; set; }
        public string relation { get; set; }
        public string axis { get; set; }
        public string edge { get; set; }
        public string[] targetKeys { get; set; } = new string[0];
        public string direction { get; set; }
        public float tolerance { get; set; }
        public float margin { get; set; }
    }

    sealed class UiVerifyScreenLayoutRequest
    {
        public UiVerifyTargetRequest[] Targets { get; set; } = new UiVerifyTargetRequest[0];
        public UiVerifyAssertionRequest[] Assertions { get; set; } = new UiVerifyAssertionRequest[0];
    }

    sealed class UiScreenResolutionRequest
    {
        public string key { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    sealed class UiVerifyScreenLayoutMatrixRequest
    {
        public UiScreenResolutionRequest[] Resolutions { get; set; } = new UiScreenResolutionRequest[0];
        public UiVerifyTargetRequest[] Targets { get; set; } = new UiVerifyTargetRequest[0];
        public UiVerifyAssertionRequest[] Assertions { get; set; } = new UiVerifyAssertionRequest[0];
        public bool RestoreOriginal { get; set; } = true;
        public int WarmupMs { get; set; } = 100;
    }

    sealed class UiCanvasPrefabRequest
    {
        public string PrefabPath { get; set; }
        public string RootName { get; set; }
        public string RenderMode { get; set; } = "screen_space_overlay";
        public int? SortingOrder { get; set; }
        public bool? PixelPerfect { get; set; }
        public JToken ReferenceResolution { get; set; }
        public string ScaleMode { get; set; } = "scale_with_screen_size";
        public UiNamedHierarchyNodeSpec[] Nodes { get; set; } = new UiNamedHierarchyNodeSpec[0];
    }

    sealed class UiRaycastPointRequest
    {
        public string key { get; set; }
        public float screenX { get; set; }
        public float screenY { get; set; }
        public string target { get; set; }
        public string searchMethod { get; set; } = "by_name";
        public bool includeInactive { get; set; }
        public int maxResults { get; set; } = 10;
        public string expectTopPathContains { get; set; }
        public bool? expectBlocked { get; set; }
    }

    sealed class UiVerifyRaycastAndLayoutRequest
    {
        public UiRaycastPointRequest[] Points { get; set; } = new UiRaycastPointRequest[0];
        public UiVerifyTargetRequest[] Targets { get; set; } = new UiVerifyTargetRequest[0];
        public UiVerifyAssertionRequest[] Assertions { get; set; } = new UiVerifyAssertionRequest[0];
    }

    sealed class UiOperationResult
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public string errorKind { get; set; }
        public object errorData { get; set; }

        public static UiOperationResult Ok(string message, object data = null)
        {
            return new UiOperationResult
            {
                success = true,
                message = message,
                data = data
            };
        }

        public static UiOperationResult Error(string message, string errorKind, object errorData = null)
        {
            return new UiOperationResult
            {
                success = false,
                message = message,
                errorKind = errorKind,
                errorData = errorData ?? new { errorKind }
            };
        }
    }
}
