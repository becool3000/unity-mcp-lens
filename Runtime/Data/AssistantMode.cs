using System;

namespace Unity.AI.Assistant.Data
{
    /// <summary>
    /// Represents the Assistant mode
    /// </summary>
    [Flags]
    public enum AssistantMode
    {
        /// <summary> Undefined mode. </summary>
        Undefined = 0,

        /// <summary> Agent mode, i.e. can perform actions like modifying object or writing data. </summary>
        Agent = 1 << 0,

        /// <summary> Ask mode, i.e. cannot perform actions and use only read-only tools. </summary>
        Ask = 1 << 1,

        /// <summary> All modes </summary>
        Any = Agent | Ask
    }
}
