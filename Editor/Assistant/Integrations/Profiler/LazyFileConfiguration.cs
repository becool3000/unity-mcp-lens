using System;
using System.IO;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    /// <summary>
    /// Utility class for reading prompt files with automatic file change detection and caching.
    /// </summary>
    abstract class LazyFileConfiguration<T> where T : class
    {
        private readonly string m_FilePath;
        private DateTime m_LastModified;
        protected T m_Data;

        /// <summary>
        /// Creates a new LazyFileConfiguration for the specified file path.
        /// </summary>
        /// <param name="path">The path to the file to read.</param>
        public LazyFileConfiguration(T initialValue, string configurationFilePath)
        {
            m_Data = initialValue;
            m_FilePath = configurationFilePath;
            m_LastModified = DateTime.MinValue;
        }

        public T Data
        {
            get
            {
                if (!string.IsNullOrEmpty(m_FilePath))
                {
                    var lastModified = File.GetLastWriteTime(m_FilePath);
                    if (m_Data == null || m_LastModified != lastModified)
                    {
                        using (var stream = new FileStream(m_FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            m_Data = Parse(stream);
                        }
                        m_LastModified = lastModified;
                    }
                }

                return m_Data;
            }
        }

        protected abstract T Parse(FileStream stream);
    }
}
