using System.Collections.Generic;
using System.Text;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    class CompilationErrors
    {
        public class ErrorLog
        {
            public int Line;
            public string Message;
        }

        public List<ErrorLog> Errors { get; } = new();

        public void Add(string message, int line = -1)
        {
            Errors.Add(new ErrorLog { Line = line, Message = message });
        }

        public override string ToString()
        {
            StringBuilder errorLog = new();
            foreach (var error in Errors)
                errorLog.AppendLine($"- Error {error.Message} (Line: {error.Line + 1})");

            return errorLog.ToString();
        }
    }
}
