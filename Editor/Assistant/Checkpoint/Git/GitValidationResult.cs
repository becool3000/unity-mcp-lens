namespace Unity.AI.Assistant.Editor.Checkpoint.Git
{
    readonly struct GitValidationResult
    {
        public string GitPath { get; }
        public string GitVersion { get; }
        public string LfsVersion { get; }
        public string ErrorMessage { get; }

        public bool GitFound => !string.IsNullOrEmpty(GitVersion);
        public bool LfsInstalled => !string.IsNullOrEmpty(LfsVersion);
        public bool IsValid => GitFound && LfsInstalled;

        GitValidationResult(string gitPath, string gitVersion, string lfsVersion, string errorMessage)
        {
            GitPath = gitPath ?? string.Empty;
            GitVersion = gitVersion;
            LfsVersion = lfsVersion;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static GitValidationResult Valid(string gitPath, string gitVersion, string lfsVersion)
        {
            return new GitValidationResult(gitPath, gitVersion, lfsVersion, null);
        }

        public static GitValidationResult GitNotFound(string path)
        {
            return new GitValidationResult(path, null, null, $"Git not found: {path}");
        }

        public static GitValidationResult LfsMissing(string gitPath, string gitVersion)
        {
            return new GitValidationResult(gitPath, gitVersion, null, "Git LFS is not installed. Please install Git LFS to use checkpoints.");
        }

        public static GitValidationResult Error(string gitPath, string errorMessage)
        {
            return new GitValidationResult(gitPath, null, null, errorMessage);
        }
    }
}
