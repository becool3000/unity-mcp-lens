using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AudioFileUtilities
    {
        public static async Task<Stream> ConvertAsync(Stream inputStream, string toExtension)
        {
            if (inputStream is not { CanSeek: true, CanRead: true })
                return null;

            var fromExtension = FileTypeSupport.GetFileExtension(inputStream);

            if (string.Equals(fromExtension, toExtension, StringComparison.OrdinalIgnoreCase))
                return inputStream;

            if (fromExtension == AssetUtils.mp3AssetExtension && toExtension == AssetUtils.wavAssetExtension)
            {
                var tempFileName = $"{Guid.NewGuid()}.mp3";
                using var tempAssetScope = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { (tempFileName, inputStream) });
                var tempAsset = tempAssetScope.assets.FirstOrDefault();
                if (tempAsset == null)
                {
                    Debug.LogError("Failed to create temporary asset for MP3 conversion.");
                    return null;
                }

                var audioClip = tempAsset.asset.GetObject() as AudioClip;
                if (audioClip)
                {
                    var wavStream = new MemoryStream();
                    await audioClip.EncodeToWavAsync(wavStream, new SoundEnvelopeSettings());
                    wavStream.Position = 0;
                    return wavStream;
                }
            }

            return null;
        }
    }
}
