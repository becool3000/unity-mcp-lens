using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    internal static class MessageUtils
    {
        public const string k_Today = "000000#Today";
        public const string k_Yesterday = "000001#Yesterday";
        public const string k_ThisWeek = "000003#This Week";
        public const string k_ThisMonth = "000004#This Month";

        const string k_BoundaryRegexComplete = @"\r?\n--boundary-\w{32}\r?\n({[\s\S]*?})\r?\nboundary-\w{32}\r?\n";
        const string k_BoundaryRegexBegin = @"\r?\n--boundary-\w{32}\r?\n";

        static readonly Regex k_FootnoteInlineRegex = new(@"\[\^(\d+)\^\](?=(?:\[\^(?:\d+)\^\])*\.(?:\s|\r))", RegexOptions.Compiled);
        static readonly Regex k_FootnoteURLsRegex = new( @"(\r\n|\n)\[?\^(\d+)\^\]?: \(source: \[(.*?)\]\((.*?)\)\)", RegexOptions.Compiled);
        static readonly Regex k_FootnoteURLsOldFormatRegex = new( @"(\r\n|\n)(?:> )?\[\^(\d+)\^\]: \[(.*?)\]\((.*?)\)", RegexOptions.Compiled);
        static readonly Regex k_FootnoteURLsNoTitleRegex = new( @"(\r\n|\n)\[\^(\d+)\^\]: \((.*?)\)", RegexOptions.Compiled);

        static readonly Regex k_FootnoteInvalidInlineRegex = new(@"\s*(\[(\d+)\])+(?=[:.]($|\s|\n))", RegexOptions.Compiled);

        static readonly Regex k_SourceMarkerRegex = new(@"{{source:(\d+)}}", RegexOptions.Compiled);
        static readonly Regex k_SourceMarkersSequenceRegEx = new (@"{{source:\d+}}({{source:(\d+)}})+", RegexOptions.Compiled);
        static readonly Regex k_FirstSourceMarkerRegEx = new Regex(@"(?<!{{source:\d+}})({{source:\d+}})", RegexOptions.Compiled);

        static readonly Regex k_BoldRegex = new(@"\*\*(.*?)\*\*", RegexOptions.Compiled);

        static readonly DayOfWeek k_FirstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        static readonly StringBuilder k_StringBuilder = new();

        public enum FootnoteFormat
        {
            SpritesForText,
            SimpleIndexForClipboard,
        }

        struct SourceOrFootnote
        {
            public bool IsSource;
            public int FootnoteIndex;
            public int SourceIndex;
            public int ForwardTo;
            public int FinalSourceIndex;
            public string Title;
            public string URL;
        };

        static string ProcessChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return chunk;
            }

            var matches = k_BoldRegex.Matches(chunk);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var boldText = match.Groups[1].Value;
                chunk = chunk.Replace(match.Value, $"<b>{boldText}</b>");
            }

            return chunk;
        }

        public static void ProcessContent(string content, bool isComplete, ref IList<SourceBlock> sourceBlocks, out string messageContent, FootnoteFormat mode = FootnoteFormat.SpritesForText)
        {
            List<SourceOrFootnote> sourceOrFootnotes = new();

            k_StringBuilder.Clear();

            var chunks = Regex.Split(content, k_BoundaryRegexComplete);

            for (var i = 0; i < chunks.Length; i += 2)
            {
                // Even chunk - text
                // If this is the last chunk, we chop off the last word unless the message is complete
                // If the starting block regex is here, we don't use any of that text
                var lastBlock = (i == chunks.Length - 1);
                string text;
                if (lastBlock && !isComplete)
                {
                    var subChunks = Regex.Split(chunks[i], k_BoundaryRegexBegin);

                    if (subChunks.Length < 2)
                    {
                        var lastSpace = subChunks[0].LastIndexOf(' ');

                        if (lastSpace > 0)
                        {
                            text = ProcessChunk(subChunks[0].Substring(0, lastSpace));
                        }
                        else
                        {
                            text = ProcessChunk(subChunks[0]);
                        }
                    }
                    else
                    {
                        text = ProcessChunk(subChunks[0]);
                    }
                }
                else
                {
                    text = ProcessChunk(chunks[i]);
                }

                if (isComplete)
                {
                    // Replace inline footnotes with placeholders
                    text = k_FootnoteInlineRegex.Replace(text, match =>
                    {
                        var index = int.Parse(match.Groups[1].Value);
                        sourceOrFootnotes.Add(new SourceOrFootnote() { IsSource = false, FootnoteIndex = index });

                        return $"{{{{source:{sourceOrFootnotes.Count}}}}}";
                    });
                }

                k_StringBuilder.Append(text);

                // If this is not the last chunk placeholder source index
                if (!lastBlock)
                {
                    if (isComplete)
                    {
                        // Replace source (boundary tag) with placeholders
                        sourceOrFootnotes.Add(new SourceOrFootnote() { IsSource = true, FootnoteIndex = 0, SourceIndex = i / 2 });

                        k_StringBuilder.Append($"{{{{source:{sourceOrFootnotes.Count}}}}}");
                    }
                    else
                    {
                        k_StringBuilder.Append(GetReferenceString(i / 2 + 1));
                    }
                }
            }

            // If the message is complete, store all sources from odd chunks
            if (isComplete)
            {
                for (var i = 1; i < chunks.Length; i += 2)
                {
                    try
                    {
                        var sourceBlock = JsonUtility.FromJson<SourceBlock>(chunks[i]);

                        var sourceEntryIndex = sourceOrFootnotes.FindIndex(e => e.IsSource && e.SourceIndex == i/2);
                        if (sourceEntryIndex != -1)
                        {
                            var sourceEntry = sourceOrFootnotes[sourceEntryIndex];
                            sourceEntry.Title = sourceBlock.reason;
                            sourceEntry.URL = sourceBlock.source;
                            sourceOrFootnotes[sourceEntryIndex] = sourceEntry;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse source block: {e}");
                    }
                }
            }

            // Parse sources and footnotes, if any, and consolidate them as SourceBlocks
            if (sourceOrFootnotes.Count > 0)
            {
                sourceBlocks ??= new List<SourceBlock>();

                messageContent = k_StringBuilder.ToString();

                // Fill in footnote title/URL found at end of text
                messageContent = k_FootnoteURLsRegex.Replace(messageContent, match =>
                {
                    AddFootnoteAsSource(match, sourceOrFootnotes);
                    return "";
                });
                messageContent = k_FootnoteURLsOldFormatRegex.Replace(messageContent, match =>
                {
                    AddFootnoteAsSource(match, sourceOrFootnotes);
                    return "";
                });
                messageContent = k_FootnoteURLsNoTitleRegex.Replace(messageContent, match =>
                {
                    AddFootnoteAsSource(match, sourceOrFootnotes, true);
                    return "";
                });

                // Create SourceBlocks from all source and footnote information
                for (var i = 0; i < sourceOrFootnotes.Count; i++)
                {
                    var footnoteInfo = sourceOrFootnotes[i];
                    if (!string.IsNullOrEmpty(footnoteInfo.URL))
                    {
                        var duplicateURLIndex = sourceOrFootnotes.FindIndex(e =>
                            !string.IsNullOrEmpty(e.URL) && e.URL.Equals(footnoteInfo.URL));

                        if (duplicateURLIndex != -1 && duplicateURLIndex < i)
                        {
                            footnoteInfo.FinalSourceIndex = sourceOrFootnotes[duplicateURLIndex].FinalSourceIndex;
                        }
                        else
                        {
                            sourceBlocks.Add(new SourceBlock()
                            {
                                reason = footnoteInfo.Title,
                                source = footnoteInfo.URL
                            });

                            footnoteInfo.FinalSourceIndex = sourceBlocks.Count;
                        }

                        sourceOrFootnotes[i] = footnoteInfo;
                    }
                }

                // Reorder placeholders by final source index; remove duplicates and unresolved references (index 0)
                messageContent = k_SourceMarkersSequenceRegEx.Replace(messageContent, m =>
                {
                    var validFootnoteIndices = new List<int>();

                    var matches = k_SourceMarkerRegex.Matches(m.Value);
                    foreach (Match match in matches)
                    {
                        var index = int.Parse(match.Groups[1].Value);
                        var footnoteInfo = sourceOrFootnotes[index-1];

                        if (footnoteInfo.FinalSourceIndex == 0)
                            continue;

                        validFootnoteIndices.Add(index);
                    }

                    validFootnoteIndices.Sort((i, j) =>
                    {
                        if (sourceOrFootnotes[i - 1].FinalSourceIndex == sourceOrFootnotes[j - 1].FinalSourceIndex)
                            return 0;
                        return sourceOrFootnotes[i - 1].FinalSourceIndex < sourceOrFootnotes[j - 1].FinalSourceIndex ? -1 : 1;
                    });

                    if (validFootnoteIndices.Count == 0)
                        return "";

                    var sb = new StringBuilder();
                    var lastSourceIndex = -1;
                    foreach (var index in validFootnoteIndices)
                    {
                        if (sourceOrFootnotes[index - 1].FinalSourceIndex == lastSourceIndex)
                            continue;

                        sb.Append($"{{{{source:{index}}}}}");

                        lastSourceIndex = sourceOrFootnotes[index - 1].FinalSourceIndex;
                    }

                    return sb.ToString();
                });

                // For clipboard string add a space for readability before source marker (which becomes a footnote, e.g. " [1]")
                if (mode == FootnoteFormat.SimpleIndexForClipboard)
                {
                    messageContent = k_FirstSourceMarkerRegEx.Replace(messageContent, m =>
                    {
                        return $" {m.Value}";
                    });
                }

                // Replace invalid non-footnote markers
                messageContent = k_FootnoteInvalidInlineRegex.Replace(messageContent, "");

                // Replace all placeholders with final footnotes
                messageContent = k_SourceMarkerRegex.Replace(messageContent, match =>
                {
                    var index = int.Parse(match.Groups[1].Value);
                    var footnoteInfo = sourceOrFootnotes[index - 1];

                    if (footnoteInfo.FinalSourceIndex == 0)
                    {
                        return "";
                    }

                    // Sprites with indices for text field output
                    if (mode == FootnoteFormat.SpritesForText)
                        return GetReferenceString(footnoteInfo.FinalSourceIndex);

                    // Simple indices for clipboard output
                    return $"[{footnoteInfo.FinalSourceIndex}]";
                });
            }
            else
            {
                messageContent = k_StringBuilder.ToString();

                // Replace invalid non-footnote markers
                messageContent = k_FootnoteInvalidInlineRegex.Replace(messageContent, "");
            }
        }

        public static void AppendSourceBlocks(IList<SourceBlock> sourceBlocks, ref string messageContent)
        {
            if (sourceBlocks == null || sourceBlocks.Count == 0)
            {
                return;
            }

            messageContent += "\n\nSources:";

            var index = 1;
            foreach (var s in sourceBlocks)
            {
                messageContent += $"\n[{index++}] {s.reason} - {s.source}";
            }
        }

        private static void AddFootnoteAsSource(Match match, List<SourceOrFootnote> sourceOrFootnotes, bool noTitle = false)
        {
            var index = int.Parse(match.Groups[2].Value);
            var title = noTitle ? "Source" : match.Groups[3].Value;
            var URL = match.Groups[noTitle ? 3 : 4].Value;

            var sourceEntryIndex = sourceOrFootnotes.FindIndex(e => !e.IsSource && e.FootnoteIndex == index);
            if (sourceEntryIndex != -1)
            {
                var sourceEntry = sourceOrFootnotes[sourceEntryIndex];
                sourceEntry.Title = title;
                sourceEntry.URL = URL;
                sourceOrFootnotes[sourceEntryIndex] = sourceEntry;
            }
        }

        public static string GetReferenceString(int index)
        {
            return $"<link=\"{AssistantConstants.SourceReferencePrefix}{index - 1}\"><size=11><b><color=#{AssistantConstants.SourceReferenceColor}> [ {index} ]</color></b></size></link>";
        }

        public static bool GetAssetFromLink(string linkUrl, out Object asset)
        {
            asset = null;
            try
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(linkUrl);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return false;
                }

                var assetType = AssetDatabase.GetMainAssetTypeFromGUID(new GUID(linkUrl));
                if (assetType == default)
                {
                    return false;
                }

                asset = AssetDatabase.LoadAssetAtPath(assetPath, assetType);
                return asset != null;
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to load asset from link: " + e);
                asset = null;
                return false;
            }
        }

        public static string GetWebLink(string url, string title)
        {
            return $"<a href=\"{url}\">{title}</a>";
        }

        public static string RichColor(this string text, string hexColor)
        {
            return $"<color={hexColor}>{text}</color>";
        }

        public static string GetTextWithMaxLength(this string text, int maxLength)
        {
            if (maxLength <= AssistantConstants.TextCutoffSuffix.Length)
            {
                throw new ArgumentException("Max length must be greater than " + AssistantConstants.TextCutoffSuffix.Length);
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            maxLength -= AssistantConstants.TextCutoffSuffix.Length;
            return text.Substring(0, Mathf.Min(maxLength, text.Length)) + AssistantConstants.TextCutoffSuffix;
        }

        public static string GetMessageTimestampGroup(long timeStampRaw, long nowRaw)
        {
            var nowTime = DateTimeOffset.FromUnixTimeMilliseconds(nowRaw);
            var yesterdayTime = nowTime.AddDays(-1);
            var timeStamp = DateTimeOffset.FromUnixTimeMilliseconds(timeStampRaw);
            var timeDiff = nowTime - timeStamp;
            if (timeDiff.Days <= 1)
            {
                if (nowTime.Day == timeStamp.Day)
                {
                    return k_Today;
                }

                if (yesterdayTime.DayOfYear == timeStamp.DayOfYear)
                {
                    return k_Yesterday;
                }
            }

            var startOfWeekNow = nowTime.AddDays(-(int)nowTime.DayOfWeek + (int)k_FirstDayOfWeek);
            var startOfWeekTimeStamp = timeStamp.AddDays(-(int)timeStamp.DayOfWeek + (int)k_FirstDayOfWeek);

            if (startOfWeekNow.Date == startOfWeekTimeStamp.Date)
                return k_ThisWeek;

            if (nowTime.Month == timeStamp.Month && nowTime.Year == timeStamp.Year)
                return k_ThisMonth;

            string yearMonthKey = $"{5000 - timeStamp.Year}{50 - timeStamp.Month}";
            return $"{yearMonthKey}#{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(timeStamp.Month)} {timeStamp.Year}";
        }
    }
}
