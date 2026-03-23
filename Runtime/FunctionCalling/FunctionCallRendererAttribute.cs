using System;
using System.ComponentModel;
using System.Reflection;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Use this attribute to define custom renderer for tools
    /// You can implement your renderer by inheriting ChatElementFunctionCallBase or FunctionCallElement (lower level)
    /// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false), EditorBrowsable(EditorBrowsableState.Never)]
    class FunctionCallRendererAttribute : Attribute
    {
        /// <summary>
        /// The tool ID
        /// </summary>
        public readonly string FunctionId;

        /// <summary>
        /// If true, this tool call is displayed prominently outside the reasoning section.
        /// If false (default), it is grouped within the reasoning/thinking sequence.
        /// </summary>
        public bool Emphasized { get; set; }

        /// <summary>
        /// Defines a renderer for a specific tool
        /// </summary>
        /// <param name="functionId">The tool ID to associate to this renderer</param>
        public FunctionCallRendererAttribute(string functionId)
        {
            FunctionId = functionId;
        }

        /// <summary>
        /// Defines a renderer for a specific tool
        /// Ex.: [FunctionCallRenderer(typeof(AssetTools), nameof(AssetTools.FindProjectAssets))]
        /// </summary>
        /// <param name="declaringType">The declaring type of the tool method (encompassing class)</param>
        /// <param name="methodName">The name of the C# method implementing the tool</param>
        public FunctionCallRendererAttribute(Type declaringType, string methodName)
        {
            var methodInfo = declaringType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            
            if(methodInfo == null)
                throw new ArgumentException($"{methodName} not found.");
            
            var toolAttr = methodInfo.GetCustomAttribute<AgentToolAttribute>();
            if (toolAttr == null)
                throw new InvalidOperationException($"{methodInfo.Name} is not marked with [AgentTool]");
            
            FunctionId = toolAttr.Id;
            
            if (!ToolRegistry.FunctionToolbox.HasFunction(toolAttr.Id))
                throw new ArgumentException($"{methodName} not registered in ToolRegistry.");
        }
    }
}
