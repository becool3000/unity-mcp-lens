using System.IO;

namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// A skill resource that loads content from a file on the filesystem.
    /// The content is loaded on-demand when GetContent() is called.
    /// </summary>
    class FileSkillResource : ISkillResource
    {
        readonly string m_FilePath;

        /// <summary>
        /// Creates a new file-based skill resource.
        /// </summary>
        /// <param name="filePath">Absolute path to the file</param>
        public FileSkillResource(string filePath)
        {
            m_FilePath = filePath;
        }

        /// <summary>
        /// Loads and returns the content of the file.
        /// </summary>
        /// <returns>The file content as a string</returns>
        /// <exception cref="IOException">Thrown if the file cannot be read</exception>
        public string GetContent()
        {
            return File.ReadAllText(m_FilePath);
        }
    }
}
