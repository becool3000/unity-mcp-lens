using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Animate.Services.Utilities
{
    static class AnimationClipDatabaseUtils
    {
        public struct SerializedData
        {
            public byte[] data;
            public string fileName;
        }

        /// <summary>
        /// Serializes an AnimationClip to a byte array via a temporary asset.
        /// Also returns the temporary asset filename that was used.
        /// </summary>
        public static SerializedData SerializeAnimationClip(AnimationClip clip)
        {
            if (!clip)
            {
                Debug.LogError("No data to serialize from AnimationClip.");
                return default;
            }

            using var temporaryAsset = ImportAssets(new[] { clip });
            var fileName = temporaryAsset.assets[0].asset.GetPath();
            var bytes = FileIO.ReadAllBytes(temporaryAsset.assets[0].asset.GetPath());
            return new SerializedData { data = bytes, fileName = fileName };
        }

        /// <summary>
        /// Deserializes the byte array back into an AnimationClip using the provided filename.
        /// </summary>
        public static AnimationClip DeserializeAnimationClip(string fileName, byte[] data)
        {
            if (data == null)
            {
                Debug.LogError("No data to deserialize to an AnimationClip.");
                return null;
            }

            using var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new[] { (fileName, data) });
            var animationClip = temporaryAsset.assets[0].asset.GetObject<AnimationClip>();
            return UnityEngine.Object.Instantiate(animationClip);
        }

        public static TemporaryAsset.Scope ImportAssets(IEnumerable<AnimationClip> clips)
        {
            var assets = clips.Select(ImportAsset).ToList();
            var validAssets = assets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        static TemporaryAsset ImportAsset(AnimationClip clip)
        {
            var tempFolder = $"{TemporaryAssetUtilities.toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            try
            {
                var guid = Guid.NewGuid().ToString();
                var fileName = $"Temp{clip.GetType().Name}_{guid}{AssetUtils.defaultAssetExtension}";
                var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));

                var clone = UnityEngine.Object.Instantiate(clip);
                clone.name = Path.GetFileNameWithoutExtension(fileName);

                AssetDatabase.CreateAsset(clone, destFileName);

                return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
            }
            catch
            {
                Directory.Delete(tempFolder, true);
                throw;
            }
        }
    }
}
