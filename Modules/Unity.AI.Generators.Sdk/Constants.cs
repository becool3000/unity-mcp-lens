using System;

namespace Unity.AI.Generators.Sdk
{
    static class Constants
    {
        public static readonly TimeSpan noTimeout = TimeSpan.FromDays(1);
        public static readonly TimeSpan realtimeTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan generateTimeout = TimeSpan.FromSeconds(30);
        // this is the full time to live of the generation on the server
        public static readonly TimeSpan generationTimeToLive = TimeSpan.FromMinutes(30);

        public static readonly TimeSpan motionDownloadCreateUrlRetryTimeout = TimeSpan.FromSeconds(90);
        public static readonly TimeSpan imageDownloadCreateUrlRetryTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan soundDownloadCreateUrlRetryTimeout = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan statusCheckCreateUrlRetryTimeout = TimeSpan.FromSeconds(5);
        public const int retryCount = 15;

        public static readonly TimeSpan modelsFetchTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan referenceUploadTimeToLive = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan referenceUploadCreateUrlTimeout = TimeSpan.FromSeconds(45);

        public const int downloadRefreshPointsDelayMs = 5000;
    }
}
