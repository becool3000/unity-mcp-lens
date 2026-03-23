namespace Unity.AI.Generators.UI.Utilities
{
    static class PromptUtilities
    {
        public const int maxPromptLength = 1024;

        public static string TruncatePrompt(string prompt)
        {
            if (string.IsNullOrEmpty(prompt))
                return string.Empty;
            return prompt.Length <= maxPromptLength ? prompt : prompt[..maxPromptLength];
        }
    }
}
