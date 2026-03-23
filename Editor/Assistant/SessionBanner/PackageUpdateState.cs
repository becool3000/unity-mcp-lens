using System;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    [Serializable]
    class PackageUpdateState : ScriptableSingleton<PackageUpdateState>
    {
        public bool updateAvailable;
        public string currentVersion;
        public string latestVersion;
        public bool dismissed;

        public event Action OnChange;

        public void SetUpdateAvailable(string current, string latest)
        {
            // Only reset dismissed if the version changed
            if (latestVersion != latest)
                dismissed = false;

            currentVersion = current;
            latestVersion = latest;
            updateAvailable = true;
            OnChange?.Invoke();
        }

        public void Dismiss()
        {
            dismissed = true;
            OnChange?.Invoke();
        }

        public void Clear()
        {
            updateAvailable = false;
            currentVersion = null;
            latestVersion = null;
            dismissed = false;
            OnChange?.Invoke();
        }
    }
}
