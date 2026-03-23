using System;
using System.ComponentModel;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    ///     Marks a static method as an agent tool function for Muse Chat.
    ///     Each method parameter must have a <see cref="ParameterAttribute"/> attribute.
    ///     The first parameter of a tool can optionally be a ToolExecutionContext type to receive additional context
    ///     This context is only available locally and is never sent to the LLM
    ///     Among other things, it can be used to handle tool permissions and user interactions
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false), EditorBrowsable(EditorBrowsableState.Never)]
    class AgentToolAttribute : Attribute
    {
        /// <summary> Unique id of the tool. If not provided, it'll be automatically determined. </summary>
        public readonly string Id;

        /// <summary> A description of the functionality provided by the tool method. </summary>
        public readonly string Description;

        /// <summary> A list of tags associated with the tool method. Tags can be used to categorize and filter tools. </summary>
        public readonly string[] Tags;

        /// <summary> Specifies the editor mode requirements for this agent tool (flags). </summary>
        public readonly ToolCallEnvironment ToolCallEnvironment;

        /// <summary> Specifies the assistant mode requirements for this agent tool (flags). </summary>
        public readonly AssistantMode AssistantMode;

        /// <summary>
        /// Controls whether and how this tool is exposed via MCP.
        /// <see cref="McpAvailability.None"/>: not registered (default).
        /// <see cref="McpAvailability.Available"/>: registered but disabled by default.
        /// <see cref="McpAvailability.Default"/>: registered and enabled by default.
        /// </summary>
        public McpAvailability Mcp { get; set; }

        /// <summary>Marks a static method as an agent tool function for  AI Assistant.</summary>
        /// <param name="description">A description of the functionality provided by the tool method.</param>
        /// <param name="id">Unique id of the tool. If not provided, it'll be automatically determined.</param>
        /// <param name="toolCallEnvironment">Specifies the editor mode requirements for this agent tool (flags).</param>
        /// <param name="assistantMode">Specifies the assistant mode requirements for this agent tool (flags).</param>
        /// <param name="tags">A list of tags associated with the tool method. Tags can be used to categorize and filter tools.</param>
        /// <exception cref="ArgumentException">
        ///     Thrown if description is null or empty. A description must be provided for the LLM to understand how to use the tool.
        /// </exception>
        public AgentToolAttribute(string description, string id = null, ToolCallEnvironment toolCallEnvironment = ToolCallEnvironment.PlayMode | ToolCallEnvironment.EditMode, AssistantMode assistantMode = AssistantMode.Agent, params string[] tags)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Cannot be empty", nameof(description));

            Id = id;
            Description = description;
            ToolCallEnvironment = toolCallEnvironment;
            AssistantMode = assistantMode;
            Tags = tags.Length == 0 ? new []{ FunctionCallingUtilities.k_AgentToolTag } : tags;
        }
    }

    /// <summary>
    /// Controls whether and how an <see cref="AgentToolAttribute"/>-marked tool is exposed via MCP.
    /// </summary>
    public enum McpAvailability
    {
        /// <summary>Not registered in MCP (default).</summary>
        None = 0,

        /// <summary>Registered in MCP but disabled by default in settings.</summary>
        Available = 1,

        /// <summary>Registered in MCP and enabled by default (curated set).</summary>
        Default = 2
    }

    /// <summary>
    /// Specifies the editor mode requirements for an agent tool.
    /// </summary>
    [Flags]
    enum ToolCallEnvironment
    {
        /// <summary>Tool is available in the Unity Runtime (aka no Editor present)</summary>
        Runtime = 1,

        /// <summary>Tool is available in Unity's Play Mode.</summary>
        PlayMode = 2,

        /// <summary>Tool is available in Unity's Edit Mode.</summary>
        EditMode = 4,
    }
}
