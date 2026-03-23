using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Accounts.Services.Data;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class CloudConnectedState
    {
        internal readonly Signal<ProjectStatus> settings;

        public event Action OnChange;
        public ProjectStatus Value { get => settings.Value; internal set => settings.Value = value; }
        public void Refresh() => settings.Refresh();

        internal bool SimulateBroken
        {
            get => UnityConnectUtils.GetSimulatedBrokenState();
            set => UnityConnectUtils.SimulateBrokenState(value);
        }

        public bool IsConnected => Value is ProjectStatus.Connected or ProjectStatus.OfflineConnected; // Is Api accessible with user rights.

        public CloudConnectedState()
        {
            settings = new(AccountPersistence.CloudConnectedProxy, RefreshInternal, () => OnChange?.Invoke());
            Refresh();
            AIDropdownBridge.ConnectProjectStateChanged(Refresh);
        }

        void RefreshInternal()
        {
            // Ensure the cache is updated before reading its values
            UnityConnectProvider.UpdateCache(); // This populates cachedInfo

            // Option 1: Base status on the 'official' project validity flag
            // (This will be false if raw connection is invalid)
            var isLiveProjectInfoValid = AIDropdownBridge.isProjectValid;

            // Option 2: Check if we have *any* meaningful project data available from the cache,
            // even if the live connection isn't fully validated.
            // This leverages the cached data that was loaded/merged.
            var hasCachedProjectData = !string.IsNullOrEmpty(UnityConnectProvider.organizationKey) &&
                !string.IsNullOrEmpty(UnityConnectProvider.projectId);
            var hasCachedAccessToken = !string.IsNullOrEmpty(UnityConnectProvider.accessToken);

            if (isLiveProjectInfoValid)
            {
                // Live connection is fully valid and reporting status correctly
                Value = UnityConnectProvider.projectBound ? ProjectStatus.Connected : ProjectStatus.NotConnected;
            }
            else if (hasCachedProjectData && hasCachedAccessToken)
            {
                // Live connection is *not* valid, but we have cached project data.
                // This is where you leverage the cache!
                // Introduce a new ProjectStatus enum value
                Value = ProjectStatus.OfflineConnected;
            }
            else
            {
                // No live connection, and no cached project data available
                Value = ProjectStatus.NotReady;
            }

            if (UnityConnectProvider.unityConnectLogLevel > 0)
                Debug.Log($"[CloudConnectedState] Refreshed. isLiveProjectInfoValid: {isLiveProjectInfoValid}, hasCachedProjectData: {hasCachedProjectData}, Current Value: {Value}");
        }
    }
}
