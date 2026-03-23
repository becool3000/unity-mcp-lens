using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Utilities
{

    [Serializable]
    struct GameObjectData
    {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("activeSelf")]
        public bool ActiveSelf;

        [JsonProperty("activeInHierarchy")]
        public bool ActiveInHierarchy;

        [JsonProperty("tag")]
        public string Tag;

        [JsonProperty("layer")]
        public int Layer;

        [JsonProperty("isStatic")]
        public bool IsStatic;

        [JsonProperty("instanceID")]
        public int InstanceID;

        [JsonProperty("transform")]
        public TransformData Transform;

        [JsonProperty("components")]
        public string[] Components;

        [JsonProperty("children")]
        public GameObjectData[] Children;
    }

    [Serializable]
    struct TransformData
    {
        [JsonProperty("position")]
        public Vector3Data Position;

        [JsonProperty("rotation")]
        public Vector3Data Rotation;

        [JsonProperty("scale")]
        public Vector3Data Scale;
    }

    [Serializable]
    struct Vector3Data
    {
        [JsonProperty("x")]
        public float X;

        [JsonProperty("y")]
        public float Y;

        [JsonProperty("z")]
        public float Z;
    }
}
