using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    /// <summary>
    /// A manipulator that executes a cleanup action after its target visual element
    /// has been detached from a panel for a specified delay. This version uses a
    /// centralized, high-performance scheduler to handle many instances efficiently.
    /// </summary>
    class DelayedCleanupManipulator : Manipulator
    {
        /// <summary>
        /// Manages the lifecycle of all manipulator instances, including domain reloads
        /// and a centralized, high-performance scheduler for delayed cleanup.
        /// </summary>
        [InitializeOnLoad]
        static class ManipulatorLifecycleManager
        {
            static readonly List<DelayedCleanupManipulator> k_AllActiveInstances = new();
            static readonly List<DelayedCleanupManipulator> k_WaitingForCleanup = new();

            public static bool IsQuitting { get; private set; }

            static ManipulatorLifecycleManager()
            {
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                EditorApplication.quitting += () => IsQuitting = true;
            }

            // --- Domain Reload Handling ---
            static void OnBeforeAssemblyReload()
            {
                var instancesToClean = new List<DelayedCleanupManipulator>(k_AllActiveInstances);
                foreach (var manipulator in instancesToClean)
                {
                    manipulator.CleanupNow();
                }
                k_AllActiveInstances.Clear();
                k_WaitingForCleanup.Clear();
                EditorApplication.update -= OnUpdate; // Ensure no dangling subscription
            }

            public static void RegisterInstance(DelayedCleanupManipulator manipulator)
            {
                if (!k_AllActiveInstances.Contains(manipulator))
                {
                    k_AllActiveInstances.Add(manipulator);
                }
            }

            public static void UnregisterInstance(DelayedCleanupManipulator manipulator)
            {
                k_AllActiveInstances.Remove(manipulator);
                // Also ensure it's removed from the scheduler if it's there
                CancelDelayedCleanup(manipulator);
            }

            // --- High-Performance Delayed Cleanup Scheduling ---
            public static void ScheduleDelayedCleanup(DelayedCleanupManipulator manipulator)
            {
                if (k_WaitingForCleanup.Contains(manipulator)) return;

                manipulator.m_DetachTime = EditorApplication.timeSinceStartup;
                k_WaitingForCleanup.Add(manipulator);

                // IMPORTANT: Subscribe to the global update loop ONLY if we are the first item.
                if (k_WaitingForCleanup.Count == 1)
                {
                    EditorApplication.update += OnUpdate;
                }
            }

            public static void CancelDelayedCleanup(DelayedCleanupManipulator manipulator)
            {
                var removed = k_WaitingForCleanup.Remove(manipulator);

                // IMPORTANT: Unsubscribe from the global update loop ONLY if we were the last item.
                if (removed && k_WaitingForCleanup.Count == 0)
                {
                    EditorApplication.update -= OnUpdate;
                }
            }

            /// <summary>
            /// This single method is called by EditorApplication.update. It efficiently
            /// checks all pending cleanups in one pass.
            /// </summary>
            static void OnUpdate()
            {
                // Iterate backwards because CleanupNow() will modify the collection,
                // which is safe to do when iterating in reverse.
                for (var i = k_WaitingForCleanup.Count - 1; i >= 0; i--)
                {
                    var manipulator = k_WaitingForCleanup[i];
                    if (EditorApplication.timeSinceStartup - manipulator.m_DetachTime >= manipulator.m_DelayInSeconds)
                    {
                        // Time's up, perform the cleanup.
                        // CleanupNow will call CancelDelayedCleanup internally, removing it from the list.
                        manipulator.CleanupNow();
                    }
                }
            }
        }

        readonly Action m_CleanupAction;
        readonly float m_DelayInSeconds;
        bool m_IsCleanedUp;
        double m_DetachTime; // Set by the static manager

        public DelayedCleanupManipulator(Action cleanupAction, float delayInSeconds = 1.0f)
        {
            m_CleanupAction = cleanupAction ?? throw new ArgumentNullException(nameof(cleanupAction));
            m_DelayInSeconds = Mathf.Max(0, delayInSeconds);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            ManipulatorLifecycleManager.RegisterInstance(this);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            ManipulatorLifecycleManager.UnregisterInstance(this);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ManipulatorLifecycleManager.CancelDelayedCleanup(this);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            ManipulatorLifecycleManager.ScheduleDelayedCleanup(this);
        }

        void CleanupNow()
        {
            if (m_IsCleanedUp)
                return;

            // The job is done, either by aborting or by cleaning up.
            // Mark as cleaned up immediately to prevent re-entrancy.
            m_IsCleanedUp = true;

            // Abort condition: The element was re-attached.
            if (target?.panel != null && !ManipulatorLifecycleManager.IsQuitting)
            {
                // Even though we're aborting the user's action, we must
                // still fully clean up the manipulator itself.
                UnregisterAndRemoveSelf();
                return;
            }

            try
            {
                // Perform the user's cleanup action.
                m_CleanupAction.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception during delayed cleanup for '{target?.name}': {ex.Message}");
            }
            finally
            {
                // Always perform the full manipulator cleanup.
                UnregisterAndRemoveSelf();
            }
        }

        /// <summary>
        /// Helper method to centralize the manipulator's own cleanup logic.
        /// </summary>
        void UnregisterAndRemoveSelf()
        {
            // This call is critical. It removes the manipulator from the k_WaitingForCleanup list
            // and potentially unsubscribes the manager from EditorApplication.update if this was the last one.
            ManipulatorLifecycleManager.CancelDelayedCleanup(this);

            // Remove from the main instance tracking list used for domain reloads.
            ManipulatorLifecycleManager.UnregisterInstance(this);

            // Detach from the target VisualElement.
            target?.RemoveManipulator(this);
        }
    }
}
