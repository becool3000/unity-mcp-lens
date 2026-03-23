using System;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class AmbientOcclusionUtils
    {
        static ComputeShader s_ComputeShader;
        static Shader s_FragmentShader;
        static Material s_BlitMaterial;

        public static Texture2D GenerateAOMap(Texture2D heightMap)
        {
            if (!s_ComputeShader)
                s_ComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Shaders/AmbientOcclusionScale.compute");
            
            using var resultBuffer = new ComputeBuffer(1, sizeof(float));

            var kernelHandle = s_ComputeShader.FindKernel("CSMain");
            s_ComputeShader.SetTexture(kernelHandle, "_InputTexture", heightMap);
            s_ComputeShader.SetBuffer(kernelHandle, "_ResultBuffer", resultBuffer);
            s_ComputeShader.SetFloat("_Width" , heightMap.width);
            s_ComputeShader.SetFloat("_Height" , heightMap.height);
            s_ComputeShader.Dispatch(kernelHandle, 1, 1, 1);

            var resultArray = new float[1];
            resultBuffer.GetData(resultArray);

            var scale = resultArray[0];

            if (!s_FragmentShader)
                s_FragmentShader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Shaders/AmbientOcclusion.shader");
            
            if (!s_BlitMaterial)
                s_BlitMaterial = new Material(s_FragmentShader);
            
            s_BlitMaterial.SetFloat("_DispScale", scale);

            var heightmapRT = RenderTexture.GetTemporary(heightMap.width, heightMap.height, 0, RenderTextureFormat.R16, RenderTextureReadWrite.Linear);
            Graphics.Blit(heightMap, heightmapRT);

            var destRT = RenderTexture.GetTemporary(heightMap.width, heightMap.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(heightmapRT, destRT, s_BlitMaterial);

            var previousRT = RenderTexture.active;
            RenderTexture.active = destRT;

            var aoMap = new Texture2D(heightMap.width, heightMap.height, TextureFormat.RGBA32, false, true);
            aoMap.ReadPixels(new Rect(0, 0, aoMap.width, aoMap.height), 0, 0);
            aoMap.Apply();

            RenderTexture.active = previousRT;
            RenderTexture.ReleaseTemporary(destRT);
            RenderTexture.ReleaseTemporary(heightmapRT);

            return aoMap;
        }
    }
}
