using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor.Utilities
{
    class PreviewRenderUtility : IDisposable
    {
        UnityEditor.PreviewRenderUtility m_Preview;
        GameObject m_Instance;

        public PreviewRenderUtility()
        {
            m_Preview = new UnityEditor.PreviewRenderUtility
            {
                camera =
                {
                    fieldOfView = 30f,
                    allowHDR = true,
                    allowMSAA = true
                },
                ambientColor = new Color(0.1f, 0.1f, 0.1f, 0)
            };

            m_Preview.lights[0].intensity = 1.3f;
            m_Preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
            m_Preview.lights[1].intensity = 0.8f;
            m_Preview.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
        }

        void SetTarget(GameObject go)
        {
            CleanupInstance();
            if (go == null) return;

            m_Instance = Object.Instantiate(go);
            m_Instance.hideFlags = HideFlags.HideAndDontSave;
            m_Instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m_Instance.transform.localScale = Vector3.one;

            m_Preview.AddSingleGO(m_Instance);
        }

        public List<Texture2D> RenderViews(GameObject go, GameObjectPreviewOptions options)
        {
            SetTarget(go);
            if (m_Instance == null) return new List<Texture2D>();

            var cam = m_Preview.camera;
            cam.orthographic = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;

            var b = CalculateBounds(m_Instance);
            var center = b.center;
            var radius = Mathf.Max(b.extents.magnitude, 0.001f);
            var fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            var distance = radius / Mathf.Sin(fovRad * 0.5f) * 1.2f;

            var rect = new Rect(0, 0, options.Width, options.Height);
            var outputs = new List<Texture2D>(options.Images);
            var bg = new GUIStyle();

            for (var i = 0; i < options.Images; i++)
            {
                var yaw = options.BaseYaw + i * options.StepDegrees;
                var pitch = options.Pitch;

                var dir = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
                cam.transform.position = center - dir.normalized * distance;
                cam.transform.LookAt(center);

                m_Preview.BeginPreview(rect, bg);
                m_Preview.Render(true);
                var tex = m_Preview.EndPreview();

                if (tex == null) continue;

                var readable = TextureUtils.ResizeTextureSinglePass(tex,
                    options.Width, options.Height,
                    RenderTextureReadWrite.Default);

                Object.DestroyImmediate(tex);
                
                outputs.Add(readable);
            }

            return outputs;
        }

        static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        public void Dispose()
        {
            CleanupInstance();
            if (m_Preview != null)
            {
                m_Preview.Cleanup();
                m_Preview = null;
            }
        }

        void CleanupInstance()
        {
            if (m_Instance == null) return;
            try
            {
                Object.DestroyImmediate(m_Instance);
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex, LogFilter.Search);
            }

            m_Instance = null;
        }
    }
}