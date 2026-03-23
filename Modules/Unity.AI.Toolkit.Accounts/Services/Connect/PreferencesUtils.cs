using System;
using System.Reflection;

namespace Unity.AI.Toolkit.Connect
{
    static class PreferencesUtils
    {
        /// <summary>
        /// Registers a callback to be called when the hide menu preference changes.
        /// </summary>
        /// <param name="callback">Action that will be called with a bool that will be true if the button should be hidden.</param>
        /// <returns>Action to unregister the callback.</returns>
        public static Action RegisterHideMenuChanged(Action<bool> callback)
        {
            // Get the PreferencesProvider type from UnityEditor namespace
            var preferencesProviderType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PreferencesProvider");
            if (preferencesProviderType == null)
                return null;

            // Get the hideMenuChanged event field
            var eventField = preferencesProviderType.GetField("hideMenuChanged",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (eventField == null)
                return null;

            // Get add and remove methods for the event
            var eventAddMethod = eventField.FieldType.GetMethod("add_" + eventField.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var eventRemoveMethod = eventField.FieldType.GetMethod("remove_" + eventField.Name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (eventAddMethod == null || eventRemoveMethod == null)
                return null;

            // Get the current event value
            var eventValue = eventField.GetValue(null);
            if (eventValue == null)
                return null;

            // Add our callback to the event
            eventAddMethod.Invoke(eventValue, new object[] { callback });

            // Return an Action that will remove our callback from the event
            return () =>
            {
                eventRemoveMethod.Invoke(eventValue, new object[] { callback });
            };
        }
    }
}
