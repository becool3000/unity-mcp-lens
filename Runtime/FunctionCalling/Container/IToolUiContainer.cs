namespace Unity.AI.Assistant.FunctionCalling
{
    interface IToolUiContainer
    {
        void PushElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction);
        void PopElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction);
    }
}
