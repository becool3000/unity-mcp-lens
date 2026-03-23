using System;

namespace Unity.AI.Assistant.Editor
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    sealed class LinkHandlerAttribute : Attribute
    {
        /// <summary>
        /// The prefix that this link handlers will handles
        /// For example 'custom' would handle any link starting with 'custom://'
        /// </summary>
        public string Prefix { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="prefix">
        /// The prefix that this link handlers will handles
        /// For example 'custom' would handle any link starting with 'custom://'
        /// </param>
        public LinkHandlerAttribute(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("Prefix cannot be null or empty.", nameof(prefix));

            Prefix = prefix;
        }
    }
}
