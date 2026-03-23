namespace Unity.AI.Assistant.FunctionCalling
{
    interface IFunctionSource
    {
        LocalAssistantFunction[] GetFunctions();
    }
}
