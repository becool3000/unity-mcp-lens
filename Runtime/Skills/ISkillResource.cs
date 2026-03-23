namespace Unity.AI.Assistant.Skills
{
    /// <summary>
    /// Interface for skill resources that can provide their content on-demand.
    /// </summary>
    interface ISkillResource
    {
        /// <summary>
        /// Gets the content of the resource.
        /// </summary>
        /// <returns>The resource content as a string</returns>
        string GetContent();
    }
}
