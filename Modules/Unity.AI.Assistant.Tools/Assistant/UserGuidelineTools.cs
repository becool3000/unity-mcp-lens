using System;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    class UserGuidelineTools
    {
        const string k_GetUserGuidelinesFunctionId = "Unity.GetUserGuidelines";

        [AgentTool(
            "Before working on or reasoning about the Unity project, " +
            "call this tool to load essential project context. " +
            "It provides information about what the project does, " +
            "how scripts and assets are organized, naming conventions, " +
            "C# coding style, folder structure, and other Unity-specific standards. " +
            "Use this when creating, editing, explaining, or analyzing any Unity-related file, script, " +
            "or system so your response matches the existing project setup.",
            k_GetUserGuidelinesFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Available)]
        internal static string GetUserGuidelines()
        {
            var customInstructions =
                AssetDatabase.LoadAssetAtPath<TextAsset>(AssistantProjectPreferences.CustomInstructionsFilePath);

            if (customInstructions is null)
                return string.Empty;

            // Limit string length:
            var customInstructionsText = customInstructions.text;
            const int limit = AssistantConstants.UserGuidelineCharacterLimit;

            if (customInstructionsText.Length > limit)
            {
                customInstructionsText = customInstructionsText.Substring(0, limit);
                Debug.LogWarning(
                    $"Custom instructions exceeded {limit} characters and were truncated.");
            }

            return customInstructionsText;
        }
    }
}
