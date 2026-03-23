using System;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class NetworkAvailability
    {
        static Action s_OnChange;
        public static event Action OnChanged
        {
            add
            {
                // If this is the first subscriber, start the polling.
                if (s_OnChange == null)
                {
                    Start();
                }
                s_OnChange += value;
            }
            remove
            {
                s_OnChange -= value;
                // If this was the last subscriber, stop the polling.
                if (s_OnChange == null)
                {
                    Stop();
                }
            }
        }
        public static bool IsAvailable => Application.internetReachability != NetworkReachability.NotReachable;
        public const int delay = 2000; // Delay in milliseconds

        static bool s_PreviousAvailability;
        static DateTime s_NextCheckTime;
        static TimeSpan s_PollingInterval;

        static void Start()
        {
            s_PreviousAvailability = IsAvailable;
            s_PollingInterval = TimeSpan.FromMilliseconds(delay);

            // Set the first check time. Using UtcNow to be independent of local time zones.
            s_NextCheckTime = DateTime.UtcNow + s_PollingInterval;

            // Subscribe to the editor's update loop and quitting event.
            EditorApplication.update += OnUpdate;
            EditorApplication.quitting += Stop;
        }

        static void Stop()
        {
            // Unsubscribe from events to prevent leaks and stop the polling loop.
            EditorApplication.update -= OnUpdate;
            EditorApplication.quitting -= Stop;
        }

        // This method is called on every editor frame update.
        static void OnUpdate()
        {
            // Only proceed if the current time has passed the scheduled check time.
            if (DateTime.UtcNow < s_NextCheckTime)
            {
                return;
            }

            // Schedule the next check based on the current time.
            s_NextCheckTime = DateTime.UtcNow + s_PollingInterval;

            // Perform the availability check.
            CheckNetworkAvailability();
        }

        static void CheckNetworkAvailability()
        {
            var currentAvailability = IsAvailable;
            if (s_PreviousAvailability != currentAvailability)
            {
                s_PreviousAvailability = currentAvailability;
                try
                {
                    // Invoke the event to notify subscribers of the change.
                    s_OnChange?.Invoke();
                }
                catch (Exception e)
                {
                    // Catch exceptions from subscribers to prevent them from breaking the editor's update loop.
                    Debug.LogException(e);
                }
            }
        }
    }
}
