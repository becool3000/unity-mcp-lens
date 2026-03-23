using System;
using System.IO;
using UnityEngine;
using System.Threading.Tasks;
using UnityEditor;

namespace Unity.AI.Search.Editor.Services.Models
{
    class SigLip2 : IDisposable
    {
        public static readonly SigLipModelInfo ModelInfo = SigLipModelInfo.Create(384);

#if SENTIS_AVAILABLE
        SigLip2Text Text { get; set; } = new SigLip2Text();
        SigLip2Image Image { get; set; } = new SigLip2Image();

        public bool CanLoad() => Text.CanLoad() && Image.CanLoad() && SigLip2TagMatcher.CanLoad(TagsFilePath);

        // Convenience methods that delegate to the specialized classes
        public async Task<float[]> GetTextEmbeddings(string text) => await Text.GetTextEmbeddings(text);
        public async Task<float[][]> GetTextEmbeddings(string[] texts) => await Text.GetTextEmbeddings(texts);

        public async Task<float[]> GetImageEmbeddings(Texture2D image) => await Image.GetImageEmbeddings(image);
        public async Task<float[][]> GetImageEmbeddings(Texture2D[] images) => await Image.GetImageEmbeddings(images);
#endif
        static string s_TagsFilePath;

        public static string TagsFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(s_TagsFilePath))
                {
                    // Find unity_tag_list in this package:
                    try
                    {
                        var packageInfo =
                            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SigLip2).Assembly);
                        var tagsFileGuids =
                            AssetDatabase.FindAssets("unity_tag_list t:TextAsset", new[] { packageInfo.assetPath });

                        s_TagsFilePath = AssetDatabase.GUIDToAssetPath(tagsFileGuids[0]);
                    }
                    catch
                    {
                        // Ignored
                    }
                }

                return s_TagsFilePath;
            }
        }

        public static string GetTagsCacheFilePath()
        {
            return Path.Combine("Library", "AI.Search", Path.GetFileNameWithoutExtension(TagsFilePath) + ".embeddings");
        }

        public void Dispose()
        {
#if SENTIS_AVAILABLE
            Text?.Dispose();
            Text = null;
            Image?.Dispose();
            Image = null;
#endif
        }
    }
}