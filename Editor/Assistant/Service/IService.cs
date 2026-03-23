using System;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Editor.Service
{
    interface IService : IAsyncDisposable
    {
        /// <summary>
        /// If this function completes successfully, then the service can be considered ready to use. If an exception
        /// is thrown, then the service failed to initialize and cannot be used.
        /// </summary>
        public Task Initialize();
    }
}
