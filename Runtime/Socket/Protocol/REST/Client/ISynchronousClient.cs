using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Ai.Assistant.Protocol.Utilities;
using UnityEngine.Networking;

namespace Unity.Ai.Assistant.Protocol.Client
{
    internal class RequestInterceptionCallbacks
    {
        public Action<IUnityWebRequest> OnAfterRequestSend;
        public Action<IUnityWebRequest> OnAfterResponseReceived;
    }

    /// <summary>
    /// Contract for Synchronous RESTful API interactions.
    ///
    /// This interface allows consumers to provide a custom API accessor client.
    /// </summary>
    internal interface IClient
    {
        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the GET http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> GetAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the POST http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> PostAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the PUT http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> PutAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the DELETE http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> DeleteAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the HEAD http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> HeadAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the OPTIONS http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> OptionsAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        /// <summary>
        /// Executes a non-blocking call to some <paramref name="path"/> using the PATCH http verb.
        /// </summary>
        /// <param name="path">The relative path to invoke.</param>
        /// <param name="options">The request parameters to pass along to the client.</param>
        /// <param name="configuration">Per-request configurable settings.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <typeparam name="T">The return type.</typeparam>
        /// <returns>A task eventually representing the response data, decorated with <see cref="ApiResponse{T}"/></returns>
        Task<ApiResponse<T>> PatchAsync<T>(string path, RequestOptions options, IReadableConfiguration configuration = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken), RequestInterceptionCallbacks callbacks = null);

        public void Dispose();
    }
}
