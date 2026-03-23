using System;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    class FunctionCallBlockModel : IMessageBlockModel, IEquatable<FunctionCallBlockModel>
    {
        public AssistantFunctionCall Call;

        public bool Equals(FunctionCallBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Call, other.Call);
        }

        public override bool Equals(object obj) => obj is FunctionCallBlockModel other && Equals(other);
        public override int GetHashCode() => Call.GetHashCode();
    }
}