using System;
using Unity.AI.Animate.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Animate.Services.Stores.Actions
{
    static class DebuggerActions
    {
        public static readonly string slice = "debugger";
        public static Creator<DebuggerState> init => new($"{slice}/init");
        public static StandardAction<bool> setRecording => new($"{slice}/setRecording");
    }
}
