using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.FunctionCalling
{
    class LocalAssistantFunction : ICachedFunction, IAssemblyFunction
    {
        public Assembly Assembly => Method?.DeclaringType?.Assembly;
        public MethodInfo Method { get; }
        public FunctionDefinition FunctionDefinition { get; }

        /// <summary>
        /// The required editor mode for this function (flags).
        /// </summary>
        public ToolCallEnvironment ToolCallEnvironment;

        public LocalAssistantFunction(MethodInfo method, string description, string id, AssistantMode mode, string[] tags, ToolCallEnvironment environment)
        {
            Method = method;
            FunctionDefinition = LocalAssistantFunctionJsonUtils.GetFunctionDefinition(
                method,
                description ,
                id,
                mode,
                tags);

            ToolCallEnvironment = environment;
        }

        public async Task<object> InvokeAsync(ToolExecutionContext context)
        {
            if (Method == null)
            {
                InternalLog.LogError("Trying to invoke a null function!");
                return null;
            }

            // Use async validation that can auto-switch from PlayMode to EditMode if needed
            await FunctionCallingUtilities.ValidateEnvironmentOrThrow(ToolCallEnvironment, forceModeSwitch: true);

            var requiredParameters = FunctionDefinition.Parameters.Where(p => !p.Optional).ToArray();

            if (requiredParameters.Count() > context.Call.Parameters.Count)
                throw new TargetParameterCountException();

            var isAsync = Method.GetCustomAttribute<AsyncStateMachineAttribute>() != null;

            object result;
            object[] parameters = LocalAssistantFunctionJsonUtils.ConvertJsonParametersToObjects(context, Method, FunctionDefinition);

            if (isAsync)
            {
                result = await InvokeAsyncInternal(parameters);
            }
            else
            {
                result = Method.Invoke(null, parameters);
            }

            return result;
        }

        async Task<object> InvokeAsyncInternal(object[] parameters)
        {
            var task = (Task)Method.Invoke(null, parameters);
            await task;

            if (task.GetType().IsGenericType)
            {
                var resultProperty = task.GetType().GetProperty("Result");
                var result = resultProperty.GetValue(task);

                return result;
            }

            return null;
        }
    }
}