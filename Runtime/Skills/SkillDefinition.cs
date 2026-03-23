using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// Metadata for a skill definition. This data includes key information to choose skills.
    /// If added from a SKILL.md file, the name, description, and required packages come from YAML frontmatter.
    /// </summary>
    class SkillMetaData
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("description", Required = Required.Always)]
        public string Description { get; set; }

        [JsonProperty("required_packages", Required = Required.Always)]
        public Dictionary<string, string> RequiredPackages { get; set; } = new();

        [JsonProperty("resources", Required = Required.Always)]
        public List<string> AvailableResourcePaths { get; set; } = new();

        [JsonProperty("tools", Required = Required.Always)]
        public List<string> Tools { get; set; } = new();

        [JsonProperty("enabled", Required = Required.Always)]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Represents a single skill with its metadata and content.
    /// Provides builder methods for creating skill definitions.
    /// </summary>
    class SkillDefinition
    {
        public SkillMetaData MetaData { get; set; } = new();

        public List<string> AvailableResources => new(Resources.Keys);

        [JsonProperty("path", Required = Required.Always)]
        public string Path { get; set; }

        /// <summary>
        /// The resources scanned from a skill folder or added directly via C# code. May be empty.
        /// </summary>
        public Dictionary<string, ISkillResource> Resources { get; set; } = new();

        /// <summary>
        /// The full content of a SKILL.md file, including YAML frontmatter and markdown body.
        /// </summary>
        internal string Content { get; set; }
        
        /// <summary>
        /// A tag to differentiate skills added by various C# code, to allow to identify and clear skills by tag.
        /// </summary>
        public List<string> Tags { get; set; } = new();

        public bool IsValid => !string.IsNullOrEmpty(MetaData?.Name)
                               && !string.IsNullOrEmpty(MetaData?.Description)
                               && !string.IsNullOrEmpty(Content)
                               && Tags.Count > 0;

        internal SkillDefinition WithName(string name)
        {
            if (!SkillUtils.IsValidSkillName(name))
            {
                throw new ArgumentException($"Skill name '{name}' contains invalid characters. Only alphanumeric and hyphens are allowed.");
            }
            MetaData.Name = name;
            return this;
        }

        internal SkillDefinition WithDescription(string description)
        {
            MetaData.Description = description;
            return this;
        }

        internal SkillDefinition WithPath(string path)
        {
            Path = path;
            return this;
        }

        internal SkillDefinition WithContent(string content)
        {
            Content = content;
            return this;
        }
        
        internal SkillDefinition WithTag(string tag)
        {
            Tags.Add(tag);
            return this;
        }
        
        internal SkillDefinition WithRequiredPackage(string package, string version)
        {
            // Validate version string against Unity's package versioning standard (semver, e.g. 1.2.3, 1.2.3-preview.1)
            if (!SkillUtils.IsValidUnityPackageVersion(version))
            {
                throw new ArgumentException($"Package '{package}' has a version '{version}' that does not follow Unity's package versioning standards.");
            }
            MetaData.RequiredPackages.Add(package, version);
            return this;
        }

        internal SkillDefinition WithTool(string toolId)
        {
            if (!MetaData.Tools.Contains(toolId))
                MetaData.Tools.Add(toolId);
            return this;
        }
        
        internal SkillDefinition WithToolsFrom<T>()
        {
            var type = typeof(T);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var method in methods)
            {
                var toolAttr = method.GetCustomAttribute<AgentToolAttribute>();
                if (toolAttr == null)
                    continue;

                WithTool(toolAttr.Id);
            }

            return this;
        }
        
        internal SkillDefinition WithTool(MethodInfo methodInfo)
        {
            var toolAttr = methodInfo.GetCustomAttribute<AgentToolAttribute>();
            if (toolAttr == null)
            {
                throw new ArgumentException($"Method {methodInfo.Name} is not marked with [AgentTool]");
            }
            
            var toolId = toolAttr.Id;
            if (string.IsNullOrEmpty(toolId))
            {
                throw new ArgumentException($"The method with name {methodInfo.Name} has no valid [AgentTool] 'Id' property to register it as a tool. Make sure the function Id is properly defined.");
            }

            WithTool(toolId);
            return this;
        }

        internal SkillDefinition WithTool(FunctionDefinition functionDefinition)
        {
            var toolId = functionDefinition.FunctionId;
            if (string.IsNullOrEmpty(toolId))
            {
                throw new ArgumentException($"The functionDefinition with name {functionDefinition.Name} has no valid 'FunctionId' property to register it as a tool. Make sure the function Id is properly defined, e.g with an [AgentTool] attribute.");
            }

            WithTool(toolId);
            return this;
        }

        internal SkillDefinition WithTools(IEnumerable<FunctionDefinition> functionDefinitions)
        {
            foreach (var functionDefinition in functionDefinitions)
            {
                WithTool(functionDefinition);
            }
            return this;
        }

        /// <summary>
        /// Adds a resource to the skill.
        /// </summary>
        /// <param name="resourcePath">The resource's relative path</param>
        /// <param name="resource">The resource providing content, via an interface. Built-in implementations: <see cref="FileSkillResource"/> for file-based resources, <see cref="MemorySkillResource"/> for in-memory content.</param>
        /// <returns>This SkillDefinition for method chaining</returns>
        /// <seealso cref="WithResourceFile"/>
        /// <seealso cref="WithResourceContent"/>
        internal SkillDefinition WithResource(string resourcePath, ISkillResource resource)
        {
            // Normalize resourcePath to use forward slashes
            var normalizedResourcePath = resourcePath.Replace('\\', '/');
            Resources[normalizedResourcePath] = resource;

            if (!MetaData.AvailableResourcePaths.Contains(normalizedResourcePath))
                MetaData.AvailableResourcePaths.Add(normalizedResourcePath);

            return this;
        }
        
        internal SkillDefinition SetEnabled(bool enabled)
        {
            MetaData.Enabled = enabled;
            return this;
        }
        
        /// <summary>
        /// Creates a SkillDefinition by scanning a folder containing a SKILL.md file.
        /// Reads the file, parses YAML frontmatter, and populates all properties.
        /// </summary>
        /// <param name="skillFile">Absolute path to a SKILL.md file</param>
        /// <returns>A fully populated SkillDefinition</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when the skill file is invalid or cannot be parsed</exception>
        internal static SkillDefinition FromFolder(string skillFile)
        {
            return SkillUtils.FromFolder(skillFile);
        }
    }
}
