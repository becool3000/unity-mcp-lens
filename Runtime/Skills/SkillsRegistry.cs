using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// Registry for local skills (SkillDefinition) to be sent to the backend.
    /// </summary>
    static class SkillsRegistry
    {
        static Dictionary<string, SkillDefinition> s_RegisteredSkills = new();

        public static Dictionary<string, SkillDefinition> GetSkills() => s_RegisteredSkills;

        public static void RegisterSkill(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrEmpty(skill.MetaData.Name))
                return;

            if (s_RegisteredSkills.Any(x => x.Key == skill.MetaData.Name))
            {
                InternalLog.LogWarning($"[SkillsRegistry] A skill with name '{skill.MetaData.Name}` was skipped, a SkillDefinition with same name was already registered.");
                return;
            }
            
            s_RegisteredSkills[skill.MetaData.Name] = skill;
        }

        /// <summary>
        /// Remove skills by tag, allowing to select skills from the same source that added them.
        /// </summary>
        public static void RemoveByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            var skillsToRemove = s_RegisteredSkills.Where(x => x.Value.Tags.Contains(tag)).Select(x => x.Key).ToList();
            foreach (var key in skillsToRemove)
            {
                s_RegisteredSkills.Remove(key);
            }
        }

        /// <summary>
        /// Remove all skills.
        /// </summary>
        public static void Clear()
        {
            s_RegisteredSkills.Clear();
        }

        public static void AddSkills(List<SkillDefinition> skills)
        {
            if (skills?.Count > 0)
            {
                foreach (var skill in skills)
                {
                    if (skill != null && skill.IsValid)
                    {
                        RegisterSkill(skill);
                    }                    
                    else
                    {
                        InternalLog.LogWarning("[SkillsRegistry] Skipped NULL, unnamed, or otherwise invalid skill when adding skills to registry. Look at any previous logs for failed SkillDefinition building steps.");
                    }
                }
                InternalLog.Log($"[SkillsRegistry] Updated with {skills.Count} skills");
            }
        }
    }
}
