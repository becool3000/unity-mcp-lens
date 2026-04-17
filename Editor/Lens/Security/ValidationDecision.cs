using Becool.UnityMcpLens.Editor.Models;

namespace Becool.UnityMcpLens.Editor.Security
{
    /// <summary>
    /// Represents the outcome of a validation decision.
    /// Separates validation status from connection information.
    /// </summary>
    enum ValidationStatus
    {
        /// <summary>
        /// Connection attempt is pending user approval
        /// </summary>
        Pending,

        /// <summary>
        /// Server validated successfully and connection is accepted
        /// </summary>
        Accepted,

        /// <summary>
        /// Server validation failed and connection is rejected (Strict mode)
        /// </summary>
        Rejected,

        /// <summary>
        /// Server validation failed but connection is allowed with warning (LogOnly mode)
        /// </summary>
        Warning,

        /// <summary>
        /// Connection denied because the maximum number of direct connections has been reached
        /// </summary>
        CapacityLimit
    }

    /// <summary>
    /// Result of validating a connection attempt.
    /// Contains both the validation decision and the connection information.
    /// </summary>
    class ValidationDecision
    {
        public ValidationStatus Status { get; set; }
        public string Reason { get; set; }              // Why this decision was made
        public ConnectionInfo Connection { get; set; }   // The connection being validated

        /// <summary>
        /// Whether the connection should be accepted (Accepted, Warning, or Pending for user approval)
        /// </summary>
        public bool IsAccepted => Status == ValidationStatus.Accepted || Status == ValidationStatus.Warning || Status == ValidationStatus.Pending;

        public override string ToString()
        {
            return $"{Status}: {Reason} ({Connection?.DisplayName ?? "unknown"})";
        }
    }
}
