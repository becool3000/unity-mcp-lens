namespace Unity.AI.Assistant.Editor.Settings
{
    /// <summary>
    /// Fetch state for data that comes from an async source.
    /// </summary>
    enum FetchState
    {
        NotFetched,
        Fetching,
        Fetched,
        Unavailable,
        Error
    }

    record FetchInfo
    {
        public string Error { get; init; }
        public FetchState State { get; init; } = FetchState.NotFetched;

        public bool IsReady => State == FetchState.Fetched;
        public bool IsLoading => State == FetchState.Fetching;
        public bool HasError => State == FetchState.Error;
        public bool IsUnavailable => State == FetchState.Unavailable;
    }

    /// <summary>
    /// Generic wrapper for fetchable data. Provides easy equality via record.
    /// Used with Signal&lt;T&gt; for reactive UI updates.
    /// </summary>
    record Fetchable<T> : FetchInfo
    {
        public T Data { get; init; }

        public static Fetchable<T> NotFetched() => new() { State = FetchState.NotFetched };
        public static Fetchable<T> Fetching() => new() { State = FetchState.Fetching };
        public static Fetchable<T> Fetched(T data) => new() { State = FetchState.Fetched, Data = data };
        public static Fetchable<T> Failed(string error) => new() { State = FetchState.Error, Error = error };
        public static Fetchable<T> Unavailable() => new() { State = FetchState.Unavailable };
    }
}
