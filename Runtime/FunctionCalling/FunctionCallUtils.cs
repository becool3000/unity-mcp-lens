using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.FunctionCalling
{
    static class FunctionCallUtils
    {
        public static string GetDefaultTitle(this AssistantFunctionCall functionCall)
        {
            // FindSceneObjects → "Find Scene Objects". "GetXMLData" → "Get XML Data".
            var functionCallName = functionCall.FunctionId.Split('.').Last();
            return Regex.Replace(functionCallName, @"(?<!^)(?=[A-Z][a-z])|(?<=[a-z])(?=[A-Z])", " ");
        }
        
        public static string GetDefaultTitleDetails(this AssistantFunctionCall functionCall, string parameterName, Func<JToken, string> function)
        {
            using var handle = DictionaryPool<string, Func<JToken, string>>.Get(out var dict);
            dict.Add(parameterName, function);
            return functionCall.GetDefaultTitleDetails(dict);
        }
        
        public static string GetDefaultTitleDetails(this AssistantFunctionCall functionCall, Dictionary<string, Func<JToken, string>>  parametersFormatting = null)
        {
            var parameters = functionCall.Parameters;
            // Convert the parameter to a compact one line string
            var sb = new StringBuilder();
            foreach (var kv in parameters)
            {
                var valueString = parametersFormatting != null && parametersFormatting.ContainsKey(kv.Key) ?
                    parametersFormatting[kv.Key](kv.Value) :
                    kv.Value?.ConvertToString()?.Trim();

                // Only add the parameter if it is not empty and tt must also not contain a new line
                // to avoid breaking the layout. We prefer not showing it at all in that case.
                if (!string.IsNullOrEmpty(valueString))
                {
                    if (sb.Length > 0)
                        sb.Append(", ");

                    valueString = GetFirstLine(valueString);

                    sb.Append($"{kv.Key}: {valueString}");
                }
            }

            // Add parenthesis if there is at least one parameter
            if (sb.Length > 0)
                sb.Insert(0, "(").Append(")");

            return sb.ToString();
        }

        public static T GetTypedResult<T>(this IFunctionCaller.CallResult result) => AssistantJsonHelper.ToObject<T>(result.Result);

        public static VisualElement CreateContentLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
                text = "<empty result>";
            else
                text = text.TrimEnd();

            var textField = new TextField { value = text, isReadOnly = true };
            textField.AddToClassList("mui-function-call-text-field");
            return textField;
        }

        public static VisualElement CreateDefaultContentLabel(this IFunctionCaller.CallResult result)
        {
            string typedResult = null;
            try
            {
                typedResult = result.GetTypedResult<string>()?.Trim();
            }
            catch (Exception)
            {
                typedResult = result.Result?.ToString();
            }

            return CreateContentLabel(typedResult);
        }

        public static string ConvertToString(this JToken value)
        {
            if (value.Type == JTokenType.Object || value.Type == JTokenType.Array)
                return value.ToString(Newtonsoft.Json.Formatting.None);

            return value.Value<string>();
        }

        static string GetFirstLine(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            int newlineIndex = input.IndexOfAny(new[] { '\r', '\n' });
            return newlineIndex >= 0 ? input.Substring(0, newlineIndex) + " ..." : input;
        }
    }
}

