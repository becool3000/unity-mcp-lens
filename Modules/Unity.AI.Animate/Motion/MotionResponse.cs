using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Animate.Motion
{
    class MotionResponse : IDisposable
    {
        [Serializable]
        struct SerializedFrame
        {
            [JsonProperty("positions")]
            public string positionsBase64;

            [JsonProperty("rotations", Required = Required.Always)]
            public string rotationsBase64;
        }

        [Serializable]
        struct SerializedResponse
        {
            [JsonProperty("fps")]
            public float fps;

            [JsonProperty("frames", Required = Required.Always)]
            public SerializedFrame[] frames;
        }

        public class Frame
        {
            public Vector3[] positions;
            public Quaternion[] rotations;
        }

        public List<Frame> frames { get; private set; } = new();
        public float framesPerSecond { get; private set; }

        public void Dispose() => frames.Clear();

        public static async Task<MotionResponse> FromFileAsync(string fileName) => FromJson(await FileIO.ReadAllTextAsync(fileName));

        public static MotionResponse FromFile(string fileName) => FromJson(FileIO.ReadAllText(fileName));

        static MotionResponse FromJson(string text)
        {
            var data = JObject.Parse(text);
            var response = new MotionResponse();
            response.Deserialize(data);
            return response;
        }

        void Deserialize(JObject data)
        {
            Dispose();

            var sr = data.ToObject<SerializedResponse>();
            framesPerSecond = sr.fps;

            frames = new List<Frame>(sr.frames?.Length ?? 0);
            if (sr.frames == null)
                return;
            foreach (var sf in sr.frames)
            {
                // decode positions
                var posFloats = MotionUtilities.DecodeFloatsFromBase64(sf.positionsBase64, 3, out var posElemCount);
                var positionsArray = new Vector3[posElemCount];
                var idx = 0;
                for (var i = 0; i < posElemCount; i++)
                {
                    var x = posFloats[idx++];
                    var y = posFloats[idx++];
                    var z = posFloats[idx++];
                    positionsArray[i] = new Vector3(x, y, z);
                }

                // decode rotations
                var rotFloats = MotionUtilities.DecodeFloatsFromBase64(sf.rotationsBase64, 4, out var rotElemCount);
                var rotationsArray = new Quaternion[rotElemCount];
                idx = 0;
                for (var i = 0; i < rotElemCount; i++)
                {
                    var w = rotFloats[idx++];
                    var x = rotFloats[idx++];
                    var y = rotFloats[idx++];
                    var z = rotFloats[idx++];
                    rotationsArray[i] = new Quaternion(x, y, z, w);
                }

                frames.Add(new Frame
                {
                    positions = positionsArray,
                    rotations = rotationsArray
                });
            }
        }
    }
}
