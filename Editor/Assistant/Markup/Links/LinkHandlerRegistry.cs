using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Editor
{
    static class LinkHandlerRegistry
    {
        const string k_UrlDelimiter = "://";

        static Dictionary<string, ILinkHandler> s_Handlers = new();

        static LinkHandlerRegistry()
        {
            RegisterAllHandlers();
        }

        /// <summary>
        /// Checks if a custom handler can handle this link
        /// </summary>
        /// <param name="url">The link</param>
        /// <returns>True if the link can be handled, false otherwise</returns>
        public static bool CanHandle(string url)
        {
            return TryParse(url, out _, out _, out _);
        }

        /// <summary>
        /// Try to handler the given link with a registered handler
        /// </summary>
        /// <param name="conversationId">The ID of the conversation</param>
        /// <param name="url">The link</param>
        /// <returns>True if the link was handled, false otherwise</returns>
        public static bool TryHandle(string conversationId, string url)
        {
            if (!TryParse(url, out var handler, out var prefix, out var link))
                return false;

            var context = new ILinkHandler.Context(conversationId);
            handler.Handle(context, prefix, link);
            return true;
        }

        /// <summary>
        /// Add a custom handler
        /// </summary>
        /// <param name="prefix">
        /// The link prefix for this handler to be called.
        /// For instance the prefix 'mylink' would handle any url starting with 'mylink://'
        /// </param>
        /// <param name="handler">The handler to be called when a matching link is clicked</param>
        public static void AddHandler(string prefix, ILinkHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (string.IsNullOrEmpty(prefix))
                throw new Exception($"{nameof(prefix)} cannot be null or empty");

            if (s_Handlers.ContainsKey(prefix))
                throw new InvalidOperationException($"Handler with prefix {prefix} has already been added");

            if (prefix.Contains(k_UrlDelimiter))
                throw new Exception($"{nameof(prefix)} must not contain '{k_UrlDelimiter}'");

            s_Handlers.Add(prefix, handler);
        }

        /// <summary>
        /// Remove the handler for the given prefix
        /// </summary>
        /// <param name="prefix">The link prefix</param>
        public static void RemoveHandler(string prefix)
        {
            s_Handlers.Remove(prefix);
        }

        /// <summary>
        /// Remove the given handler instance
        /// </summary>
        /// <param name="handler">The handler instance</param>
        public static void RemoveHandler(ILinkHandler handler)
        {
            if (handler == null)
                return;

            using var keysToRemovePool = ListPool<string>.Get(out var keysToRemove);
            foreach (var kvp in s_Handlers)
            {
                if (kvp.Value == handler)
                    keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                s_Handlers.Remove(key);
            }
        }

        /// <summary>
        /// Remove all handlers
        /// </summary>
        public static void ClearHandlers()
        {
            s_Handlers.Clear();
        }

        static bool TryParse(string url, out ILinkHandler handler, out string prefix, out string link)
        {
            handler = null;
            prefix = null;
            link = null;

            var delimiterIndex = url.IndexOf(k_UrlDelimiter);
            if (delimiterIndex < 0)
                return false;

            prefix = url.Substring(0, delimiterIndex);
            link = url.Substring(delimiterIndex + k_UrlDelimiter.Length).Trim();
            if (string.IsNullOrEmpty(link))
                return false;

            if (!s_Handlers.TryGetValue(prefix, out handler))
                return false;

            return true;
        }

        static void RegisterAllHandlers()
        {
            var typesWithAttribute = TypeCache.GetTypesWithAttribute<LinkHandlerAttribute>();

            foreach (var type in typesWithAttribute)
            {
                if (type.IsAbstract)
                    continue;

                if (!typeof(ILinkHandler).IsAssignableFrom(type))
                {
                    Debug.LogError($"Type {type.FullName} must implement {nameof(ILinkHandler)}");
                    continue;
                }

                if (Activator.CreateInstance(type) is ILinkHandler handler)
                {
                    var attributes = (LinkHandlerAttribute[])type.GetCustomAttributes(typeof(LinkHandlerAttribute), false);
                    foreach (var attr in attributes)
                    {
                        try
                        {
                            AddHandler(attr.Prefix, handler);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
        }

    }
}
