using System.Reflection;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Represents a function that is present in a C# Assembly
    /// </summary>
    interface IAssemblyFunction
    {
        /// <summary>
        /// The Assembly within which this function is present
        /// </summary>
        Assembly Assembly { get; }
    }
}