using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Skills;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class SkillsTools
    {
        // Tool IDs are directly used by backend, do not change without backend update.
        const string k_GetSkillBodyID = "Unity.Skill.ReadSkillBody";
        const string k_GetResourceContentID = "Unity.Skill.ReadSkillResource";
        
        [Serializable]
        public class GetResourceOutput
        {
            public string ResourcePath = string.Empty;
            public string ResourceContent = string.Empty;
        }
        
        [AgentTool("Returns a skill's body, frontmatter and markdown",
            k_GetSkillBodyID,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag
        )]
        public static Task<string> ReadSkillBody(
            ToolExecutionContext context,
            [Parameter("The skill's name as defined in metadata")]
            string skill_name
        )
        {
            if (string.IsNullOrEmpty(skill_name))
                throw new ArgumentException("Skill name cannot be empty.");

            var skills = SkillsRegistry.GetSkills();

            if (!skills.TryGetValue(skill_name, out var skill))
            {
                throw new ArgumentException($"Skill name {skill_name} doesn't exist in local skills.");
            }

            return Task.FromResult(skill.Content);
        }
        
        [AgentTool("Returns one skill's content for a given resource.",
            k_GetResourceContentID,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag
        )]
        public static Task<GetResourceOutput> GetSkillResourceContent(
            ToolExecutionContext context,
            [Parameter("The skill's name as defined in metadata")]
            string skill_name,
            [Parameter("The resource path to retrieve content from")]
            string resource_path
        )
        {
            if (string.IsNullOrEmpty(skill_name))
                throw new ArgumentException("Skill name cannot be empty.");
            if (string.IsNullOrEmpty(resource_path))
                throw new ArgumentException("Resource path cannot be empty.");

            var skills = SkillsRegistry.GetSkills();

            if (!skills.TryGetValue(skill_name, out var skill))
            {
                throw new ArgumentException($"Skill name {skill_name} doesn't exist in local skills.");
            }

            if (!skill.Resources.TryGetValue(resource_path, out var resource))
            {
                throw new ArgumentException($"Resource path {resource_path} doesn't exist in skill {skill_name}.");
            }
            
            var output = new GetResourceOutput();

            output.ResourcePath = resource_path;

            try
            {
                output.ResourceContent = resource.GetContent();
            }
            catch (System.IO.IOException ex)
            {
                throw new InvalidOperationException($"Failed to load resource '{resource_path}' from skill '{skill_name}': {ex.Message}", ex);
            }

            return Task.FromResult(output);
        }
    }
}
