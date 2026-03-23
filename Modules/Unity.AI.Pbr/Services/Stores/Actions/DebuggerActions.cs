using System;
using Unity.AI.Pbr.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Pbr.Services.Stores.Actions
{
    static class DebuggerActions
    {
        public static readonly string slice = "debugger";
        public static Creator<DebuggerState> init => new($"{slice}/init");
        public static StandardAction<bool> setRecording => new($"{slice}/setRecording");
    }
}
