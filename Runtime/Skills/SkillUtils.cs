using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Skills
{
    class SkillUtils
    {
        static readonly Regex k_UnityPackageVersionRegex =
            new(@"^(>=?|<=?|\^|~)?\d+\.\d+\.\d+(-[a-zA-Z]+(\.\d+)?)?$", RegexOptions.Compiled);
        static readonly Regex k_ValidSkillNameRegex =
            new(@"^[A-Za-z0-9\-]+$", RegexOptions.Compiled); // Only alphanumeric and hyphen

        internal static readonly HashSet<string> k_CommonFrontmatterFields = new() {
            "name", "description", "required_packages", "tools", "metadata", "enabled"
        };

        class YamlFrontmatter
        {
            public string name;
            public string description;
            public Dictionary<string, string> required_packages;
            public List<string> tools;
            public bool enabled;
        }
        
        internal static readonly string CommonFrontmatterFieldNames = string.Join(", ", k_CommonFrontmatterFields);

        /// <summary>
        /// Creates a SkillDefinition by scanning a folder containing a SKILL.md file.
        /// Reads the file, parses YAML frontmatter, and populates all properties.
        /// </summary>
        /// <param name="skillFile">Absolute path to a SKILL.md file</param>
        /// <returns>A fully populated SkillDefinition</returns>
        /// <exception cref="InvalidOperationException">Thrown when the skill file is invalid or cannot be parsed</exception>
        /// <exception cref="FileNotFoundException">Thrown when the SKILL.md file isn't found</exception>
        internal static SkillDefinition FromFolder(string skillFile)
        {
            var skillFolderPath = Path.GetDirectoryName(skillFile);

            try
            {
                var skillMdPath = Path.Combine(skillFolderPath, "SKILL.md");

                if (!File.Exists(skillMdPath))
                {
                    InternalLog.LogWarning($"[SkillUtils.FromFolder] SKILL.md not found at: {skillMdPath}");
                    throw new FileNotFoundException($"SKILL.md not found at: {skillMdPath}");
                }

                // Read the full content
                string content = File.ReadAllText(skillMdPath);

                // Parse YAML frontmatter to extract name and description
                var frontmatter = ExtractInfoFromYamlFrontmatter(content);

                if (string.IsNullOrEmpty(frontmatter.name) || string.IsNullOrEmpty(frontmatter.description))
                {
                    InternalLog.LogWarning($"[SkillUtils.FromFolder] Failed to parse name/description from: {skillMdPath}");
                    throw new InvalidOperationException($"Failed to parse name/description from: {skillMdPath}");
                }

                var skill = new SkillDefinition()
                    .WithName(frontmatter.name)
                    .WithDescription(frontmatter.description)
                    .WithPath(skillFile)
                    .WithContent(content)
                    .SetEnabled(frontmatter.enabled);

                // Parse required_packages string (format: "pkg1: version1, pkg2: version2")
                if (frontmatter.required_packages?.Count > 0)
                {
                    foreach (var entry in frontmatter.required_packages)
                    {
                        skill = skill.WithRequiredPackage(entry.Key, entry.Value);
                    }
                }

                // Parse tools (tool names from YAML list)
                if (frontmatter.tools?.Count > 0)
                {
                    foreach (var toolName in frontmatter.tools)
                    {
                        skill = skill.WithTool(toolName);
                    }
                }

                // Parse all files and for all but the SKILL.md itself, add them as resources
                var allFiles = Directory.GetFiles(skillFolderPath, "*", SearchOption.AllDirectories);
                foreach (var file in allFiles)
                {
                    if (file != skillMdPath)
                    {
                        // make the path relative to the skill folder
                        var relativePath = Path.GetRelativePath(skillFolderPath, file);
                        try
                        {
                            var resource = new FileSkillResource(file);
                            skill = skill.WithResource(relativePath, resource);

                            InternalLog.Log($"[SkillUtils.FromFolder] Adding resource file {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            InternalLog.LogError($"[SkillUtils.FromFolder] Error adding resource: {ex.Message}");
                        }
                    }
                }

                return skill;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid skill data in '{skillFolderPath}': {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[SkillUtils.FromFolder] Error scanning folder '{skillFolderPath}': {ex.Message}");
                throw new InvalidOperationException($"Error scanning folder '{skillFolderPath}': {ex.Message}", ex);
            }
        }

        static YamlFrontmatter ExtractInfoFromYamlFrontmatter(string content)
        {
            var frontmatter = GetFrontmatterFromSkillFile(content);

            // Extract the "name" field from the frontmatter
            var name = ExtractFrontmatterScalarField(frontmatter, "name");

            // Extract the "description" field from the frontmatter
            var description = ExtractFrontmatterScalarField(frontmatter, "description");

            // Extract the "packages" field from the frontmatter, as a dictionary
            var packageDict = ExtractFrontmatterMappingField(frontmatter, "required_packages", true);

            // Extract the "tools" field from the frontmatter, as a list
            var toolsList = ExtractFrontmatterListField(frontmatter, "tools", true);

            // Extract the "enabled" field from the frontmatter
            var enabledValue = ExtractFrontmatterScalarField(frontmatter, "enabled", true);
            var enabledAsBool = true;

            if (!string.IsNullOrEmpty(enabledValue))
            {
                // Variations of 'true' and 'false' are modern standard; throw if the value is simply unexpected
                if (!bool.TryParse(enabledValue, out enabledAsBool))
                {
                    throw new InvalidOperationException($"Invalid 'enabled' field value in YAML frontmatter: '{enabledValue}'. Expected 'True' or 'False', or variations like 'true'/'TRUE'.");
                }
            }

            var frontmatterResult = new YamlFrontmatter()
            {
                name = name,
                description = description,
                required_packages = packageDict,
                tools = toolsList,
                enabled = enabledAsBool
            };

            return frontmatterResult;
        }

        private static string GetFrontmatterFromSkillFile(string content)
        {
            // Match YAML frontmatter block (content between --- delimiters at start of file)
            var frontmatterPattern = @"^---\s*\n(.*?)\n---";
            var frontmatterMatch = Regex.Match(content, frontmatterPattern, RegexOptions.Singleline);

            if (!frontmatterMatch.Success)
            {
                throw new InvalidOperationException("Missing or invalid YAML frontmatter (must start with --- and end with ---)");
            }

            var frontmatter = frontmatterMatch.Groups[1].Value;
            return frontmatter;
        }

        /// <summary>
        /// Validates that every top-level field in the YAML frontmatter block is in the known set.
        /// Returns any field name like "fieldname:" we didn't whitelist.
        /// </summary>
        /// <returns>A list of unknown field names found in the frontmatter (without the colon), or null if there was nothing to return.</returns>
        internal static List<string> GetUncommonFrontmatterFields(string skillFilePath)
        {
            try
            {
                string content = File.ReadAllText(skillFilePath);
                string frontmatter = GetFrontmatterFromSkillFile(content);
                
                List<string> uncommonFields = new List<string>();

                foreach (var line in frontmatter.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Indented lines are nested values (e.g. list items, mapping entries) — skip.
                    if (char.IsWhiteSpace(line[0]))
                        continue;

                    var colonIdx = line.IndexOf(':');
                    if (colonIdx <= 0)
                        continue;

                    var fieldName = line.Substring(0, colonIdx).Trim();
                    if (!k_CommonFrontmatterFields.Contains(fieldName))
                        uncommonFields.Add(fieldName);
                }

                return uncommonFields;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[SkillUtils.GetUncommonFrontmatterFields] Error loading or validating frontmatter in '{skillFilePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract a simple scalar field from YAML frontmatter, return as a string
        /// YAML example: A "name" scalar starts with "name:" followed in the same line by a space and value like "my_skill_name".
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a required field is missing or empty</exception>
        static string ExtractFrontmatterScalarField(string frontmatter, string fieldName, bool isOptional = false)
        {
            var pattern = $@"^\s*{fieldName}\s*:[ \t]*(.+?)[ \t]*$";
            var match = Regex.Match(frontmatter, pattern, RegexOptions.Multiline);
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                if (!isOptional)
                    throw new InvalidOperationException($"Empty '{fieldName}' field value in YAML frontmatter");

                return null;
            }

            if (!isOptional)
                throw new InvalidOperationException($"Missing  '{fieldName}' field in YAML frontmatter");

            return null;
        }

        /// <summary>
        /// Extract a list field from YAML frontmatter, return as a C# list of strings.
        /// YAML example: A "tools" list starts with "tools:" followed by indented list items like "  - Unity.Profiler.Initialize".
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a required field is missing or has invalid format</exception>
        static List<string> ExtractFrontmatterListField(string frontmatter, string fieldName, bool optional = false)
        {
            var lines = frontmatter.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int listStartIdx = -1;
            const int listItemIndent = 2;

            // Find the field line (e.g., tools:)
            var fieldMarker = fieldName + ":";
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.Trim();
                if (trimmed == fieldMarker)
                {
                    listStartIdx = i + 1;
                    break;
                }
            }

            if (listStartIdx == -1)
            {
                if (optional)
                {
                    return null;
                }
                throw new InvalidOperationException($"Field '{fieldName}' not found as a list in frontmatter.");
            }

            // Parse indented lines that start with "- "
            var list = new List<string>();
            for (int i = listStartIdx; i < lines.Length; i++)
            {
                var line = lines[i];

                // End of this list block, we don't expect empty lines
                if (string.IsNullOrWhiteSpace(line))
                    break;

                int leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

                // End of this list block, we expect only valid entries with the correct indentation
                if (leadingSpaces < listItemIndent)
                    break;

                var trimmed = line.Trim();

                // Check if this is a list item (starts with "- ")
                if (trimmed.StartsWith("- "))
                {
                    var value = trimmed.Substring(2).Trim().Trim('"', '\'');
                    if (string.IsNullOrEmpty(value))
                    {
                        throw new InvalidOperationException($"Empty list item in '{fieldName}' in YAML frontmatter: '{line}'. Expected format: '  - item_value'.");
                    }
                    list.Add(value);
                }
                else
                {
                    // End of this list block, invalid line without list marker
                    throw new InvalidOperationException($"List '{fieldName}' in YAML frontmatter stopped at invalid line: '{line}'. Expected format: '  - item_value'.");
                }
            }
            return list;
        }

        /// <summary>
        /// Extract a mapping field from YAML frontmatter, return as a C# dictionary.
        /// YAML example: A "packages" mapping starts with "packages:" followed by indented key/value lines like "  com.unity.2d.tooling: 1.0.0".
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a required field is missing or has invalid format</exception>
        static Dictionary<string, string> ExtractFrontmatterMappingField(string frontmatter, string fieldName, bool optional = false)
        {
            var lines = frontmatter.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int dictStartIdx = -1;
            const int dictItemIndent = 2;

            // Find the field line (e.g., mydict:)
            var fieldMarker = fieldName + ":";
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.Trim();
                if (trimmed == fieldMarker)
                {
                    dictStartIdx = i + 1;
                    break;
                }
            }
            
            if (dictStartIdx == -1)
            {
                if (optional)
                {
                    return null;
                }
                throw new InvalidOperationException($"Field '{fieldName}' not found as a dictionary in frontmatter.");
            }
            
            // Parse indented lines of key/value pairs
            var dict = new Dictionary<string, string>();
            for (int i = dictStartIdx; i < lines.Length; i++)
            {
                var line = lines[i];
                
                // End of this dictionary block, we don't expect empty lines
                if (string.IsNullOrWhiteSpace(line))
                    break;
                
                int leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

                // End of this dictionary block, we expect only valid entries with the correct indentation
                if (leadingSpaces < dictItemIndent)
                    break;
                
                var trimmed = line.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = trimmed.Substring(0, colonIdx).Trim().Trim('"', '\'');
                    var value = trimmed.Substring(colonIdx + 1).Trim().Trim('"', '\'');
                    dict[key] = value;

                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    {
                        throw new InvalidOperationException($"Invalid key/value pair in mapping '{fieldName}' in YAML frontmatter: '{line}'. Both key and value must be non-empty, ex.: '  package.name: version'.");
                    }
                }
                else
                {
                    // End of this dictionary block, invalid line without key/value pair
                    throw new InvalidOperationException($"Mapping '{fieldName}' in YAML frontmatter stopped at invalid line: '{line}'. A format like '  package.name: version' is expected.");
                }
            }
            return dict;
        }

        public static bool IsValidUnityPackageVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;
            return k_UnityPackageVersionRegex.IsMatch(version);
        }

        public static bool IsValidSkillName(string toolId)
        {
            // Only allow alphanumeric and hyphen, underscore is not allowed
            if (string.IsNullOrEmpty(toolId))
                return false;
            return k_ValidSkillNameRegex.IsMatch(toolId);
        }
    }
}
