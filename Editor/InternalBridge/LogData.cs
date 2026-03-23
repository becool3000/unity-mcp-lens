using System;
using UnityEngine;

namespace Unity.AI.Assistant.Bridge.Editor
{
    /// <summary>
    /// Stores relevant data from console logs
    /// </summary>
    [Serializable]
    internal struct LogData : IEquatable<LogData>
    {
        [SerializeField]
        public string Message;

        [SerializeField]
        public string File;

        [SerializeField]
        public int Line;

        [SerializeField]
        public int Column;

        [SerializeField]
        public LogDataType Type;

        [SerializeField]
        // Not part of comparison, just for UI to see Message with timestamp
        public string MessageWithTimestamp;

        [SerializeField]
        // Not part of comparison, just for UI to differentiate origin of log
        public bool IsSelectedInConsole;

        public bool Equals(LogData other)
        {
            return Message == other.Message && File == other.File && Line == other.Line && Column == other.Column && Type == other.Type;
        }

        public override bool Equals(object obj)
        {
            return obj is LogData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Message, File, Line, Column, (int)Type);
        }
    }
}
