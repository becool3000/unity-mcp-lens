using System;

namespace Unity.AI.Image.Services.Stores.States
{
    [Serializable]
    record PixelateSettings
    {
        public int targetSize = 256;
        public bool keepImageSize = true;
        public int pixelBlockSize = 8;
        public PixelateMode mode = PixelateMode.Centroid;
        public int outlineThickness;
        public const int minSamplingSize = 4;
    }

    enum PixelateMode
    {
        Centroid = 0,
        Contrast = 1,
        Bicubic = 2,
        Nearest = 3,
        Center = 4
    }
}
