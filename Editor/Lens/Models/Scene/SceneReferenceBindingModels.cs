#nullable disable
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Models.Scene
{
    sealed class SceneReferenceBindingEntry
    {
        public string targetPath { get; set; } = ".";
        public string componentType { get; set; }
        public int componentIndex { get; set; }
        public string propertyPath { get; set; }
        public JToken reference { get; set; }
        public JToken[] references { get; set; } = new JToken[0];
    }

    sealed class SceneReferenceBindingRequest
    {
        public JToken Target { get; set; }
        public string SearchMethod { get; set; } = "by_name";
        public bool IncludeInactive { get; set; } = true;
        public SceneReferenceBindingEntry[] Bindings { get; set; } = new SceneReferenceBindingEntry[0];
    }

    sealed class SceneReferenceBindingOperationResult
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public string errorKind { get; set; }
        public object errorData { get; set; }

        public static SceneReferenceBindingOperationResult Ok(string message, object data = null)
        {
            return new SceneReferenceBindingOperationResult
            {
                success = true,
                message = message,
                data = data
            };
        }

        public static SceneReferenceBindingOperationResult Error(string message, string errorKind, object errorData = null)
        {
            return new SceneReferenceBindingOperationResult
            {
                success = false,
                message = message,
                errorKind = errorKind,
                errorData = errorData ?? new { errorKind }
            };
        }
    }
}
