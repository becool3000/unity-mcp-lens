using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Interface for rendering function call UI elements in the assistant chat.
    /// Implementers handle the visual representation and state updates of function calls.
    /// </summary>
    interface IFunctionCallRenderer
    {
        /// <summary>
        /// The main title displayed for the function call element.
        /// </summary>
        string Title { get; }

        /// <summary>
        /// Additional details displayed alongside the title.
        /// </summary>
        string TitleDetails { get; }
        
        /// <summary>
        /// If true, the content section is expanded
        /// </summary>
        bool Expanded { get; }

        /// <summary>
        /// Called when a function call is initially requested.
        /// Use this to set up the initial UI state for the pending call.
        /// </summary>
        /// <param name="functionCall">The function call request containing call details.</param>
        void OnCallRequest(AssistantFunctionCall functionCall);

        /// <summary>
        /// Called when the function call completes successfully.
        /// </summary>
        /// <param name="functionId">The identifier of the function that was called.</param>
        /// <param name="callId">The unique identifier for this specific call instance.</param>
        /// <param name="result">The result returned by the function call.</param>
        /// <returns>True if the content section should auto-expand, false otherwise.</returns>
        void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result);

        /// <summary>
        /// Called when the function call fails with an error.
        /// </summary>
        /// <param name="functionId">The identifier of the function that was called.</param>
        /// <param name="callId">The unique identifier for this specific call instance.</param>
        /// <param name="error">The error message describing the failure.</param>
        void OnCallError(string functionId, Guid callId, string error);
    }
}
