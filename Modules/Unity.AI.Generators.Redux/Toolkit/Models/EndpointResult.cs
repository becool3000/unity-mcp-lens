using System;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    /// <summary>
    /// A standardized result record for endpoint calls.
    /// </summary>
    [Serializable]
    record EndpointResult
    {
        /// <summary>
        /// When true, indicates that the query is currently loading for the first time,
        /// and has no data yet. This will be true for the first request fired off, but not for subsequent requests.
        /// </summary>
        public bool isLoading;

        /// <summary>
        /// When true, indicates that the query is in an error state.
        /// </summary>
        public bool isError;

        /// <summary>
        /// When true, indicates that the query has data from a successful request.
        /// </summary>
        public bool isSuccess;

        /// <summary>
        /// When true, indicates that the query has not started yet.
        /// </summary>
        public bool isUninitialized = true;

        /// <summary>
        /// The latest returned result.
        /// </summary>
        [SerializeReference]
        public object payloadObj;

        /// <summary>
        /// The error result if present.
        /// </summary>
        public ReduxException error;

        /// <summary>
        /// The current progress.
        /// </summary>
        public ApiProgress progress;

        /// <summary>
        /// Indicates whether the operation is done, either successfully or with an error.
        /// </summary>
        public bool isDone => isSuccess || isError;
    }

    [Serializable]
    record EndpointResult<TResult> : EndpointResult
    {
        public TResult payload => (TResult)payloadObj;

        public EndpointResult() { }
        public EndpointResult(EndpointResult existing)
        {
            isLoading = existing.isLoading;
            isError = existing.isError;
            isSuccess = existing.isSuccess;
            isUninitialized = existing.isUninitialized;
            payloadObj = existing.payloadObj;
            error = existing.error;
            progress = existing.progress;
        }
    }

    /// <summary>
    /// Endpoint result that is subscribed to state changes and provides a way to unsubscribe.
    /// </summary>
    record UseEndpointResult<TResult> : EndpointResult<TResult>
    {
        public Unsubscribe unsubscribe;
        public UseEndpointResult(EndpointResult<TResult> existing, Unsubscribe unsubscribe) : base(existing)
        {
            this.unsubscribe = () =>
            {
                var result = unsubscribe();
                unsubscribe = () => false;  // Ensure multiple calls don't over-unsubscribe as it's reference-counted.
                return result;
            };
        }
    }
}
