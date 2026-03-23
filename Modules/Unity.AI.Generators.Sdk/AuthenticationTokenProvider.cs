using System;
using System.Threading.Tasks;
using AiEditorToolsSdk.Domain.Abstractions.Services;
using AiEditorToolsSdk.Domain.Core.Results;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Sdk
{
    /// <summary>
    /// Very safe and very thread-safe implementation of <see cref="IUnityAuthenticationTokenProvider"/>.
    /// This class captures the service token at construction and always returns the same value.
    /// Use this when stability is critical and you want to avoid any risk of race conditions or token refresh failures.
    /// Note: Unity Genesis Staging bust be used with AI Staging environments for this to work correctly, and Genesis production with AI production.
    /// </summary>
    class PreCapturedServiceTokenProvider : IUnityAuthenticationTokenProvider
    {
        readonly string m_Token;

        PreCapturedServiceTokenProvider(string token)
        {
            m_Token = token;
        }

        public Task<Result<string>> ForceRefreshToken()
        {
            return Task.FromResult(Result<string>.Ok(m_Token));
        }

        public Task<Result<string>> GetToken()
        {
            return Task.FromResult(Result<string>.Ok(m_Token));
        }

        public static async Task<PreCapturedServiceTokenProvider> Build()
        {
            var token = await CloudProjectSettings.GetServiceTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Failed to retrieve a valid service token.");
            }
            return new PreCapturedServiceTokenProvider(token);
        }
    }

    /// <summary>
    /// Very safe and very thread-safe implementation of <see cref="IUnityAuthenticationTokenProvider"/>.
    /// This class captures the authentication token at construction and always returns the same value.
    /// Use this when stability is critical and you want to avoid any risk of race conditions or token refresh failures.
    /// </summary>
    class PreCapturedAuthenticationTokenProvider : IUnityAuthenticationTokenProvider
    {
        readonly string m_Token = UnityConnectProvider.accessToken;

        public Task<Result<string>> ForceRefreshToken()
        {
            return Task.FromResult(Result<string>.Ok(m_Token));
        }

        public Task<Result<string>> GetToken()
        {
            return Task.FromResult(Result<string>.Ok(m_Token));
        }
    }

    class AuthenticationTokenProvider : IUnityAuthenticationTokenProvider
    {
        const string k_UnityHubUriScheme = "unityhub://";
        const string k_UnityHubLoginDomain = "login";

        // Track the last time the URL was opened
        static DateTime s_LastUrlOpenTime = DateTime.MinValue;
        static readonly TimeSpan k_URLOpenCooldown = TimeSpan.FromMinutes(1);
        static bool s_LastStatus = true;

        readonly int m_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        string m_Token = UnityConnectProvider.accessToken;

        public async Task<Result<string>> ForceRefreshToken()
        {
            try
            {
                return await EditorTask.RunOnMainThread(async () => await ForceRefreshTokenInternal());
            }
            catch
            {
                return Result<string>.Ok(m_Token);
            }
        }

        async Task<Result<string>> ForceRefreshTokenInternal()
        {
            try
            {
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != m_MainThreadId)
                    throw new InvalidOperationException("ForceRefreshTokenInternal must be called from the main thread.");

                var tcs = new TaskCompletionSource<bool>();
                CloudProjectSettings.RefreshAccessToken(callbackStatus => tcs.TrySetResult(callbackStatus));

                // Check if enough time has passed since the last URL open
                var currentTime = DateTime.Now;
                if (currentTime - s_LastUrlOpenTime >= k_URLOpenCooldown)
                {
                    // Open URL and update the timestamp
                    Application.OpenURL($"{k_UnityHubUriScheme}{k_UnityHubLoginDomain}");
                    s_LastUrlOpenTime = currentTime;
                }

                const int timeoutSeconds = 30;
                var completedTask = await Task.WhenAny(tcs.Task, EditorTask.Delay((int)TimeSpan.FromSeconds(timeoutSeconds).TotalMilliseconds));
                if (completedTask == tcs.Task)
                {
                    m_Token = UnityConnectProvider.accessToken;

                    var status = await tcs.Task;
                    if (status)
                        Debug.Log("Access token refreshed successfully.");
                    else if (s_LastStatus)
                        Debug.LogError("Token refresh failed or was not needed.");

                    s_LastStatus = status;
                    return Result<string>.Ok(m_Token);
                }

                Debug.LogWarning($"Token refresh timed out after {timeoutSeconds} seconds.");
                return Result<string>.Fail();
            }
            catch
            {
                return Result<string>.Ok(m_Token);
            }
        }

        public async Task<Result<string>> GetToken()
        {
            try
            {
                return await EditorTask.RunOnMainThread(
                    () => Task.FromResult(Result<string>.Ok(m_Token = UnityConnectProvider.accessToken)));
            }
            catch
            {
                return Result<string>.Ok(m_Token);
            }
        }
    }
}
