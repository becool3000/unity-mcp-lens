using System;
using System.IO;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Context
{
    /// <summary>
    /// Allows a console log message and  associated file to be sent to the LLM for evaluation
    /// </summary>
    class ConsoleContextSelection : IContextSelection
    {
        LogData? m_Target;

        public LogData? Target => m_Target;

        internal void SetTarget(LogData target)
        {
            m_Target = target;
        }

        string IContextSelection.Classifier => "Console, Editor Log, Player Log";

        string IContextSelection.Description
        {
            get
            {
                if (m_Target == null)
                    return "No log selected";

                return $"{m_Target.Value.Message.Substring(0, Mathf.Min(m_Target.Value.Message.Length, 200))}";
            }
        }

        string IContextSelection.Payload
        {
            get
            {
                if (m_Target == null)
                    return null;

                return $"{UnityDataUtils.OutputLogData(m_Target.Value, true)}";
            }
        }

        string IContextSelection.DownsizedPayload
        {
            get
            {
                if (m_Target == null)
                    return null;

                return $"{UnityDataUtils.OutputLogData(m_Target.Value, false)}";
            }
        }

        string IContextSelection.ContextType
        {
            get
            {
                if (m_Target == null)
                {
                    return "UNSET!";
                }

                switch (m_Target.Value.Type)
                {
                    case LogDataType.Info:
                    {
                        return "console log";
                    }

                    case LogDataType.Error:
                    {
                        return "console error";
                    }

                    case LogDataType.Warning:
                    {
                        return "console warning";
                    }

                    default:
                    {
                        throw new InvalidDataException("Unknown log type: " + m_Target.Value.Type);
                    }
                }
            }
        }

        string IContextSelection.TargetName => string.Empty;

        bool? IContextSelection.Truncated => null;

        bool IEquatable<IContextSelection>.Equals(IContextSelection other)
        {
            if (ReferenceEquals(this, other))
                return true;

            if (this == null || other == null)
                return false;

            if (other is not ConsoleContextSelection otherSelection)
                return false;

            var asConsoleContext = other as ConsoleContextSelection;

            return asConsoleContext.m_Target?.Message == m_Target?.Message;
        }
}
}
