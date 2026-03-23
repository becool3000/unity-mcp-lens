using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Utils
{
    [InitializeOnLoad]
    static class AccessTokenRefreshUtility
    {
        static bool s_IsApplicationActiveOnLastCheck;
        static bool s_IsErrorIndicatingRefreshRequired = true;
        static bool s_IsRefreshBeingAttempted;
        static float s_TimeSinceLastRefresh;
        const float k_MinSecondsBetweenRefresh = 60 * 1;

        /// <summary>
        /// Because of difficult to track down problem with refresh timing, sometimes an error needs to occur before
        /// it's clear that a manual refresh call is required to refresh the
        /// <see cref="CloudProjectSettings.accessToken"/>. Calling <see cref="IndicateRefreshMayBeRequired"/> indicates
        /// to the <see cref="AccessTokenRefreshUtility"/> that something happened that might be worth attempting a
        /// refresh. If this function is called, the <see cref="AccessTokenRefreshUtility"/> will attempt to refresh the
        /// token.
        /// </summary>
        public static void IndicateRefreshMayBeRequired()
        {
            s_IsErrorIndicatingRefreshRequired = true;
        }

        static AccessTokenRefreshUtility()
        {
            // Taking the access token provided here we can exchange for JWT at this endpoint
            // https://services.unity.com/api/auth/v1/genesis-token-exchange/unity

            // The provided JWT has an expiration equal to the expiration of the CloudProject.accessToken

            // There are edge cases where the token is not automatically refreshed. Below are registered functions
            // designed to force a refresh at times where there are issues.

            // 1) If the application goes from being inactive to being active, attempt a refresh at that point. The
            // logic here is that when the application is active, refresh happens automatically. It is possible for the
            // token to become stale and not be refreshed if the editor is out of focus for too long.
            // TODO: The code below has been disabled because it is too noisy. We need to coordinate with AI-Toolkit so that we can create a solution that works for all applications.
            // EditorApplication.update -= RefreshIfSwitchingFromInactiveToActive;
            // EditorApplication.update += RefreshIfSwitchingFromInactiveToActive;
            //
            // void RefreshIfSwitchingFromInactiveToActive()
            // {
            //     try
            //     {
            //         if (!s_IsApplicationActiveOnLastCheck &&
            //             UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            //         {
            //             PerformRefresh();
            //         }
            //
            //         s_IsApplicationActiveOnLastCheck = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
            //     }
            //     catch (Exception)
            //     {
            //         // Ignore failures
            //     }
            // }

            // 2) As a failsafe, check that the current token is not expired manually. If it is expired, attempt to
            // refresh the token.
            EditorApplication.update -= RefreshDueToError;
            EditorApplication.update += RefreshDueToError;

            void RefreshDueToError()
            {
                try
                {
                    if (s_IsErrorIndicatingRefreshRequired)
                    {
                        PerformRefresh();
                        s_IsErrorIndicatingRefreshRequired = false;
                    }
                }
                catch (Exception)
                {
                    // Ignore failures
                }
            }

            void PerformRefresh()
            {
                if (!s_IsRefreshBeingAttempted)
                {
                    // Refreshing the accessToken causes UI flicked because it changes internal states used by the
                    // status trackers in AI Toolkit. To avoid causing a lot of flicker, only allow refreshes every so
                    // often.
                    if(Time.realtimeSinceStartup - s_TimeSinceLastRefresh < k_MinSecondsBetweenRefresh)
                        return;

                    s_TimeSinceLastRefresh = Time.realtimeSinceStartup;
                    s_IsRefreshBeingAttempted = true;
                    CloudProjectSettings.RefreshAccessToken(_ => s_IsRefreshBeingAttempted = false);
                }
            }
        }
    }
}
