using System;

namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// A skill resource that holds content already in memory.
    /// Useful for pre-loaded content, testing, or dynamically generated resources.
    /// </summary>
    class MemorySkillResource : ISkillResource
    {
        readonly string m_Content;

        /// <summary>
        /// Creates a new memory-based skill resource.
        /// </summary>
        /// <param name="content">The resource content</param>
        /// <exception cref="ArgumentNullException">Thrown if content is null</exception>
        public MemorySkillResource(string content)
        {
            m_Content = content ?? throw new ArgumentNullException(nameof(content), "Resource content cannot be null");
        }

        /// <summary>
        /// Returns the content stored in memory.
        /// </summary>
        /// <returns>The resource content as a string</returns>
        public string GetContent()
        {
            return m_Content;
        }
    }
}
