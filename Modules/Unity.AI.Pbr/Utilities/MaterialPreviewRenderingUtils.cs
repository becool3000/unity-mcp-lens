using System;
using System.Collections.Generic;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialPreviewRenderingUtils
    {
        record CacheKey(string key, int width, int height);

        static readonly Dictionary<CacheKey, RenderTexture> k_Cache = new();

        static MaterialPreviewRenderUtility s_MaterialPreviewRenderUtility;

        [InitializeOnLoadMethod]
        static void InitializeOnLoad() => AssemblyReloadEvents.beforeAssemblyReload += Cleanup;

        static void Cleanup()
        {
            if (s_MaterialPreviewRenderUtility == null)
                return;
            s_MaterialPreviewRenderUtility.Cleanup();
            s_MaterialPreviewRenderUtility = null;
        }

        /// <summary>
        /// Renders a preview of the given material on a default cube.
        /// </summary>
        /// <param name="material">The material to preview.</param>
        /// <param name="state"></param>
        /// <param name="width">Width of the preview texture.</param>
        /// <param name="height">Height of the preview texture.</param>
        /// <param name="screenScaleFactor">Device size scaling</param>
        /// <param name="invalidateCache"></param>
        /// <returns>A RenderTexture containing the preview image.</returns>
        public static RenderTexture GetPreview(this MaterialResult material, IState state, int width = 128, int height = 128, float screenScaleFactor = 1, bool invalidateCache = false)
        {
            var texWidth = Mathf.RoundToInt(width * screenScaleFactor);
            var texHeight = Mathf.RoundToInt(height * screenScaleFactor);

            texWidth = Mathf.NextPowerOfTwo(Mathf.Clamp(texWidth, 128, 1024));
            texHeight = Mathf.NextPowerOfTwo(Mathf.Clamp(texHeight, 128, 1024));

            // Try to get from cache
            var cacheKey = new CacheKey(material.uri.GetLocalPath(), texWidth, texHeight);
            if (k_Cache.TryGetValue(cacheKey, out var cachedTexture) && !invalidateCache)
            {
                if (cachedTexture.IsValid())
                    return cachedTexture;

                // Evict invalid texture from cache
                k_Cache.Remove(cacheKey);
                if (cachedTexture != null)
                    RenderTexture.ReleaseTemporary(cachedTexture);
                cachedTexture = null;
            }

            if (!cachedTexture)
                cachedTexture = RenderTexture.GetTemporary(texWidth, texHeight, 0);
            k_Cache[cacheKey] = cachedTexture;

            s_MaterialPreviewRenderUtility ??= new MaterialPreviewRenderUtility();
            var t = material.GetTemporary(state);
            if (!typeof(Material).IsAssignableFrom(t.AsObject.GetType()))
                return null;

            var materialObj = t.AsObject as Material;
            if (!ShaderUtil.IsPassCompiled(materialObj, 0))
                ShaderUtil.CompilePass(materialObj, 0, true);

            s_MaterialPreviewRenderUtility.SetMaterial(materialObj);

            var rt = s_MaterialPreviewRenderUtility.DoRenderPreview(new Rect(0, 0, texWidth, texHeight), new GUIStyle());
            if (rt == null)
                return null;

            var previous = RenderTexture.active;
            Graphics.Blit(rt, cachedTexture);
            RenderTexture.active = previous;
            return cachedTexture;
        }

        class MaterialPreviewRenderUtility
        {
            PreviewRenderUtility m_PreviewUtility;
            GameObject m_Cube;

            /// <summary>
            /// Initializes the PreviewRenderUtility and creates a cube primitive.
            /// </summary>
            public MaterialPreviewRenderUtility()
            {
                m_PreviewUtility = new PreviewRenderUtility
                {
                    camera =
                    {
                        fieldOfView = 11.45f,
                        allowHDR = true,
                        allowMSAA = true
                    },
                    ambientColor = new Color(.1f, .1f, .1f, 0)
                };

                // Setup simple lighting.
                m_PreviewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
                Srp.Utilities.SrpUtilities.SetupPreviewLight(m_PreviewUtility.lights[0], 2.4f);
                Srp.Utilities.SrpUtilities.SetupPreviewLight(m_PreviewUtility.lights[1], 1.6f);

                // Create a cube using Unity's built-in primitive.
                m_Cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                // Remove the collider since it's not needed.
                var col = m_Cube.GetComponent<Collider>();
                if (col != null)
                    col.SafeDestroy();

                // The cube will be added to the preview context.
                m_PreviewUtility.AddSingleGO(m_Cube);
            }

            /// <summary>
            /// Releases resources held by the PreviewRenderUtility.
            /// </summary>
            public void Cleanup()
            {
                if (m_PreviewUtility == null)
                    return;
                m_PreviewUtility.Cleanup();
                m_PreviewUtility = null;
            }

            /// <summary>
            /// Assigns the supplied material to the cube's MeshRenderer.
            /// </summary>
            /// <param name="material">The material to preview.</param>
            public void SetMaterial(Material material)
            {
                if (m_Cube == null)
                    return;
                var renderer = m_Cube.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                }
            }

            /// <summary>
            /// Renders the preview of the cube with its current material.
            /// </summary>
            /// <param name="rect">The area for the preview.</param>
            /// <param name="background">The style used for background drawing.</param>
            /// <returns>A Texture containing the rendered preview image.</returns>
            public Texture DoRenderPreview(Rect rect, GUIStyle background)
            {
                // Ensure the preview rect is at least 64 pixels in height.
                var previewRect = rect;
                previewRect.height = Mathf.Max(previewRect.height, 64f);

                // Optionally save ambient settings if needed.
                var ambientColor = RenderSettings.ambientLight;

                // Begin the preview.
                m_PreviewUtility.BeginPreview(previewRect, background);

                // Position the cube at the origin.
                m_Cube.transform.position = Vector3.zero;
                m_Cube.transform.rotation = Quaternion.Euler(0, 180, 0);
                m_Cube.transform.localScale = new Vector3(1, 1, 0.01f);

                // Setup camera to look at the cube.
                var cam = m_PreviewUtility.camera;
                cam.orthographic = false;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100;
                cam.transform.position = new Vector3(0, 0, -5);
                cam.transform.rotation = Quaternion.identity;
                cam.transform.LookAt(Vector3.zero);

                // Temporarily set the camera clear color.
                var originalClearFlags = cam.clearFlags;
                var originalBackgroundColor = cam.backgroundColor;
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = Color.clear;

                // Render the preview.
                m_PreviewUtility.Render(true);

                // Restore camera settings.
                cam.clearFlags = originalClearFlags;
                cam.backgroundColor = originalBackgroundColor;

                // End and return the preview texture.
                var texture = m_PreviewUtility.EndPreview();

                // Restore any ambient settings if necessary.
                RenderSettings.ambientLight = ambientColor;
                return texture;
            }
        }
    }
}
