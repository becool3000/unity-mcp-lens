using System;

namespace Unity.AI.Assistant.Utils
{
    /// <summary>
    /// Runtime-safe version of AccessTokenRefreshUtility with delegation to Editor implementation
    /// </summary>
    static class AccessTokenRefreshUtility
    {
        /// <summary>
        /// Delegate for indicating that a token refresh may be required
        /// </summary>
        public static Action IndicateRefreshMayBeRequiredDelegate { get; set; }

        /// <summary>
        /// Indicates that a token refresh may be required. Delegates to Editor implementation if available.
        /// </summary>
        public static void IndicateRefreshMayBeRequired()
        {
            IndicateRefreshMayBeRequiredDelegate?.Invoke();
        }
    }
}
