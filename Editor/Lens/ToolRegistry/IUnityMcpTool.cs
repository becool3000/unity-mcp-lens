using System;
using System.Threading.Tasks;

namespace Becool.UnityMcpLens.Editor.ToolRegistry
{
    /// <summary>
    /// Interface for implementing class-based MCP tools with flexible parameter handling.
    /// Use this when your tool needs state management, complex initialization, or custom schema generation.
    /// For simpler tools with strongly-typed parameters, consider using <see cref="IUnityMcpTool{TParams}"/> instead.
    /// </summary>
    /// <remarks>
    /// Class-based tools are instantiated once at discovery time and reused for all executions.
    /// The class must have a parameterless constructor and be decorated with the <see cref="McpToolAttribute"/>.
    ///
    /// Schema generation:
    /// - If <see cref="GetInputSchema"/> returns null, a default flexible schema is provided
    /// - If <see cref="GetOutputSchema"/> returns null, the system attempts to auto-generate from the return type
    ///
    /// Thread safety: Execute may be called from Unity's main thread. Avoid blocking operations.
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpTool("custom_tool", "Performs custom operations")]
    /// public class CustomTool : IUnityMcpTool
    /// {
    ///     public Task&lt;object&gt; ExecuteAsync(object parameters)
    ///     {
    ///         var jobject = parameters as JObject;
    ///         var action = jobject?["action"]?.Value&lt;string&gt;();
    ///
    ///         return Task.FromResult&lt;object&gt;(new { success = true, result = $"Executed {action}" });
    ///     }
    ///
    ///     public object GetInputSchema()
    ///     {
    ///         return new
    ///         {
    ///             type = "object",
    ///             properties = new
    ///             {
    ///                 action = new { type = "string", description = "Action to perform" }
    ///             },
    ///             required = new[] { "action" }
    ///         };
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IUnityMcpTool
    {
        /// <summary>
        /// Executes the tool asynchronously with the provided parameters from the MCP client.
        /// </summary>
        /// <remarks>
        /// Called on Unity's main thread when an MCP client invokes this tool.
        /// Parameters are provided as a Newtonsoft.Json.Linq.JObject for flexible access.
        /// Return values are automatically serialized to JSON for the MCP response.
        /// For synchronous tools, return Task.FromResult(result).
        /// </remarks>
        /// <param name="parameters">Parameters as JObject from MCP client, or null if no parameters provided</param>
        /// <returns>A task containing the tool execution result, which will be serialized to JSON. Can be an anonymous object, class instance, or primitive type</returns>
        /// <exception cref="System.Exception">Thrown exceptions are caught and returned as error responses to the MCP client</exception>
        Task<object> ExecuteAsync(object parameters);

        /// <summary>
        /// Provides a custom JSON schema for this tool's input parameters.
        /// </summary>
        /// <remarks>
        /// Optional: Return null to use a default flexible schema that accepts any object properties.
        /// The schema should follow JSON Schema specification and describe the expected parameter structure.
        /// </remarks>
        /// <returns>JSON schema object (typically an anonymous object with type/properties/required fields), or null to use default schema</returns>
        object GetInputSchema() => null;

        /// <summary>
        /// Provides a custom JSON schema for this tool's output structure.
        /// </summary>
        /// <remarks>
        /// Optional: Return null for no output schema. The system will attempt to auto-generate a schema
        /// by inspecting the Execute method's return type, but this may not work for all types.
        /// Providing an explicit schema improves documentation and client-side typing.
        /// </remarks>
        /// <returns>JSON schema object describing the return value structure, or null for no output schema</returns>
        object GetOutputSchema() => null;
    }

    /// <summary>
    /// Generic interface for class-based MCP tools with strongly-typed parameters.
    /// Recommended for most tools as it provides automatic schema generation and type safety.
    /// </summary>
    /// <remarks>
    /// This interface automatically generates input schemas from the <typeparamref name="TParams"/> type,
    /// eliminating the need for manual schema definition and parameter casting.
    ///
    /// The parameter type should be a class or record with public properties decorated with
    /// <see cref="McpDescriptionAttribute"/> for better schema documentation.
    ///
    /// Schema generation supports:
    /// - Primitive types (int, string, bool, etc.)
    /// - Enums (converted to string constraints)
    /// - Collections (arrays and lists)
    /// - Nested objects
    /// - Nullable types
    /// - Default values from property initializers
    ///
    /// The class must have a parameterless constructor and be decorated with <see cref="McpToolAttribute"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class GreetParams
    /// {
    ///     [McpDescription("Name of person to greet", Required = true)]
    ///     public string Name { get; set; }
    ///
    ///     [McpDescription("Greeting style")]
    ///     public string Style { get; set; } = "formal";
    /// }
    ///
    /// [McpTool("greet_person", "Greets a person with a message")]
    /// public class GreetTool : IUnityMcpTool&lt;GreetParams&gt;
    /// {
    ///     public Task&lt;object&gt; ExecuteAsync(GreetParams parameters)
    ///     {
    ///         var greeting = parameters.Style == "formal"
    ///             ? $"Good day, {parameters.Name}"
    ///             : $"Hey {parameters.Name}!";
    ///
    ///         return Task.FromResult&lt;object&gt;(new { message = greeting });
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="TParams">The strongly-typed parameter class or record. Must be a reference type with a parameterless constructor</typeparam>
    public interface IUnityMcpTool<TParams> where TParams : class
    {
        /// <summary>
        /// Executes the tool asynchronously with strongly-typed parameters deserialized from the MCP client request.
        /// </summary>
        /// <remarks>
        /// Called on Unity's main thread. The parameters are automatically deserialized from JSON
        /// using Newtonsoft.Json with enum string conversion enabled.
        /// Return values are automatically serialized to JSON for the MCP response.
        /// For synchronous tools, return Task.FromResult(result).
        /// </remarks>
        /// <param name="parameters">Strongly-typed parameters deserialized from the MCP client request</param>
        /// <returns>A task containing the tool execution result, which will be serialized to JSON. Can be an anonymous object, class instance, or primitive type</returns>
        /// <exception cref="System.Exception">Thrown exceptions are caught and returned as error responses to the MCP client</exception>
        Task<object> ExecuteAsync(TParams parameters);
    }
}
