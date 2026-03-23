namespace Unity.AI.Assistant.Editor.Checkpoint.Git
{
    readonly struct GitInstanceConfig
    {
        public GitInstanceType Type { get; }
        public string CustomPath { get; }

        public GitInstanceConfig(GitInstanceType type, string customPath = null)
        {
            Type = type;
            CustomPath = customPath;
        }

        public static GitInstanceConfig Default => new(GitInstanceType.Embedded);
        public static GitInstanceConfig UseSystem() => new(GitInstanceType.System);
        public static GitInstanceConfig UseCustom(string path) => new(GitInstanceType.Custom, path);
    }
}
