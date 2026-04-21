#nullable disable
namespace Becool.UnityMcpLens.Editor.Lens
{
    static class ToolSchemaFragments
    {
        public static object TargetRef(string description, bool allowNull = false)
        {
            object[] variants = allowNull
                ? new object[] { new { type = "string" }, new { type = "integer" }, new { type = "null" } }
                : new object[] { new { type = "string" }, new { type = "integer" } };

            return new
            {
                description,
                anyOf = variants
            };
        }

        public static object SearchMethod()
        {
            return new
            {
                type = "string",
                description = "How to resolve target/searchTerm.",
                @enum = new[] { "by_name", "by_id", "by_path", "by_tag", "by_layer", "by_component", "by_id_or_name_or_path" }
            };
        }

        public static object Vector3Array(string description)
        {
            return new
            {
                type = "array",
                description,
                items = new { type = "number" },
                min_items = 3,
                max_items = 3
            };
        }

        public static object ValidationMessage()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    severity = new { type = "string" },
                    code = new { type = "string" },
                    message = new { type = "string" }
                }
            };
        }

        public static object ResponseEnvelope(object dataSchema = null)
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean" },
                    message = new { type = "string" },
                    data = dataSchema ?? new { type = "object" },
                    code = new { type = "string" },
                    error = new { type = "string" }
                }
            };
        }
    }
}
