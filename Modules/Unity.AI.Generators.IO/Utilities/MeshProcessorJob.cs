using System;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Srp.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Abstract base class to manage a mesh processing/rendering job.
    /// Manages the lifecycle of a PreviewRenderUtility and processes work over multiple editor frames.
    /// </summary>
    abstract class MeshProcessorJob<T>
    {
        protected readonly GameObject m_GameObject;
        protected readonly TaskCompletionSource<T> m_Tcs;

        PreviewRenderUtility m_PreviewUtility;
        GameObject m_PreviewInstance;

        protected MeshProcessorJob(GameObject gameObject, TaskCompletionSource<T> tcs)
        {
            m_GameObject = gameObject;
            m_Tcs = tcs;
        }

        public void Start()
        {
            try
            {
                if (m_GameObject == null)
                    throw new ArgumentNullException(nameof(m_GameObject));

                // Initialize resources on the main thread.
                m_PreviewUtility = new PreviewRenderUtility();
                ConfigurePreviewUtility(m_PreviewUtility);

                m_PreviewInstance = EditorUtilityWrapper.InstantiateForAnimatorPreview(m_GameObject);
                EditorUtilityWrapper.InitInstantiatedPreviewRecursive(m_PreviewInstance);

                InitializeProcessing(m_PreviewInstance);

                EditorApplication.update += Update;
            }
            catch (Exception e)
            {
                Cleanup();
                m_Tcs.TrySetException(e);
            }
        }

        void Update()
        {
            try
            {
                if (IsJobFinished())
                {
                    Finish();
                }
                else
                {
                    // Let the subclass perform one unit of work.
                    Process(m_PreviewInstance, m_PreviewUtility);
                }
            }
            catch (Exception e)
            {
                Cleanup();
                m_Tcs.TrySetException(e);
            }
        }

        void Finish()
        {
            try
            {
                FinalizeProcessing();
                // Subclass is responsible for setting the TCS result.
            }
            catch (Exception e)
            {
                m_Tcs.TrySetException(e);
            }
            finally
            {
                Cleanup();
            }
        }

        void Cleanup()
        {
            EditorApplication.update -= Update;

            m_PreviewUtility?.Cleanup();
            m_PreviewUtility = null;

            if (m_PreviewInstance != null)
            {
                m_PreviewInstance.SafeDestroy();
                m_PreviewInstance = null;
            }

            CleanupProcessing();
        }

        static void ConfigurePreviewUtility(PreviewRenderUtility utility)
        {
            utility.camera.fieldOfView = 30.0f;
            utility.camera.allowHDR = true;
            utility.camera.allowMSAA = true;
            utility.camera.farClipPlane = 1000.0f;
            utility.camera.nearClipPlane = 0.1f;
            utility.ambientColor = new Color(.4f, .4f, .4f, 0);
            utility.lights[0].transform.rotation = Quaternion.Euler(50f, 50f, 0);
            SrpUtilities.SetupPreviewLight(utility.lights[0], 1.2f);
            utility.lights[1].transform.rotation = Quaternion.Euler(-50f, -50f, 0);
            SrpUtilities.SetupPreviewLight(utility.lights[1], 0.8f);
        }

        protected abstract void InitializeProcessing(GameObject previewInstance);
        protected abstract bool IsJobFinished();
        protected abstract void Process(GameObject previewInstance, PreviewRenderUtility utility);
        protected abstract void FinalizeProcessing();
        protected abstract void CleanupProcessing();
    }

    /// <summary>
    /// A private job that renders exactly one frame and then finishes.
    /// </summary>
    class SingleFrameRenderJob : MeshProcessorJob<bool>
    {
        readonly float m_RotationY;
        readonly RenderTexture m_TargetBuffer;
        bool m_IsFinished;

        public SingleFrameRenderJob(GameObject gameObject, TaskCompletionSource<bool> tcs, float rotationY, RenderTexture targetBuffer)
            : base(gameObject, tcs)
        {
            m_RotationY = rotationY;
            m_TargetBuffer = targetBuffer;
            m_IsFinished = false;
        }

        protected override void InitializeProcessing(GameObject previewInstance) { }
        protected override bool IsJobFinished() => m_IsFinished;

        protected override void Process(GameObject previewInstance, PreviewRenderUtility utility)
        {
            // This method will only be called once.
            var bounds = GetBounds(previewInstance);
            RenderFrame(previewInstance, utility, m_RotationY, bounds, m_TargetBuffer);
            m_IsFinished = true;
        }

        protected override void FinalizeProcessing() => m_Tcs.TrySetResult(true);
        protected override void CleanupProcessing() { }

        public static void RenderFrame(GameObject previewInstance, PreviewRenderUtility utility, float rotationY, Bounds bounds, RenderTexture target)
        {
            utility.BeginPreview(new Rect(0, 0, target.width, target.height), null);
            utility.AddSingleGO(previewInstance);

            try
            {
                var sphereRadius = bounds.extents.magnitude > 0.001f ? bounds.extents.magnitude : 1f;
                var distance = sphereRadius * 3.75f;
                if (distance < 2f) distance = 5f;

                previewInstance.transform.rotation = Quaternion.Euler(0, rotationY, 0);

                var camRot = Quaternion.Euler(20, -120, 0);
                var camPos = camRot * (Vector3.forward * -distance) + bounds.center;

                utility.camera.transform.position = camPos;
                utility.camera.transform.rotation = camRot;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = Color.clear;
                utility.camera.targetTexture = target;

                SetEnabledRecursive(previewInstance, true);
                utility.Render(true);
                SetEnabledRecursive(previewInstance, false);

                utility.EndPreview();
            }
            finally
            {
                utility.camera.targetTexture = null;
            }
        }

        static void SetEnabledRecursive(GameObject go, bool enabled)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                renderer.enabled = enabled;
        }

        static Bounds GetBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            var bounds = new Bounds();
            var hasBounds = false;
            foreach (var r in renderers)
            {
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            return bounds;
        }
    }
}
