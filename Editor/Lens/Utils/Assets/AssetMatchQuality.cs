using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Utils.Assets
{
    /// <summary>
    /// Shared constants and utilities for asset match quality thresholds and guidance generation.
    /// These values determine how we categorize semantic similarity scores and present results to users.
    /// </summary>
    internal static class AssetMatchQuality
    {
        // Similarity thresholds for categorizing match quality
        // TODO: Tune thresholds based on feedback (JIRA: https://jira.unity3d.com/browse/ASST-1979)
        public const float HighQualityThreshold = 0.10f;
        public const float MediumQualityThreshold = 0.08f;
        
        // Display limits for guidance text
        public const int MaxHighMatchAssetsToDisplay = 5;
        public const int MaxMediumMatchAssetsToDisplay = 5;
        public const int MaxLowMatchAssetsToDisplay = 3;
        
        /// <summary>
        /// Convert Unity Search score to normalized similarity (0..1).
        /// Unity Search uses inverted scores where lower is better.
        /// </summary>
        public static float ScoreToSimilarity(float score)
        {
            if (score <= 0f)
                return 0f;
            
            // Convert search score back to cosine similarity using the inverse of the formula used by Unity Search.
            var similarity = 1000f / score;
            return UnityEngine.Mathf.Clamp01(similarity);
        }
        
        /// <summary>
        /// Categorize similarity into quality levels.
        /// </summary>
        public static MatchQualityLevel GetQualityLevel(float similarity)
        {
            if (similarity >= HighQualityThreshold)
                return MatchQualityLevel.High;
            if (similarity >= MediumQualityThreshold)
                return MatchQualityLevel.Medium;
            if (similarity >= 0f)
                return MatchQualityLevel.Low;
            return MatchQualityLevel.None;
        }
        
        /// <summary>
        /// Get display string for match quality level.
        /// </summary>
        public static string GetQualityDisplayText(MatchQualityLevel level)
        {
            return level switch
            {
                MatchQualityLevel.High => "High",
                MatchQualityLevel.Medium => "Medium",
                MatchQualityLevel.Low => "Low",
                _ => string.Empty
            };
        }
        
        /// <summary>
        /// Builds guidance on how to present asset search results to users.
        /// </summary>
        public static string BuildGuidanceText(AssetMatchCategories categories)
        {
            var guidance = new StringBuilder();
            guidance.AppendLine("=== RESULT PRIORITIZATION ===");
            guidance.AppendLine();
            
            // Define priority levels in a declarative, data-driven way
            var priorities = new[]
            {
                new PriorityConfig
                {
                    Label = "PRIORITY 1",
                    MaxDisplay = MaxHighMatchAssetsToDisplay,
                    Description = "Keyword + High Content Match",
                    Notes = new[] { "→ IDEAL matches - both name and content match. Lead with these!" },
                    GetAssets = c => c.KeywordAndHighSemantic
                },
                new PriorityConfig
                {
                    Label = "PRIORITY 2",
                    MaxDisplay = MaxMediumMatchAssetsToDisplay,
                    Description = "Keyword Match Only",
                    Notes = new[]
                    {
                        "→ Name matches query but content may not strongly match.",
                        "→ User may want these by name. Show selectively with context."
                    },
                    GetAssets = c => c.KeywordOnly
                },
                new PriorityConfig
                {
                    Label = "PRIORITY 3",
                    MaxDisplay = MaxHighMatchAssetsToDisplay,
                    Description = "High Content Match Only",
                    Notes = new[]
                    {
                        "→ Strong content match but name doesn't contain exact query keywords.",
                        "→ Recommend if name is semantically similar to the query, or if Priority 1 is empty."
                    },
                    GetAssets = c => c.HighSemanticOnly
                },
                new PriorityConfig
                {
                    Label = "PRIORITY 4",
                    MaxDisplay = MaxMediumMatchAssetsToDisplay,
                    Description = "Weak Content Match",
                    Notes = new[]
                    {
                        "→ Low content similarity, no keyword match.",
                        "→ Use as fallback if better matches unavailable."
                    },
                    GetAssets = c => c.MediumSemanticOnly
                },
                new PriorityConfig
                {
                    Label = "LOW QUALITY",
                    MaxDisplay = MaxLowMatchAssetsToDisplay,
                    Description = "Weak or no matches",
                    Notes = new[] { "→ Weak/no content match, no keyword match. Mention only if necessary." },
                    GetAssets = c => c.LowQuality
                }
            };
            
            // Track which priorities have matches for strategy generation
            var priorityHasMatches = new bool[priorities.Length];
            
            // Append each priority section that has assets
            for (var i = 0; i < priorities.Length; i++)
            {
                var priority = priorities[i];
                var assets = priority.GetAssets(categories);
                
                if (assets.Count > 0)
                {
                    priorityHasMatches[i] = true;
                    AppendPrioritySection(guidance, priority.Label, assets, 
                        priority.MaxDisplay, priority.Description, priority.Notes);
                }
            }
            
            // Presentation strategy based on which priorities have matches
            AppendPresentationStrategy(guidance, priorityHasMatches);
            
            return guidance.ToString();
        }
        
        static void AppendPrioritySection(StringBuilder guidance, string label, List<string> assets, 
            int maxDisplay, string description, string[] notes)
        {
            guidance.AppendLine($"**{label}: {description}**");
            guidance.AppendLine($"Count: {assets.Count}");
            
            foreach (var note in notes)
                guidance.AppendLine(note);
            
            guidance.AppendLine();
            guidance.AppendLine("Assets:");
            for (var i = 0; i < Mathf.Min(maxDisplay, assets.Count); i++)
                guidance.AppendLine($"  - {assets[i]}");
            
            if (assets.Count > maxDisplay)
                guidance.AppendLine($"  ... and {assets.Count - maxDisplay} more");
            
            guidance.AppendLine();
        }
        
        static void AppendPresentationStrategy(StringBuilder guidance, bool[] priorityHasMatches)
        {
            guidance.AppendLine("=== PRESENTATION STRATEGY ===");
            
            if (priorityHasMatches[0])
                guidance.AppendLine("✓ Lead with Priority 1 (ideal matches)");
            else if (priorityHasMatches[1])
                guidance.AppendLine("→ No ideal matches. Lead with Priority 2 (keyword matches)");
            else if (priorityHasMatches[2])
                guidance.AppendLine("→ No keyword matches. Lead with Priority 3 (content matches)");
            
            guidance.AppendLine();
        }
        
        class PriorityConfig
        {
            public string Label { get; set; }
            public int MaxDisplay { get; set; }
            public string Description { get; set; }
            public string[] Notes { get; set; }
            public Func<AssetMatchCategories, List<string>> GetAssets { get; set; }
        }
    }
    
    enum MatchQualityLevel
    {
        None,
        Low,
        Medium,
        High
    }
}
