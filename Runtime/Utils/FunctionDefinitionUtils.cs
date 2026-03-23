using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;

namespace Unity.AI.Assistant.Utils
{
    static class FunctionDefinitionUtils
    {
        public static FunctionsObject ToFunctionsObject(this FunctionDefinition function)
        {
            return new FunctionsObject
            {
                FunctionTag = function.Tags,
                FunctionMode = function.AssistantMode.ToNameList(),
                FunctionName = function.Name,
                FunctionNamespace = function.Namespace,
                FunctionDescription = function.Description,
                FunctionId = function.FunctionId,
                FunctionParameters = function.Parameters.Select(p => new FunctionsObject.FunctionParametersObject()
                {
                    ParameterName = p.Name,
                    ParameterType = p.Type,
                    ParameterDescription = p.Description,
                    ParameterIsOptional = p.Optional,
                    ParameterJsonSchema = p.JsonSchema
                }).ToList()
            };
        }
    }
}
