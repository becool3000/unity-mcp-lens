using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    record ApiActions(string slice)
    {
        public static string subscribe = "subscribe";
        public static string unsubscribe = "unsubscribe";

        public CreatorWithAction<ApiSubscribeCache> ApiCacheSubscribe = new($"{slice}/{subscribe}");
        public CreatorWithAction<ApiUnsubscribeCache> ApiCacheUnsubscribe = new($"{slice}/{unsubscribe}");
        public Creator<ApiCacheKey> ApiCacheRemove = new($"{slice}/removeCache");
    }
}
