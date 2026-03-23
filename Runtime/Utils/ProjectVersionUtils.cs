using UnityEngine;

namespace Unity.AI.Assistant.Utils
{
    static class ProjectVersionUtils
    {
        /// <summary>
        /// Describes how much of the version to retrieve when getting the project version
        /// </summary>
        public enum VersionDetail
        {
            /// <summary>Only the major version number</summary>
            Major = 0,
            /// <summary>Major and revision number</summary>
            Revision = 1,
            /// <summary>Major, revision, and patch number</summary>
            Patch = 2
        }

        /// <summary>
        /// Returns the Unity version this project is running on
        /// </summary>
        /// <param name="detail">How much of the version to include : Format is Major.revision.patch</param>
        /// <returns>The Unity version at the requested detail level</returns>
        internal static string GetProjectVersion(VersionDetail detail)
        {
            var version = Application.unityVersion;
            switch (detail)
            {
                case VersionDetail.Major:
                    return version.Substring(0, version.IndexOf("."));
                case VersionDetail.Revision:
                    return version.Substring(0, version.LastIndexOf("."));
            }
            return version;
        }
    }
}
