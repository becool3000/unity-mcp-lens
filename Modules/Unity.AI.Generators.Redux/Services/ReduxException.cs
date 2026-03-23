using System;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Toolkit
{
    /// <summary>
    /// Serializable exception that can be used in cache.
    /// </summary>
    [Serializable]
    record ReduxException
    {
        public const string DataKey = "redux";

        public string ExceptionType;
        public string Message;
        public string StackTrace;
        public ReduxException InnerException;
        [SerializeReference]
        public object data;

        // Optional: Additional fields like HelpLink, Source, etc.

        /// <summary>
        /// Constructs a SerializableException from a standard Exception.
        /// </summary>
        /// <param name="ex">The exception to serialize.</param>
        public ReduxException(Exception ex)
        {
            if (ex == null)
                throw new ArgumentNullException(nameof(ex));

            ExceptionType = ex.GetType().FullName;
            Message = ex.Message;
            StackTrace = ex.StackTrace;
            InnerException = ex.InnerException != null ? new ReduxException(ex.InnerException) : null;

            if (ex.Data.Contains(DataKey))
                data = ex.Data[DataKey];
        }

        /// <summary>
        /// Reconstructs the exception message with stack trace and inner exceptions.
        /// Note: This does not recreate the original Exception object.
        /// </summary>
        /// <returns>A formatted string representing the exception.</returns>
        public override string ToString()
        {
            return FormatException(this);
        }

        string FormatException(ReduxException ex, int level = 0)
        {
            string indent = new string(' ', level * 2);
            string result = $"{indent}{ex.ExceptionType}: {ex.Message}\n{indent}{ex.StackTrace}\n";
            if (ex.InnerException != null)
            {
                result += $"{indent}Inner Exception:\n{FormatException(ex.InnerException, level + 1)}";
            }
            return result;
        }
    }
}
