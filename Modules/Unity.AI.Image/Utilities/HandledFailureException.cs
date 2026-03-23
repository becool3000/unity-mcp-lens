using System;
using AiEditorToolsSdk.Components.Common.Enums;

namespace Unity.AI.Image.Services.Utilities
{
    class HandledFailureException : Exception { }

    class DownloadTimeoutException : Exception { }

    class UnhandledReferenceCombinationException : Exception
    {
        public AiResultErrorEnum responseError { get; } = AiResultErrorEnum.UnsupportedModelOperation;

        public UnhandledReferenceCombinationException() : base("More than 2 references or an unhandled combination provided.") { }
    }
}
