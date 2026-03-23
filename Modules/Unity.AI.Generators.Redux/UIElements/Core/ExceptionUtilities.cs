using System;
using System.Diagnostics;
using UnityEditor;

namespace Unity.AI.Generators.UIElements.Core
{
    static class ExceptionUtilities
    {
        [Serializable]
        class Data : ScriptableSingleton<Data>
        {
            public bool detailedExceptionStack;
            public bool reduxLog;
        }

        public static Exception AggregateStack(Exception exception, StackTrace inner)
        {
            var fullException = exception;
            if (inner != null)
                fullException = new AggregateException(
                    exception,
                    new Exception("\n\n********************************* Source Stack Trace ******************************************\n" + inner + "\n************************************************************************************\n\n"));
            else
                Data.instance.detailedExceptionStack = true;
            return fullException;
        }

        const string k_InternalMenu = "internal:";
        const string k_DetailedExceptionStackMenu = "AI Toolkit/Internals/Detailed Redux Exceptions [Generators]";
        public static bool detailedExceptionStack => Data.instance.detailedExceptionStack;

        [MenuItem(k_InternalMenu + k_DetailedExceptionStackMenu, false, 1021)]
        static void ToggleDetailedException() => Data.instance.detailedExceptionStack = !detailedExceptionStack;

        [MenuItem(k_InternalMenu + k_DetailedExceptionStackMenu, true, 1021)]
        static bool ValidateDetailedException()
        {
            Menu.SetChecked(k_DetailedExceptionStackMenu, detailedExceptionStack);
            return true;
        }

        const string k_reduxLog = "AI Toolkit/Internals/Redux Log [Generators]";
        public static bool reduxLog => Data.instance.reduxLog;

        [MenuItem(k_InternalMenu + k_reduxLog, false, 1021)]
        static void ToggleReduxLog() => Data.instance.reduxLog = !reduxLog;

        [MenuItem(k_InternalMenu + k_reduxLog, true, 1021)]
        static bool ValidateReduxLog()
        {
            Menu.SetChecked(k_reduxLog, reduxLog);
            return true;
        }

        public static void LogRedux(string message)
        {
            if (reduxLog)
                UnityEngine.Debug.LogWarning(message);
        }
    }
}
