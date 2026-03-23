using System;
using Newtonsoft.Json;
using Unity.AI.Assistant.Editor.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    [Serializable]
    struct ImageOutput
    {
        [Serializable]
        public struct ImageMetadata
        {
            [JsonProperty("width")]
            public int Width;

            [JsonProperty("height")]
            public int Height;

            /// <summary>
            /// Optional name for user-friendly storage
            /// </summary>
            [JsonProperty("display_name", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
            public string DisplayName;

            /// <summary>
            /// The format of the base64 encoded data
            /// </summary>
            [JsonProperty("format")]
            public string Format;

            /// <summary>
            /// The size of the image content in bytes
            /// </summary>
            [JsonProperty("size")]
            public int SizeInBytes;
        }

        [JsonProperty("image_content")]
        public string ImageContent;

        [JsonProperty("description")]
        public string Description;

        [JsonProperty("metadata")]
        public ImageMetadata Metadata;

        public ImageOutput(Texture texture, string description, string displayName = null)
        {
            ImageContent = texture.ToBase64PNG(out var sizeInBytes, out var newWidth, out var newHeight);
            if (string.IsNullOrEmpty(ImageContent))
                throw new Exception("Failed to get image content.");

            Description = description;
            Metadata = new ImageMetadata
            {
                Width = newWidth,
                Height = newHeight,
                DisplayName = displayName,
                Format = "png",
                SizeInBytes = sizeInBytes
            };
        }

        public ImageOutput(byte[] imageBytes, int width, int height, string description, string displayName = null, string format = "png")
        {
            ImageContent = Convert.ToBase64String(imageBytes);
            if (string.IsNullOrEmpty(ImageContent))
                throw new Exception("Failed to get image content.");

            Description = description;
            Metadata = new ImageMetadata
            {
                Width = width,
                Height = height,
                DisplayName = displayName,
                Format = format,
                SizeInBytes = imageBytes.Length
            };
        }
    }
}
