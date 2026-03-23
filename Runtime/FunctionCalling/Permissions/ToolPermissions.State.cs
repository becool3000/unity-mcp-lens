using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// This file handles storing and loading the state of permission that were "always allowed" by the user
    /// for a scoped duration, for instance only a "session"
    /// The state is preserved even after a domain reload
    /// </summary>
    partial class ToolPermissions
    {
        const string k_EditorPrefsKey = "__AI_ASSISTANT_TOOL_PERMISSIONS__";

        [Serializable]
        partial class PermissionsState
        {
            public CodeExecutionState CodeExecution = new();
            public FileSystemState FileSystem = new();
            public UnityObjectState UnityObject = new();
            public ScreenCaptureState ScreenCapture = new();
            public ToolExecutionState ToolExecution = new();
            public PlayModeState PlayMode = new();
            public AssetGenerationState AssetGeneration = new();

            public void Reset()
            {
                CodeExecution.Reset();
                FileSystem.Reset();
                UnityObject.Reset();
                ScreenCapture.Reset();
                ToolExecution.Reset();
                PlayMode.Reset();
                AssetGeneration.Reset();
            }

            public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
            {
                CodeExecution.AppendTemporaryPermissions(allowedStates);
                FileSystem.AppendTemporaryPermissions(allowedStates);
                UnityObject.AppendTemporaryPermissions(allowedStates);
                ScreenCapture.AppendTemporaryPermissions(allowedStates);
                ToolExecution.AppendTemporaryPermissions(allowedStates);
                PlayMode.AppendTemporaryPermissions(allowedStates);
                AssetGeneration.AppendTemporaryPermissions(allowedStates);
            }
        }

        public void ResetTemporaryPermissions()
        {
            State.Reset();
        }

        public void ResetIgnoredObjects()
        {
            State.UnityObject.ResetIgnoredObjects();
        }

        public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> permissions) => State.GetTemporaryPermissions(permissions);

        void SaveState()
        {
            var json = JsonUtility.ToJson(State);

#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(k_EditorPrefsKey, json);
            InternalLog.Log("Successfully saved permissions state to preferences");
#else
            throw new Exception("State save is not supported in Builds");
#endif
        }

        bool TryLoadState()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.HasKey(k_EditorPrefsKey))
                return false;

            var json = UnityEditor.EditorPrefs.GetString(k_EditorPrefsKey);

            try
            {
                State = JsonUtility.FromJson<PermissionsState>(json);
            }
            catch (Exception)
            {
                InternalLog.LogWarning("Could not load permissions state from preferences");
                return false;
            }

            return true;
#else
            throw new Exception("State load is not supported in Builds");
#endif
        }
    }
}
