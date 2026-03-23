using System;

namespace Unity.AI.Image.Services.Stores.States
{
    [Serializable]
    record SpritesheetSettingsState
    {
        public int tileColumns = 4;
        public int tileRows = 4;
        public int outputWidth = 1024;
        public int outputHeight = 1024;

        public const int minTileCount = 1;
        public const int maxTileCount = 16;
        public const int minResolution = 64;
        public const int maxResolution = 4096;
    }
}
