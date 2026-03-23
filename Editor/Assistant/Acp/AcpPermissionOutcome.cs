using Newtonsoft.Json;
using Unity.Relay.Editor.Acp;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Permission response outcome.
    /// </summary>
    class AcpPermissionOutcome
    {
        /// <summary>
        /// Outcome type: "selected" or "cancelled"
        /// </summary>
        [JsonProperty("outcome")]
        public string Outcome { get; set; }

        /// <summary>
        /// The selected option ID (only for "selected" outcome)
        /// </summary>
        [JsonProperty("optionId")]
        public string OptionId { get; set; }

        public static AcpPermissionOutcome Selected(string optionId) =>
            new() { Outcome = AcpConstants.Outcome_Selected, OptionId = optionId };

        public static AcpPermissionOutcome Cancelled() =>
            new() { Outcome = AcpConstants.Outcome_Cancelled };
    }
}
