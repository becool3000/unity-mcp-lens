using System;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Marks a static method or class as an MCP tool that can be invoked by MCP clients.
    /// Tools are automatically discovered at editor startup using Unity's TypeCache system.
    /// </summary>
    /// <remarks>
    /// This attribute can be applied to:
    /// 1. Static methods with 0-1 parameters (method-based tools)
    /// 2. Classes implementing <see cref="IUnityMcpTool"/> or <see cref="IUnityMcpTool{TParams}"/> (class-based tools)
    ///
    /// Method-based tools:
    /// - Must be public static
    /// - Can have 0 parameters, 1 typed parameter, or 1 JObject parameter
    /// - Schemas auto-generated for typed parameters
    /// - Use [McpSchema] attribute for custom schemas with JObject parameters
    ///
    /// Class-based tools:
    /// - Must have a public parameterless constructor
    /// - Instantiated once at discovery time
    /// - Useful when tools need state or complex initialization
    ///
    /// Tool names must be unique across the project. Duplicate names will generate warnings.
    /// </remarks>
    /// <example>
    /// <para>Method-based tool with typed parameters:</para>
    /// <code>
    /// public class MyParams
    /// {
    ///     public string Name { get; set; }
    /// }
    ///
    /// [McpTool("my_tool", "Does something useful")]
    /// public static object MyTool(MyParams params)
    /// {
    ///     return new { result = $"Hello {params.Name}" };
    /// }
    /// </code>
    /// <para>Class-based tool:</para>
    /// <code>
    /// [McpTool("stateful_tool", "Tool with state")]
    /// public class StatefulTool : IUnityMcpTool&lt;MyParams&gt;
    /// {
    ///     private int callCount = 0;
    ///
    ///     public object Execute(MyParams parameters)
    ///     {
    ///         callCount++;
    ///         return new { calls = callCount };
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class McpToolAttribute : Attribute
    {
        /// <summary>
        /// Gets the unique name of the tool as exposed to MCP clients.
        /// Tool names should use snake_case convention (e.g., "my_tool", "read_file").
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description of what the tool does.
        /// This description is sent to MCP clients and may be used by AI models to understand tool purpose.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the display title of the tool (optional).
        /// If not specified, the Description is used as the title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets or sets optional MCP annotations describing tool behavior.
        /// Annotations can provide hints to clients about tool characteristics (e.g., side effects, cost).
        /// </summary>
        public object Annotations { get; set; }

        /// <summary>
        /// Gets or sets whether this tool is enabled by default when no user override exists.
        /// Tools with this set to true are part of the curated default set shown as enabled
        /// in the settings UI. New or unvalidated tools should leave this as false.
        /// </summary>
        public bool EnabledByDefault { get; set; }

        /// <summary>
        /// Gets or sets category tags for organizing and filtering tools.
        /// Use string identifiers from <see cref="ToolCategory"/> enum (e.g., "scripting", "scene", "assets").
        /// </summary>
        public string[] Groups { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Creates an MCP tool attribute to mark a method or class as a discoverable tool.
        /// </summary>
        /// <param name="name">The unique name of the tool as exposed to MCP clients (use snake_case)</param>
        /// <param name="description">Human-readable description of what the tool does. If null, defaults to the tool name</param>
        /// <param name="title">Optional display title. If null, defaults to the description</param>
        /// <param name="annotations">Optional MCP annotations object describing tool characteristics</param>
        /// <exception cref="ArgumentException">Thrown if name is null or empty</exception>
        public McpToolAttribute(string name, string description = null, string title = null, object annotations = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(name));

            // Sanitize name for cross-provider compatibility (OpenAI requires ^[a-zA-Z0-9_-]+$)
            Name = McpToolRegistry.SanitizeToolName(name);
            Description = description ?? name;
            Title = title;
            Annotations = annotations;
        }
    }

    /// <summary>
    /// Marks a static method as providing a custom input schema for an MCP tool.
    /// Use this when a tool takes JObject parameters and needs a schema more specific than the default.
    /// </summary>
    /// <remarks>
    /// The schema method must:
    /// - Be public static
    /// - Return an object representing the JSON schema
    /// - Have no parameters
    ///
    /// The ToolName must match the name used in the [McpTool] attribute.
    /// This attribute is only needed for tools using JObject parameters; typed parameter tools
    /// automatically generate schemas from the parameter type.
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpTool("custom_action", "Performs a custom action")]
    /// public static object CustomAction(JObject params)
    /// {
    ///     // Tool implementation
    /// }
    ///
    /// [McpSchema("custom_action")]
    /// public static object CustomActionSchema()
    /// {
    ///     return new
    ///     {
    ///         type = "object",
    ///         properties = new
    ///         {
    ///             action = new { type = "string", enum = new[] { "create", "delete" } },
    ///             target = new { type = "string", description = "Target to act upon" }
    ///         },
    ///         required = new[] { "action" }
    ///     };
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class McpSchemaAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the tool this schema method provides the input schema for.
        /// Must exactly match the name used in the corresponding [McpTool] attribute.
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// Creates a schema attribute linking this method to an MCP tool.
        /// </summary>
        /// <param name="toolName">The name of the tool this schema is for (must match the [McpTool] name exactly)</param>
        /// <exception cref="ArgumentException">Thrown if toolName is null or empty</exception>
        public McpSchemaAttribute(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));

            // Sanitize to match McpToolAttribute behavior
            ToolName = McpToolRegistry.SanitizeToolName(toolName);
        }
    }

    /// <summary>
    /// Marks a static method as providing a custom output schema for an MCP tool.
    /// Output schemas help MCP clients understand the structure of tool return values.
    /// </summary>
    /// <remarks>
    /// The schema method must:
    /// - Be public static
    /// - Return an object representing the JSON schema for the output
    /// - Have no parameters
    ///
    /// The ToolName must match the name used in the [McpTool] attribute.
    /// For class-based tools, implement <see cref="IUnityMcpTool.GetOutputSchema"/> instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpTool("calculate", "Performs a calculation")]
    /// public static object Calculate(JObject params)
    /// {
    ///     return new { result = 42, unit = "answer" };
    /// }
    ///
    /// [McpOutputSchema("calculate")]
    /// public static object CalculateOutputSchema()
    /// {
    ///     return new
    ///     {
    ///         type = "object",
    ///         properties = new
    ///         {
    ///             result = new { type = "number", description = "Calculation result" },
    ///             unit = new { type = "string", description = "Unit of measurement" }
    ///         },
    ///         required = new[] { "result" }
    ///     };
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class McpOutputSchemaAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the tool this schema method provides the output schema for.
        /// Must exactly match the name used in the corresponding [McpTool] attribute.
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// Creates an output schema attribute linking this method to an MCP tool.
        /// </summary>
        /// <param name="toolName">The name of the tool this output schema is for (must match the [McpTool] name exactly)</param>
        /// <exception cref="ArgumentException">Thrown if toolName is null or empty</exception>
        public McpOutputSchemaAttribute(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));

            // Sanitize to match McpToolAttribute behavior
            ToolName = McpToolRegistry.SanitizeToolName(toolName);
        }
    }

    /// <summary>
    /// Provides descriptions and constraints for MCP tool parameter properties.
    /// Applied to properties in parameter classes to enhance automatically generated schemas.
    /// </summary>
    /// <remarks>
    /// This attribute enhances schema generation by providing:
    /// - Human-readable descriptions for properties
    /// - Required field markers
    /// - Enum constraints for string properties
    /// - Explicit default values (overriding property initializers)
    ///
    /// The <see cref="SchemaGenerator"/> reads this attribute when generating input schemas
    /// for tools using typed parameters.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class CreateObjectParams
    /// {
    ///     [McpDescription("Name of the object to create", Required = true)]
    ///     public string Name { get; set; }
    ///
    ///     [McpDescription("Object type", EnumType = typeof(ObjectType))]
    ///     public string Type { get; set; } = "cube";
    ///
    ///     [McpDescription("Scale multiplier", Default = 1.0)]
    ///     public float Scale { get; set; }
    ///
    ///     [McpDescription("Position in world space")]
    ///     public Vector3 Position { get; set; }
    /// }
    ///
    /// public enum ObjectType { Cube, Sphere, Cylinder }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class McpDescriptionAttribute : Attribute
    {
        object m_Default;

        /// <summary>
        /// Gets the human-readable description for this parameter property.
        /// This appears in the generated JSON schema and helps MCP clients understand the parameter's purpose.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets or sets whether this parameter is required.
        /// Required parameters must be provided by the MCP client; optional parameters may be omitted.
        /// </summary>
        /// <remarks>
        /// Value types without default values are automatically considered required.
        /// Set this to true to explicitly mark reference types as required.
        /// </remarks>
        public bool Required { get; set; }

        /// <summary>
        /// Gets or sets an enum type to use for constraining string property values.
        /// The enum values will be converted to string constraints in the JSON schema.
        /// </summary>
        /// <remarks>
        /// This is useful when you want to constrain a string property to specific values
        /// without changing the property type from string.
        /// If the property itself is an enum type, this is not needed.
        /// </remarks>
        public Type EnumType { get; set; }

        /// <summary>
        /// Gets or sets an explicit default value for this parameter.
        /// Takes precedence over property initializers when generating the JSON schema.
        /// </summary>
        /// <remarks>
        /// When set, this value will be used in the JSON schema's "default" field.
        /// If not set, the schema generator will attempt to detect defaults from property initializers.
        /// For enum properties, the default will be automatically converted to its string representation.
        /// </remarks>
        public object Default
        {
            get => m_Default;
            set
            {
                m_Default = value;
                HasDefault = true;
            }
        }

        /// <summary>
        /// Gets whether an explicit default value was set via the <see cref="Default"/> property.
        /// Used internally by <see cref="SchemaGenerator"/> to distinguish between null defaults and no defaults.
        /// </summary>
        public bool HasDefault { get; private set; }

        /// <summary>
        /// Creates a description attribute for an MCP tool parameter property.
        /// </summary>
        /// <param name="description">Human-readable description text that explains the parameter's purpose and expected values</param>
        public McpDescriptionAttribute(string description)
        {
            Description = description ?? string.Empty;
        }
    }
}