using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class ApiAccessibleState
    {
        static bool s_HasLoggedWarning;

        public static bool IsAccessible => Account.network.IsAvailable && Account.signIn.IsSignedIn && Account.cloudConnected.IsConnected;

        /// <summary>
        /// Asynchronously waits for the API to become accessible. This method is safe to call
        /// even when the Unity Editor is not in focus. It uses an EditorApplication.update
        /// subscription to poll for the required state, which keeps the async context alive.
        /// </summary>
        /// <returns>A Task that completes when the API is accessible or a timeout is reached.</returns>
        public static async Task<bool> WaitForCloudProjectSettings()
        {
            if (Application.isBatchMode)
                return false;
            
            // If the API is already accessible, we can return immediately.
            if (IsAccessible)
                return true;

            var result = await EditorTask.WaitForCondition(() => IsAccessible, TimeSpan.FromSeconds(30));
            if (!result && !s_HasLoggedWarning)
            {
                if (!Application.isBatchMode)
                    Debug.LogWarning("Account API did not become accessible within 30 seconds. This may be due to network issues or editor focus.");
                s_HasLoggedWarning = true;
            }
            else if (result)
            {
                s_HasLoggedWarning = false;
            }

            return result;
        }

        public event Action OnChange
        {
            add
            {
                Account.network.OnChange += value;
                Account.signIn.OnChange += value;
                Account.cloudConnected.OnChange += value;
            }
            remove
            {
                Account.network.OnChange -= value;
                Account.signIn.OnChange -= value;
                Account.cloudConnected.OnChange -= value;
            }
        }
    }
}
