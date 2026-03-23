using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using UnityEditor;
using Unity.AI.Assistant.Utils;
using AccessTokenRefreshUtility = Unity.AI.Assistant.Utils.AccessTokenRefreshUtility;
using OrchestrationDataUtilities = Unity.AI.Assistant.Socket.Utilities.OrchestrationDataUtilities;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Initializes Editor-specific implementations for backend configuration
    /// </summary>
    [InitializeOnLoad]
    static class AssistantProtocolInitializer
    {
        static AssistantProtocolInitializer()
        {
            RegisterToolsAsCapabilities();
            SetupDelegates();
        }

        static void RegisterToolsAsCapabilities()
        {
            // List of tool IDs to exclude from registration
            var excludedToolIds = new HashSet<string>
            {
                "Unity.GameObject.CreateGameObject",
                "Unity.GameObject.ManagePrefab",
                "Unity.GameObject.AddComponent",
                "Unity.GameObject.RemoveGameObject",
                "Unity.GameObject.RemoveComponent",
                "Unity.GameObject.SetComponentProperty",
                "Unity.GameObject.ModifyGameObject",
                "Unity.GameObject.ManageLayer",
                "Unity.GameObject.ManageTag",
                "Unity.GetUserGuidelines",
                "Unity.InputSystem.Modify",
                "Unity.AssetGeneration.CreateAnimatorControllerFromClip",
                "Unity.AssetGeneration.EditAnimationClipTool",
                "Unity.AssetGeneration.ConvertToMaterial",
                "Unity.AssetGeneration.ConvertToTerrainLayer",
                "Unity.AssetGeneration.ConvertSpriteSheetToAnimationClip",
                "Unity.AudioClip.Edit",
                "Unity.PackageManager.ExecuteAction",
                "Unity.EnterPlayMode",
                "Unity.ExitPlayMode",
                "Unity.UIToolkitManager"
            };

            var registeredTools = new List<FunctionsObject>();
            
            foreach (var function in ToolRegistry.FunctionToolbox.Tools)
            {
                var toolId = function.FunctionDefinition.FunctionId;
                
                // Skip excluded tools
                if (excludedToolIds.Contains(toolId))
                {
                    continue;
                }

                var capability = function.FunctionDefinition.ToFunctionsObject();
                Unity.AI.Assistant.Backend.CapabilityRegistry.RegisterFunction(capability);
                registeredTools.Add(capability);
            }
            
            InternalLog.Log($"Registered function capability for {registeredTools.Count} tools:\n{string.Join("\n", registeredTools.Select(c => $"{c.FunctionName}\t\t(ID: {c.FunctionId})"))}");


            ToolRegistry.FunctionToolbox.OnFunctionRegistered -= HandleFunctionRegistered;
            ToolRegistry.FunctionToolbox.OnFunctionRegistered += HandleFunctionRegistered;

            ToolRegistry.FunctionToolbox.OnFunctionUnregistered -= HandleFunctionUnregistered;
            ToolRegistry.FunctionToolbox.OnFunctionUnregistered += HandleFunctionUnregistered;
        }

        static void HandleFunctionRegistered(ICachedFunction function)
        {
            Unity.AI.Assistant.Backend.CapabilityRegistry.RegisterFunction(function.FunctionDefinition.ToFunctionsObject());
        }

        static void HandleFunctionUnregistered(ICachedFunction function)
        {
            Unity.AI.Assistant.Backend.CapabilityRegistry.UnregisterFunction(function.FunctionDefinition.FunctionId);
        }

        static void SetupDelegates()
        {
            // Set up AccessTokenRefreshUtility delegation
            AccessTokenRefreshUtility.IndicateRefreshMayBeRequiredDelegate =
                Utils.AccessTokenRefreshUtility.IndicateRefreshMayBeRequired;

            // Set up OrchestrationDataUtilities delegation
            OrchestrationDataUtilities.FromEditorContextReportDelegate =
                Utils.OrchestrationDataUtilities.FromEditorContextReport;
        }
    }
}
