/*
 * This file is based on HuggingfaceHub v0.1.2 (Apache License 2.0)
 * Modifications made by Unity (2025)
 */

namespace HuggingfaceHub
{
    /// <summary>
    /// Progress callback when downloading multiple files
    /// </summary>
    interface IGroupedProgress
    {
        /// <summary>
        /// Report the progress.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="progress">A value from 0 to 100.</param>
        void Report(string filename, int progress);
    }
}