using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetCompositionPatternsTool
    {
        internal const string k_FunctionId = "Unity.AssetGeneration.GetCompositionPatterns";

        [Serializable]
        public struct CompositionPatternInfoOutput
        {
            /// <summary>
            /// The project path to the pattern asset. This value should be used for the 'referenceImagePath' parameter in other tools.
            /// </summary>
            public string AssetPath;

            /// <summary>
            /// The user-friendly display name of the pattern.
            /// </summary>
            public string DisplayName;

            /// <summary>
            /// A list of keywords associated with the pattern, useful for searching and categorization.
            /// </summary>
            public List<string> Keywords;
        }

        [Serializable]
        public struct GetCompositionPatternsOutput
        {
            public List<CompositionPatternInfoOutput> Patterns;
        }

        [AgentTool(
            "Gets a list of all available composition patterns that can be used as image references for generating materials and terrain layers. " +
            "These patterns provide foundational designs like bricks, tiles, grids, and organic shapes.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Available)]
        public static async Task<GetCompositionPatternsOutput> GetCompositionPatterns()
        {
            try
            {
                // Call the new centralized function in AssetGenerators.
                var availablePatterns = await AssetGenerators.GetAvailableCompositionPatternsAsync();

                // Map the core CompositionPattern struct to the serializable output struct for the tool.
                var outputPatterns = availablePatterns.Select(p => new CompositionPatternInfoOutput
                {
                    AssetPath = p.AssetPath,
                    DisplayName = p.DisplayName,
                    Keywords = p.Keywords
                }).ToList();

                var output = new GetCompositionPatternsOutput
                {
                    Patterns = outputPatterns
                };

                return output;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw new Exception($"Error getting composition patterns: {ex.Message}", ex);
            }
        }
    }
}
