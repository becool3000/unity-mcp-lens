using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;

namespace Unity.AI.Assistant.Data
{
    [Serializable]
    struct AssistantFunctionCall : IEquatable<AssistantFunctionCall>
    {
        public string FunctionId;
        public Guid CallId;
        public JObject Parameters;
        public IFunctionCaller.CallResult Result;

        public override int GetHashCode() => HashCode.Combine(FunctionId, CallId, Parameters, Result);
        public override bool Equals(object obj) => obj is AssistantFunctionCall other && Equals(other);
        public bool Equals(AssistantFunctionCall other)
        {
            return FunctionId == other.FunctionId && CallId.Equals(other.CallId) && Equals(Parameters, other.Parameters) && Result.Equals(other.Result);
        }
        
        internal void GetCodeEditParameters(out string filePath, out string newCode, out string oldCode)
        {
            filePath = Parameters?["filePath"]?.ToString();
            newCode = Parameters?["newString"]?.ToString();
            oldCode = Parameters?["oldString"]?.ToString();
        }
    }
}

