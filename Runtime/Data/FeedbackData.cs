using System;
using Unity.AI.Assistant.ApplicationModels;

namespace Unity.AI.Assistant.Data
{
    struct FeedbackData : IEquatable<FeedbackData>
    {
        public FeedbackData(Sentiment sentiment, string details)
        {
            Sentiment = sentiment;
            Details = details;
        }

        public readonly Sentiment Sentiment;
        public readonly string Details;

        public bool Equals(FeedbackData other)
        {
            return Sentiment == other.Sentiment &&
                   string.Equals(Details, other.Details);
        }

        public override bool Equals(object obj)
        {
            return obj is FeedbackData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Sentiment, Details?.ToLowerInvariant());
        }
    }
}
