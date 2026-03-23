using System.Collections.Generic;
using Unity.AI.Assistant.Skills;

namespace Unity.AI.Assistant.Integrations.Sample.Editor
{
    class SampleSkill
    {
        public static void InitializeSkill()
        {
            // Create a new skill definition with all mandatory fields
            var testSkill = new SkillDefinition()
                .WithName("test-skill-with-weather")
                .WithDescription("A test skill to gather some location and weather info, suggesting activities.")
                .WithTag("Skills.TestTag") // Allows also to remove this again, via SkillsRegistry.RemoveSkillsByTag("Skills.TestTag")
                .WithContent(@"
                    You are a friendly personal assistant. Use your tools to:
                    1. Suggest activities based on the weather
                    2. Categorize at least into leisure, sports, cultural, and food.

                    Some general information is stored here as a resource: `resources/activity_ideas.md`                  
                ")

                // Optional data, here we use a resource 
                .WithResource("resources/activity_ideas.md", new MemorySkillResource(@"## Things To DO depending on weather:
                    In Montreal, Canada, warm weather activities include: hiking, visiting a park, outdoor sports, having a picnic, going to a terrace, etc.
                    In Paris, France, general activities include: visiting the Louvre, going to a restaurant.
                    In New York, USA, warm weather activities include: a Manhattan tour, outdoor sports, going to a rooftop location for nice photos, etc. Cold weather activities include: visiting a museum, going to a restaurant, going to the movies, etc.
                    In London, UK, warm weather activities include: going to a terrace, going to outdoor markets including Camden Lock, etc. Cold weather activities include: visiting a museum (many are free), going to a pub, etc.
                "))
                // ...and a set of tools (a set of methods with [AgentTool] attribute in class `SampleTools`)
                .WithToolsFrom<SampleTools>();

            // From here on the skill is available to the backend, to be tested and used
            SkillsRegistry.AddSkills(new List<SkillDefinition> { testSkill });
        }

        public static void RemoveSkill()
        {
            SkillsRegistry.RemoveByTag("Skills.TestTag");
        }
    }
}
