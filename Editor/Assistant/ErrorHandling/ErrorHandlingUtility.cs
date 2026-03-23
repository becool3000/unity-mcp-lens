using System.Collections.Generic;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Helper class to convert error codes and error texts to more user friendly error messages.
    /// </summary>
    static class ErrorHandlingUtility
    {
        public const string ErrorTitle = "An error has occurred in AI Assistant";
        public const string ErrorMessagePrefix = ErrorTitle + ". ";
        public const string ErrorMessageNetworkedSuffix
            = "Please try again. If the issue persists check your network connection or file a bug using Help " +
              "> Report a Bug...";
        public const string ErrorMessageNotNetworkedSuffix
            = "Please try again. If the issue persists file a bug using Help > Report a Bug...";

        const string k_SupportFormLink = "feel free to <a href=\"https://support.unity.com/hc/en-us/requests/new?ticket_form_id=65905\">contact our support team</a>";

        // The same error code can have multiple causes, the error text gives more details.
        // Nested dictionary to look up errors by code and error text.
        static Dictionary<int, Dictionary<string, string>> errorMessageLookup = new() {
            {
                // 0 for errors that are identified by error text only and can have any error code:
                0, new Dictionary<string, string> {
                    { "GENESIS-UNEXPECTED-FAILURE", "Request failed, please try again." },
                    { "CHAT-UNEXPECTED-FAILURE", "Request failed, please try again." },
                }
            }, {
                // 1 for errors that are not ApiExceptions:
                1, new Dictionary<string, string> {
                    { "", "Looks like you are not connected to the internet." },
                }
            }, {
                400, new Dictionary<string, string> {
                    { "NO-ORGANIZATION", $"Your request could not be processed due to a missing organization ID. This could be due to a bug in our system. Try re-selecting your organization in the project settings for Muse. If the issue persists, {k_SupportFormLink} with details about the problem. We appreciate your patience as we work to ensure a smooth experience." },
                    { "INAPPROPRIATE", "It seems that your request contains inappropriate content, and our system cannot respond to such queries. Please ensure that your input adheres to our guidelines for respectful and appropriate interactions. If you have a different question or need assistance, feel free to provide a more suitable prompt. Thank you for your understanding and cooperation." }
                }
            }, {
                401, new Dictionary<string, string> {
                    { "NOT-AUTHENTICATED", $"Something unexpected happened on our end. Please try again. If the issue persists, {k_SupportFormLink} for assistance. We apologize for any inconvenience." },
                    { "ORGANIZATION-NOT-FOUND", $"We encountered an issue processing your request and cannot locate the selected organization in our records. This could be due to a bug on our end. Please ensure you've selected a valid organization with Muse access in the project settings for Muse. If the problem persists, {k_SupportFormLink} for assistance. We apologize for any inconvenience and appreciate your understanding as we work to resolve this." },
                    { "TRY-REFRESH-TOKEN", "Trying to refresh access token, please try again. We apologize for any inconvenience." },
                }
            }, {
                403, new Dictionary<string, string> {
                    { "NOT-ENTITLED", "The organization you've selected doesn't have access to Muse. Please select the organization in the project settings for Muse. We appreciate your cooperation." },
                    { "Forbidden", $"The organization you've selected doesn't have access to Muse. Please select the organization in the project settings for Muse. We appreciate your cooperation." }
                }
            }, {
                404, new Dictionary<string, string> {
                    { "SUBSCRIPTION-NOT-FOUND", "Subscription not found." }
                }
            }, {
                492, new Dictionary<string, string> {
                    {
                        "RATE-LIMIT-REACHED", $"We've noticed that you've made too many requests in a short period, and our system requires a brief moment to catch up. To ensure fair usage for all users, we have a rate limit in place. Please wait for a moment before attempting your request again. If the issue persists, consider adjusting the frequency of your requests. For further assistance, {k_SupportFormLink}. Thank you for your cooperation."
                    }
                }
            }, {
                500, new Dictionary<string, string> {
                    {
                        "", $"Something unexpected happened on our end. Please try again. If the issue persists, {k_SupportFormLink} for assistance. We apologize for any inconvenience."
                    }
                }
            }, {
                503, new Dictionary<string, string> {
                    {
                        "", "Looks like you are not connected to the internet."
                    },
                }
            }
        };

        public static string GetErrorTitle() => ErrorTitle;

        public static string GetErrorMessageFromHttpResult(int errorCode, string errorText, string messageContent, bool addErrorPrefix = true)
        {
            string errorMessage;

            errorText ??= string.Empty;

            // Try to find the error code in our lookup:
            if (errorMessageLookup.TryGetValue(errorCode, out var lookup))
            {
                errorMessage = GetErrorMessageFromErrorText(lookup);

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    return errorMessage;
                }
            }

            // Error code not found, check for error text in code 0:
            errorMessage = GetErrorMessageFromErrorText(errorMessageLookup[0]);

            if (!string.IsNullOrEmpty(errorMessage))
            {
                return errorMessage;
            }

            // Fallback for errors we didn't implement:
            return $"{ErrorMessagePrefix}{messageContent} {ErrorMessageNetworkedSuffix}";

            string GetErrorMessageFromErrorText(Dictionary<string, string> lookup)
            {
                // Find an error text that is contained in the error text we received.
                // We can't use errorText to look up directly because it may contain prefixes like "HTTP/1.1"
                foreach (var errorCodeLookUp in lookup.Keys)
                {
                    if (errorText.Contains(errorCodeLookUp))
                    {
                        var userMessage = lookup[errorCodeLookUp];
                        {
                            var result = (addErrorPrefix ? ErrorMessagePrefix : "") + userMessage;

                            // Special case for error code 1: Add the text because it could have more details:
                            if (errorCode == 1)
                            {
                                result += $". Details: {errorText}";
                            }

                            return result;
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Log a BackendResult to the console as an error.
        /// </summary>
        /// <param name="result">The result. This will be logged internally with more info</param>
        public static void PublicLogBackendResultError(BackendResult result)
        {
            Debug.LogError(result.Info.PublicMessage);
            InternalLogBackendResult(result);
        }

        public static void InternalLogBackendResult(BackendResult result)
        {
            if(result.Status == BackendResult.ResultStatus.Success)
                InternalLog.Log(PrepareInternalString(result.ToString()));
            else
                InternalLog.LogError(PrepareInternalString(result.ToString()));
        }

        /// <summary>
        /// Log a BackendResult to the console as an error.
        /// </summary>
        public static void PublicLogError(ErrorInfo info)
        {
            Debug.LogError($"{ErrorMessagePrefix} {info.PublicMessage}");
            InternalLogError(info);
        }

        public static void InternalLogError(ErrorInfo info)
        {
            InternalLog.LogError(PrepareInternalString($"{info.InternalMessage}\n\nPublic Message:\n{info.PublicMessage}"));
        }

        static string PrepareInternalString(string str) => $"[internal] {str}";
    }
}
