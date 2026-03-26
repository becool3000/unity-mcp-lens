namespace Unity.AI.Assistant.FunctionCalling
{
    interface IUserInteraction
    {
        string Action { get; }
        string Detail { get; }
        string AllowLabel { get; }
        string DenyLabel { get; }
        bool ShowScope { get; }
        void Respond(ToolPermissions.UserAnswer answer);
    }
}
