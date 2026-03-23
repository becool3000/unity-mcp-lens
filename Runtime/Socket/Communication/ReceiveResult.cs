using System;
using Unity.AI.Assistant.Socket.Protocol.Models;

namespace Unity.AI.Assistant.Socket.Communication
{
    class ReceiveResult
    {
        public bool IsDeserializedSuccessfully;
        public IModel DeserializedData;
        public string RawData;
        public Exception Exception;
    }
}
