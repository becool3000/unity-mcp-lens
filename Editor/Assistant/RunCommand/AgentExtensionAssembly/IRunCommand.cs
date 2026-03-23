namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor
{
#if CODE_LIBRARY_INSTALLED
    public
#else
    internal
#endif
    interface IRunCommand
    {
        public void Execute(ExecutionResult result);
    }
}
