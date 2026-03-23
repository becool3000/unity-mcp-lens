namespace Unity.AI.Assistant.Editor.Mcp.Configuration
{
    /// <summary>
    /// Result of a configuration load operation
    /// </summary>
    class ConfigLoadResult<T> where T : class
    {
        public T Config { get; }
        public bool Success { get; }
        public string ErrorMessage { get; }

        ConfigLoadResult(T config, bool success, string errorMessage)
        {
            Config = config;
            Success = success;
            ErrorMessage = errorMessage;
        }

        public static ConfigLoadResult<T> Succeeded(T config) => new(config, true, null);
        public static ConfigLoadResult<T> Failed(T fallbackConfig, string errorMessage) => new(fallbackConfig, false, errorMessage);
    }
}