using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.AI.Assistant.Tools.Editor
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
            
            guidance.AppendLine();
            guidance.AppendLine("NOTE: Description = Model-generated semantic description based on visual content. Match Quality = Embedding similarity score.");

            return guidance.ToString();
        }
        
        /// <summary>
        /// Configuration for a priority level in the guidance text.
        /// </summary>
        readonly struct PriorityConfig
        {
            public string Label { get; init; }
            public int MaxDisplay { get; init; }
            public string Description { get; init; }
            public string[] Notes { get; init; }
            public Func<AssetMatchCategories, List<string>> GetAssets { get; init; }
        }
        
        /// <summary>
        /// Appends a priority section to the guidance text.
        /// </summary>
        static void AppendPrioritySection(
            StringBuilder guidance,
            string priorityLabel,
            List<string> assets,
            int maxDisplay,
            string description,
            params string[] notes)
        {
            guidance.AppendLine($"{priorityLabel} ({assets.Count}): {description}");
            foreach (var asset in assets.Take(maxDisplay))
                guidance.AppendLine($"  • {asset}");
            
            foreach (var note in notes)
                guidance.AppendLine(note);
            
            guidance.AppendLine();
        }
        
        /// <summary>
        /// Appends presentation strategy based on which priority categories have matches.
        /// Uses array indices: [0]=Priority1, [1]=Priority2, [2]=Priority3, [3]=Priority4, [4]=LowQuality
        /// </summary>
        static void AppendPresentationStrategy(StringBuilder guidance, bool[] priorityHasMatches)
        {
            guidance.AppendLine("=== PRESENTATION STRATEGY ===");
            
            var hasPriority1 = priorityHasMatches.Length > 0 && priorityHasMatches[0];
            var hasPriority2 = priorityHasMatches.Length > 1 && priorityHasMatches[1];
            var hasPriority3 = priorityHasMatches.Length > 2 && priorityHasMatches[2];
            var hasPriority4 = priorityHasMatches.Length > 3 && priorityHasMatches[3];
            
            // Strategy mapping: determine best approach based on which priorities have matches
            var strategy = DeterminePresentationStrategy(hasPriority1, hasPriority2, hasPriority3, hasPriority4);
            guidance.AppendLine(strategy);
        }
        
        /// <summary>
        /// Determines the presentation strategy message based on which priority levels have matches.
        /// </summary>
        static string DeterminePresentationStrategy(bool p1, bool p2, bool p3, bool p4)
        {
            // Use pattern matching for cleaner logic
            return (p1, p2, p3, p4) switch
            {
                (true, _, _, _) => "Lead with Priority 1 (keyword + content). Mention others as alternatives.",
                (false, true, true, _) => "Present both keyword-only and content-only matches. Explain the difference.",
                (false, true, false, _) => "Only keyword matches. Explain content may differ from query.",
                (false, false, true, _) => "Lead with content matches. No keyword matches found.",
                (false, false, false, true) => "Only moderate matches. Explain they're loosely related.",
                _ => "No quality matches found."
            };
        }
    }
    
    internal enum MatchQualityLevel
    {
        None,
        Low,
        Medium,
        High
    }
}

