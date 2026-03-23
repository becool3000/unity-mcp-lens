using System;
using System.ComponentModel;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    ///     Marks a parameter of a method decorated with a <see cref="ContextProviderAttribute"/>
    ///     attribute with a description of its purpose.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter), EditorBrowsable(EditorBrowsableState.Never)]
    class ParameterAttribute : Attribute
    {
        /// <summary>
        ///     Description of the argument marked by this attribute.
        /// </summary>
        public readonly string Description;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ParameterAttribute"/> type.
        /// </summary>
        /// <param name="description">
        ///     Description of the argument marked by this attribute.
        /// </param>
        /// <exception cref="ArgumentException">
        ///     Thrown if description is null or empty. A description must be provided for the LLM to understand how to
        ///     use the tool.
        /// </exception>
        public ParameterAttribute(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Cannot be empty", nameof(description));

            Description = description;
        }
    }
}
