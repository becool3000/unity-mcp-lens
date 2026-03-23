namespace Unity.AI.Search.Editor
{
    record Result<T>(T value, bool isSuccess = true, string error = null)
    {
        public static Result<T> Success(T value) => new Result<T>(value);
        public static Result<T> Failure(string error) => new Result<T>(default, false, error);
    }
}
