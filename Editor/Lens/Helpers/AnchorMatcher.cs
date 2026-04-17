using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    /// <summary>
    /// Advanced pattern matching helper for anchor-based operations with C# structure heuristics.
    /// Based on the improved anchor matching logic from manage_script_edits.py.
    /// </summary>
    static class AnchorMatcher
    {
        /// <summary>
        /// Find the best anchor match using improved heuristics.
        ///
        /// For patterns like \s*}\s*$ that are meant to find class-ending braces,
        /// this function uses heuristics to choose the most semantically appropriate match:
        ///
        /// 1. If preferLast=true, prefer the last match (common for class-end insertions)
        /// 2. Use indentation levels to distinguish class vs method braces
        /// 3. Consider context to avoid matches inside strings/comments
        /// </summary>
        /// <param name="pattern">Regex pattern to search for</param>
        /// <param name="text">Text to search in</param>
        /// <param name="options">Regex options</param>
        /// <param name="preferLast">If true, prefer the last match over the first</param>
        /// <returns>Match object of the best match, or null if no match found</returns>
        public static Match FindBestAnchorMatch(string pattern, string text, RegexOptions options = RegexOptions.Multiline, bool preferLast = true)
        {
            try
            {
                var regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
                var matches = regex.Matches(text).Cast<Match>().ToList();

                if (!matches.Any())
                {
                    return null;
                }

                // If only one match, return it
                if (matches.Count == 1)
                {
                    return matches[0];
                }

                // For patterns that look like they're trying to match closing braces at end of lines
                bool isClosingBracePattern = pattern.Contains("}") &&
                    (pattern.Contains("$") || pattern.EndsWith(@"\s*"));

                if (isClosingBracePattern && preferLast)
                {
                    // Use heuristics to find the best closing brace match
                    return FindBestClosingBraceMatch(matches, text);
                }

                // Default behavior: use last match if preferLast, otherwise first match
                return preferLast ? matches.Last() : matches.First();
            }
            catch (RegexMatchTimeoutException)
            {
                Debug.LogWarning($"AnchorMatcher: Regex timeout for pattern: {pattern}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"AnchorMatcher: Error matching pattern '{pattern}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find the best closing brace match using C# structure heuristics.
        ///
        /// Enhanced heuristics for scope-aware matching:
        /// 1. Prefer matches with lower indentation (likely class-level)
        /// 2. Prefer matches closer to end of file
        /// 3. Avoid matches that seem to be inside method bodies
        /// 4. For #endregion patterns, ensure class-level context
        /// 5. Validate insertion point is at appropriate scope
        /// </summary>
        /// <param name="matches">List of regex matches</param>
        /// <param name="text">The full text being searched</param>
        /// <returns>The best match object</returns>
        public static Match FindBestClosingBraceMatch(IList<Match> matches, string text)
        {
            if (matches == null || !matches.Any())
            {
                return null;
            }

            var scoredMatches = new List<(int score, Match match)>();
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var match in matches)
            {
                int score = 0;
                int startPos = match.Index;

                // Find which line this match is on
                int linesBefore = text.Substring(0, startPos).Count(c => c == '\n');
                int lineNum = linesBefore;

                if (lineNum < lines.Length)
                {
                    string lineContent = lines[lineNum];

                    // Calculate indentation level (lower is better for class braces)
                    int indentation = lineContent.Length - lineContent.TrimStart().Length;

                    // Prefer lower indentation (class braces are typically less indented than method braces)
                    // Max 20 points for indentation=0
                    score += Math.Max(0, 20 - indentation);

                    // Prefer matches closer to end of file (class closing braces are typically at the end)
                    int distanceFromEnd = lines.Length - lineNum;
                    // More points for being closer to end
                    score += Math.Max(0, 10 - distanceFromEnd);

                    // Look at surrounding context to avoid method braces
                    int contextStart = Math.Max(0, lineNum - 3);
                    int contextEnd = Math.Min(lines.Length, lineNum + 2);
                    var contextLines = lines.Skip(contextStart).Take(contextEnd - contextStart);

                    // Penalize if this looks like it's inside a method (has method-like patterns above)
                    foreach (var contextLine in contextLines)
                    {
                        if (Regex.IsMatch(contextLine, @"\b(void|public|private|protected)\s+\w+\s*\("))
                        {
                            score -= 5; // Penalty for being near method signatures
                        }
                    }

                    // Bonus if this looks like a class-ending brace (very minimal indentation and near EOF)
                    if (indentation <= 4 && distanceFromEnd <= 3)
                    {
                        score += 15; // Bonus for likely class-ending brace
                    }

                    // Additional context analysis
                    score += AnalyzeMatchContext(match, text, lines, lineNum);
                }

                scoredMatches.Add((score, match));
            }

            // Return the match with the highest score
            var bestMatch = scoredMatches.OrderByDescending(x => x.score).First();
            return bestMatch.match;
        }

        /// <summary>
        /// Analyze the context around a match to determine its semantic appropriateness
        /// </summary>
        static int AnalyzeMatchContext(Match match, string text, string[] lines, int lineNum)
        {
            int score = 0;

            try
            {
                // Check if we're inside a string or comment
                if (IsInsideStringOrComment(match, text))
                {
                    score -= 50; // Heavy penalty for being inside strings/comments
                }

                // Check for namespace context (positive for class-level operations)
                if (HasNamespaceContext(lines, lineNum))
                {
                    score += 5;
                }

                // Check for class declaration context
                if (HasClassDeclarationContext(lines, lineNum))
                {
                    score += 10;
                }

                // Check if this appears to be end of a using block or similar
                if (HasUsingBlockContext(lines, lineNum))
                {
                    score -= 10; // Don't prefer using block endings
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"AnchorMatcher: Error analyzing match context: {ex.Message}");
            }

            return score;
        }

        /// <summary>
        /// Check if the match is inside a string literal or comment
        /// </summary>
        static bool IsInsideStringOrComment(Match match, string text)
        {
            try
            {
                int position = match.Index;
                bool inString = false;
                bool inChar = false;
                bool inSingleLineComment = false;
                bool inMultiLineComment = false;
                bool escaped = false;

                for (int i = 0; i < position && i < text.Length; i++)
                {
                    char c = text[i];
                    char next = i + 1 < text.Length ? text[i + 1] : '\0';

                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\' && (inString || inChar))
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '\n')
                    {
                        inSingleLineComment = false;
                        continue;
                    }

                    if (!inString && !inChar && !inSingleLineComment)
                    {
                        if (c == '/' && next == '/')
                        {
                            inSingleLineComment = true;
                            i++; // Skip next character
                            continue;
                        }
                        if (c == '/' && next == '*' && !inMultiLineComment)
                        {
                            inMultiLineComment = true;
                            i++; // Skip next character
                            continue;
                        }
                        if (c == '*' && next == '/' && inMultiLineComment)
                        {
                            inMultiLineComment = false;
                            i++; // Skip next character
                            continue;
                        }
                    }

                    if (!inSingleLineComment && !inMultiLineComment)
                    {
                        if (c == '"' && !inChar)
                        {
                            inString = !inString;
                        }
                        else if (c == '\'' && !inString)
                        {
                            inChar = !inChar;
                        }
                    }
                }

                return inString || inChar || inSingleLineComment || inMultiLineComment;
            }
            catch
            {
                // If we can't determine, err on the side of caution
                return false;
            }
        }

        /// <summary>
        /// Check if there's a namespace declaration context around the line
        /// </summary>
        static bool HasNamespaceContext(string[] lines, int lineNum)
        {
            try
            {
                // Look backwards for namespace declaration
                for (int i = Math.Max(0, lineNum - 20); i < lineNum && i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("namespace "))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if there's a class declaration context around the line
        /// </summary>
        static bool HasClassDeclarationContext(string[] lines, int lineNum)
        {
            try
            {
                // Look backwards for class declaration
                for (int i = Math.Max(0, lineNum - 10); i < lineNum && i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (Regex.IsMatch(line, @"\b(public|private|protected|internal)?\s*(static|abstract|sealed)?\s*class\s+\w+"))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if this appears to be the end of a using block
        /// </summary>
        static bool HasUsingBlockContext(string[] lines, int lineNum)
        {
            try
            {
                // Look backwards for using declarations
                for (int i = Math.Max(0, lineNum - 5); i < lineNum && i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("using ") && lines[i].Contains("="))
                    {
                        return true; // This might be a using alias block
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Perform a simple anchor match without heuristics (for basic operations)
        /// </summary>
        /// <param name="pattern">Regex pattern to search for</param>
        /// <param name="text">Text to search in</param>
        /// <param name="options">Regex options for the search</param>
        /// <returns>The first match found, or null if no match or timeout occurs</returns>
        public static Match FindSimpleMatch(string pattern, string text, RegexOptions options = RegexOptions.Multiline)
        {
            try
            {
                var regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
                return regex.Match(text);
            }
            catch (RegexMatchTimeoutException)
            {
                Debug.LogWarning($"AnchorMatcher: Regex timeout for simple pattern: {pattern}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"AnchorMatcher: Error in simple match for pattern '{pattern}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find all matches for a pattern with timeout protection
        /// </summary>
        /// <param name="pattern">Regex pattern to search for</param>
        /// <param name="text">Text to search in</param>
        /// <param name="options">Regex options for the search</param>
        /// <returns>Collection of all matches found, or null if timeout or error occurs</returns>
        public static MatchCollection FindAllMatches(string pattern, string text, RegexOptions options = RegexOptions.Multiline)
        {
            try
            {
                var regex = new Regex(pattern, options, TimeSpan.FromSeconds(2));
                return regex.Matches(text);
            }
            catch (RegexMatchTimeoutException)
            {
                Debug.LogWarning($"AnchorMatcher: Regex timeout for pattern: {pattern}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"AnchorMatcher: Error finding all matches for pattern '{pattern}': {ex.Message}");
                return null;
            }
        }
    }
}
