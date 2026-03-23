using System;

namespace Unity.AI.Assistant.Socket.ErrorHandling
{
    class BackendResult
    {
        public enum ResultStatus
        {
            /// <summary>
            /// The operation was a success
            /// </summary>
            Success,

            /// <summary>
            /// The operation failed because of the servers response
            /// </summary>
            FailOnServerResponse,

            /// <summary>
            /// The operation failed because an exception was thrown in the code
            /// </summary>
            FailOnException,

            /// <summary>
            /// The operation failed because it was cancelled
            /// </summary>
            FailOnCancellation
        }

        /// <summary>
        /// Did the communication with the backend succeed
        /// </summary>
        public ResultStatus Status;

        /// <summary>
        /// The explanation of what went wrong that can be used by the frontend to display an error message
        /// </summary>
        public ErrorInfo Info;

        /// <summary>
        /// The exception (or null if no exception was reported) related to the failure.
        /// </summary>
        public Exception Exception;

        public static BackendResult FailOnException(string info, Exception exception) => new ()
        {
            Status = ResultStatus.FailOnException,
            Exception = exception,
            Info = new ErrorInfo(info, "See Exception")
        };

        public static BackendResult FailOnCancellation() => new ()
        {
            Status = ResultStatus.FailOnCancellation,
        };

        public static BackendResult FailOnServerResponse(ErrorInfo info) => new ()
        {
            Status = ResultStatus.FailOnServerResponse,
            Info = info
        };

        public static BackendResult Success() => new ()
        {
            Status = ResultStatus.Success
        };

        public override string ToString()
        {
            switch (Status)
            {
                case ResultStatus.Success:
                    return $"BackendResult [Status: {Status}";
                case ResultStatus.FailOnException:
                    return $"BackendResult [Status: {Status}, Info: {Info.InternalMessage}, Exception: {Exception}\nPublic Message: {Info.PublicMessage}";
                case ResultStatus.FailOnCancellation:
                case ResultStatus.FailOnServerResponse:
                    return $"BackendResult [Status: {Status}, Info: {Info.InternalMessage}\nPublic Message: {Info.PublicMessage}";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Represents the result of interacting with a backend and attempting to retrieve data of type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class BackendResult<T> : BackendResult
    {
        /// <summary>
        /// The data retrieved from a successful backend call
        /// </summary>
        public T Value;

        public new static BackendResult<T> FailOnException(string info, Exception exception) => new ()
        {
            Status = ResultStatus.FailOnException,
            Exception = exception,
            Info = new ErrorInfo(info, "See Exception")
        };

        public new static BackendResult<T> FailOnCancellation() => new ()
        {
            Status = ResultStatus.FailOnCancellation,
        };

        public new static BackendResult<T> FailOnServerResponse(ErrorInfo info) => new ()
        {
            Status = ResultStatus.FailOnServerResponse,
            Info = info
        };

        public static BackendResult<T> Success(T result) => new ()
        {
            Status = ResultStatus.Success,
            Value = result
        };

        public override string ToString()
        {
            switch (Status)
            {
                case ResultStatus.Success:
                    return $"BackendResult [Status: {Status}, Value: {Value}";
                case ResultStatus.FailOnException:
                    return $"BackendResult [Status: {Status}, Info: {Info.InternalMessage}, Exception: {Exception}\nPublic Message: {Info.PublicMessage}";
                case ResultStatus.FailOnCancellation:
                case ResultStatus.FailOnServerResponse:
                    return $"BackendResult [Status: {Status}, Info: {Info.InternalMessage}\nPublic Message: {Info.PublicMessage}";
            }

            return string.Empty;
        }
    }
}
