using System;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;

namespace Unity.AI.Image.Utilities
{
    static class GenerationSettingExtensions
    {
        public static void ApplyUnsavedAssetBytes(this GenerationSetting state, UnsavedAssetBytesData payload)
        {
            state.unsavedAssetBytes.data = payload.data;
            state.unsavedAssetBytes.timeStamp = DateTime.UtcNow.Ticks;
            state.unsavedAssetBytes.uri = payload.result?.uri;
            state.unsavedAssetBytes.spriteSheet = payload.result.IsValid() && payload.result.IsSpriteSheet();
            state.unsavedAssetBytes.duration = payload.result.IsValid() && payload.result.IsSpriteSheet() ? payload.result.GetDuration() : 0;
        }

        public static void ApplyEditedDoodle(this GenerationSetting state, (ImageReferenceType imageReferenceType, byte[] data) payload)
        {
            state.imageReferences[(int)payload.imageReferenceType] = state.imageReferences[(int)payload.imageReferenceType] with
            {
                mode = payload.data is { Length: > 0 } ? ImageReferenceMode.Doodle : ImageReferenceMode.Asset,
                doodle = payload.data,
                doodleTimestamp = DateTime.UtcNow.Ticks
            };
        }

        public static byte[] SelectEditedDoodle(this GenerationSetting state, ImageReferenceType imageReferenceType) =>
            state.imageReferences[(int)imageReferenceType].doodle;
    }
}
