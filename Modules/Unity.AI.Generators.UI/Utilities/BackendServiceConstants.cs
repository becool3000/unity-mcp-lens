using System;
using System.Collections.Generic;
using AiEditorToolsSdk.Components.Common.Enums;

namespace Unity.AI.Generators.UI.Utilities
{
    class BackendServiceConstants
    {
        public static class ErrorTypes
        {
            public const string Unknown = "Unknown";
            public const string None = "None";
            public const string SdkValidationFailed = "SdkValidationFailed";
            public const string SdkTimeout = "SdkTimeout";
            public const string CanceledBySdkUser = "CanceledBySdkUser";
            public const string InvalidJobId = "InvalidJobId";
            public const string InsufficientFunds = "InsufficientFunds";
            public const string ServerValidationFailed = "ServerValidationFailed";
            public const string UnsupportedModelOperation = "UnsupportedModelOperation";
            public const string UnknownModel = "UnknownModel";
            public const string UserTooManyConcurrentGenerations = "UserTooManyConcurrentGenerations";
            public const string ServerTimeout = "ServerTimeout";
            public const string AiGeneratorIsDisabledForOrganization = "AiGeneratorIsDisabledForOrganization";
            public const string AiAssistantIsDisabledForOrganization = "AiAssistantIsDisabledForOrganization";
            public const string TermsOfServiceNotAccepted = "TermsOfServiceNotAccepted";
            public const string TokenInvalid = "TokenInvalid";
            public const string TokenExpired = "TokenExpired";
            public const string UserNotInOrganization = "UserNotInOrganization";
            public const string UserUnauthorized = "UserUnauthorized";
            public const string RateLimitExceeded = "RateLimitExceeded";
            public const string ApiNoLongerSupported = "ApiNoLongerSupported";
            public const string ModelParameterValidationFailed = "ModelParameterValidationFailed";
            public const string UnavailableForLegalReasons = "UnavailableForLegalReasons";

            public static IEnumerable<string> EnumerateAll()
            {
                yield return Unknown;
                yield return None;
                yield return SdkValidationFailed;
                yield return SdkTimeout;
                yield return CanceledBySdkUser;
                yield return InvalidJobId;
                yield return InsufficientFunds;
                yield return ServerValidationFailed;
                yield return UnsupportedModelOperation;
                yield return UnknownModel;
                yield return UserTooManyConcurrentGenerations;
                yield return ServerTimeout;
                yield return AiGeneratorIsDisabledForOrganization;
                yield return AiAssistantIsDisabledForOrganization;
                yield return TermsOfServiceNotAccepted;
                yield return TokenInvalid;
                yield return TokenExpired;
                yield return UserNotInOrganization;
                yield return UserUnauthorized;
                yield return RateLimitExceeded;
                yield return ApiNoLongerSupported;
                yield return ModelParameterValidationFailed;
                yield return UnavailableForLegalReasons;
            }
        }

        public static class JobStatus
        {
            public const string None = "None";
            public const string Waiting = "Waiting";
            public const string UserThrottled = "UserThrottled";
            public const string Working = "Working";
            public const string Done = "Done";
            public const string Failed = "Failed";

            public static IEnumerable<string> EnumerateAll()
            {
                yield return None;
                yield return Waiting;
                yield return UserThrottled;
                yield return Working;
                yield return Done;
                yield return Failed;
            }
        }

        public static AiResultErrorEnum ConvertToAiResultError(string error)
        {
            return Enum.TryParse<AiResultErrorEnum>(error, out var result) ? result : AiResultErrorEnum.Unknown;
        }

        public static JobStatusSdkEnum ConvertToJobStatus(string status)
        {
            return Enum.TryParse<JobStatusSdkEnum>(status, out var result) ? result : JobStatusSdkEnum.None;
        }
    }
}
