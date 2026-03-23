namespace Unity.AI.Search.Editor.Utilities
{
    record GameObjectPreviewOptions
    {
        public readonly int Images = 4;
        public readonly float BaseYaw = 0f;
        public readonly float Pitch = 30f;
        public readonly float StepDegrees = 90f;
        public readonly int Width = 256;
        public readonly int Height = 256;

        public GameObjectPreviewOptions(int images = 4, float baseYaw = 0f, float pitch = 30f, float stepDegrees = 90f)
        {
            Images = images;
            BaseYaw = baseYaw;
            Pitch = pitch;
            StepDegrees = stepDegrees;
            Width = AssetInspectors.k_DefaultPreviewWidth;
            Height = AssetInspectors.k_DefaultPreviewHeight;
        }
    }
}