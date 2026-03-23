using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.FunctionCalling
{
    static partial class ToolValidator
    {
        struct ValidationMessage
        {
            public enum MessageType
            {
                Info,
                Warning,
                Error
            }

            public string ToolId;
            public string Message;
            public MessageType Type;
            public MethodInfo Method;
        }

        const string k_ToolIdPrefix = "Unity.";
        static  string[] s_AllowedStringKeywords = {
            "name",      // object/asset names
            "regex",     // regular expressions
            "query",     // search/filter queries
            "path",      // file, folder, or scene paths
            "prompt",    // textual prompts (e.g., for LLM)
            "text",      // general text input
            "title",     // UI or asset titles
            "description", // textual description fields
            "label",     // short labels or tags
            "message",   // logs or chat messages
            "pattern",   // string patterns
            "content",   // textual content
            "filename",  // file names
            "command",   // string commands
            "search",    // search strings
            "input",     // generic text input
            "tag",       // string tags
            "log",       // logs
            "language",  // language stuff
            "code",      // scripts and code
            "string",    // string
            "modelId",   // special case for model IDs (Generator)
        };

#if ASSISTANT_INTERNAL
        [MenuItem("AI Assistant/Internals/Tools/Check Definitions")]
        public static void ValidateAllTools()
        {
            ValidateAllAgentTools();
        }
#endif

        static void ValidateAllAgentTools()
        {
            var results = new List<ValidationMessage>();

            var toolMethods = TypeCache.GetMethodsWithAttribute<AgentToolAttribute>();
            foreach (var method in toolMethods)
            {
                var toolAttribute = method.GetCustomAttribute<AgentToolAttribute>();
                if (toolAttribute == null)
                    continue;

                ValidateTool(method, toolAttribute, results);
            }

            foreach (var result in results)
            {
                var methodLocation = result.Method.TryGetLocation(out var path, out var line)
                    ? $"({path.Replace("\\", "/")}:{line})"
                    : "";
                switch (result.Type)
                {
                    case ValidationMessage.MessageType.Info:
                        InternalLog.Log($"[Tool Validation] {methodLocation}{result.ToolId}: {result.Message}");
                        break;
                    case ValidationMessage.MessageType.Warning:
                        InternalLog.LogWarning($"[Tool Validation] {methodLocation}{result.ToolId}: {result.Message}");
                        break;
                    case ValidationMessage.MessageType.Error:
                        InternalLog.LogError($"[Tool Validation] {methodLocation}{result.ToolId}: {result.Message}");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        static void ValidateTool(MethodInfo method, AgentToolAttribute attr, List<ValidationMessage> results)
        {
            // Check method name is not just "Call"
            if (method.Name == "Call")
                Error("Tool method should have a proper name instead of 'Call'");
            
            // Check method is non-static
            if (!method.IsStatic)
                Error($"{nameof(AgentToolAttribute)} cannot be used on a non-static method.");
            
            // Check parent class is non-static
            if (method.DeclaringType != null && method.DeclaringType.IsAbstract && method.DeclaringType.IsSealed)
                Warning("Try to group similar tools into a single non-static class.");
            
            // Check that permissions are not ignored for the whole tool
            if (method.GetCustomAttribute<ToolPermissionIgnoreAttribute>() != null)
                Error($"{nameof(ToolPermissionIgnoreAttribute)} cannot be used together with {nameof(AgentToolAttribute)}. Only ignore permission checks in small-scope utility methods.");

            // Check tool ID
            if (!IsValidToolId(attr.Id, out var idError))
                Error(idError);

            // Description validation
            if (attr.Description != null)
            {
                // Check we do not mention other tools
                if (attr.Description.Contains(k_ToolIdPrefix))
                    Warning("Description should not reference other tools.");

                // Check that we do not mention parameter names
                // Only check parameters made of several segments (at least one upper case letter)
                var paramsWithUpperCase = method.GetParameters()
                    .Where(p => p.Name.Any(char.IsUpper));
                var mentionedParams = paramsWithUpperCase
                    .Where(p => attr.Description != null && attr.Description.Contains(p.Name))
                    .Select(p => p.Name)
                    .ToList();
                if (mentionedParams.Count > 0)
                    Error($"Description should not mention parameter names: {string.Join(", ", mentionedParams)}");
            }

            // Parameter validation
            foreach (var p in method.GetParameters())
            {
                // Special case for context parameter
                if (p.ParameterType == typeof(ToolExecutionContext))
                {
                    // Must be async
                    var isAsync = method.GetCustomAttribute<AsyncStateMachineAttribute>() != null;
                    if (!isAsync)
                        Error($"Method must be async when using {nameof(ToolExecutionContext)}");
                    continue;
                }

                // Check that we have a description
                var parameterAttr = p.GetCustomAttribute<ParameterAttribute>();
                if (parameterAttr == null)
                    Error($"Parameter '{p.Name}' of method '{method.Name}' is missing [Parameter] attribute.");

                // Check that we use typed parameters
                if (p.ParameterType == typeof(string))
                {
                    // filter to exclude some typical string parameters
                    var containsAllowedKeyword = s_AllowedStringKeywords.Any(k => p.Name.Contains(k, StringComparison.OrdinalIgnoreCase));
                    if (!containsAllowedKeyword)
                        Warning($"Parameter '{p.Name}' is string; ensure it cannot be replaced by a proper type, like an enum, or if it should be given a better name.");
                }

                // Detect multiple bools that may be mutually exclusive
                if (p.ParameterType == typeof(bool))
                {
                    var boolParameters = method.GetParameters().Where(x => x.ParameterType == typeof(bool)).Select(x => x.Name).ToList();
                    if (boolParameters.Count > 2)
                        Warning($"Multiple boolean parameters detected ({string.Join(", ", boolParameters)}); consider using a single enum to avoid mutually exclusive flags if applicable.");
                }
            }

            // Check that there is no success / error flags
            var hasSuccessProp = method.ReturnType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(p => string.Equals(p.Name, "Success", StringComparison.OrdinalIgnoreCase));
            var hasErrorProp = method.ReturnType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(p => string.Equals(p.Name, "Error", StringComparison.OrdinalIgnoreCase));
            if (hasSuccessProp || hasErrorProp)
            {
                Error($"Return type '{method.ReturnType.Name}' contains disallowed properties '{(hasSuccessProp ? "Success " : "")}{(hasErrorProp ? "Error" : "")}'. " +
                    "Tools should not return explicit success/error properties, throw an exception instead.");
            }

            // Check that the tool has an implementation
            var bodyEmpty = method.GetMethodBody()?.GetILAsByteArray()?.Length < 5;
            if (bodyEmpty)
                Warning("Method appears empty or unimplemented.");

            return;

            void Error(string msg) => results.Add(new ValidationMessage { Method = method, ToolId = attr.Id, Message = msg, Type = ValidationMessage.MessageType.Error });
            void Warning(string msg) => results.Add(new ValidationMessage { Method = method, ToolId = attr.Id, Message = msg, Type = ValidationMessage.MessageType.Warning });
        }

        static bool IsValidToolId(string id, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = "Tool ID cannot be empty.";
                return false;
            }

            if (!id.StartsWith(k_ToolIdPrefix))
            {
                error = $"Tool ID must start with '{k_ToolIdPrefix}'";
                return false;
            }

            // Must contain only letters, digits, and dots
            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                if (!(char.IsLetterOrDigit(c) || c == '.'))
                {
                    error = $"Tool ID contains invalid character '{c}'. Only alphanumeric characters and dots are allowed.";
                    return false;
                }
            }

            // Split by dot and validate each segment
            var segments = id.Split('.');

            // Must have at least 2 segments: e.g. "Unity.X"
            if (segments.Length < 3)
            {
                error = "Tool ID must contain at least three dot-separated segments, with the first being 'Unity' and the second the tool domain, for instance 'Unity.GameObject.Create'";
                return false;
            }

            foreach (var seg in segments)
            {
                if (string.IsNullOrWhiteSpace(seg))
                {
                    error = "Tool ID contains an empty segment (two consecutive dots).";
                    return false;
                }

                // First character of each segment must be uppercase A–Z
                if (!char.IsUpper(seg[0]))
                {
                    error = $"Segment '{seg}' must start with an uppercase letter.";
                    return false;
                }
            }

            return true;
        }
    }
}
