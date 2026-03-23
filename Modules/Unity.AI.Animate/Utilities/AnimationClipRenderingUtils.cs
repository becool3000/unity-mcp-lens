using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.AI.Animate.Services.Utilities
{
    static class AnimationClipRenderingUtils
    {
        static AvatarPreviewRenderUtility s_AvatarPreviewRenderUtility;
        static readonly Dictionary<RenderKey, RenderTexture> k_PendingRenderCache = new();
        static int s_RenderConcurrency = 0;
        const int k_RenderMaxConcurrency = 4;

        // Record type to uniquely identify render requests
        record RenderKey(AnimationClip Clip, float Time, int Width, int Height);

        [InitializeOnLoadMethod]
        static void InitializeOnLoad()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        static void Cleanup()
        {
            if (s_AvatarPreviewRenderUtility == null)
                return;
            s_AvatarPreviewRenderUtility.Cleanup();
            s_AvatarPreviewRenderUtility = null;
        }

        static readonly GUIStyle k_GUIStyle = new();

        public static RenderTexture GetTemporary(this AnimationClip animationClip, float time, int width = 64, int height = 64, RenderTexture reusableBuffer = null)
        {
            var renderKey = new RenderKey(animationClip, time, width, height);

            // Check if we already have a pending render for this key
            if (k_PendingRenderCache.TryGetValue(renderKey, out var pendingTexture))
            {
                if (pendingTexture.IsValid())
                    return pendingTexture;

                // Evict invalid texture from cache
                k_PendingRenderCache.Remove(renderKey);
                if (pendingTexture != null)
                    RenderTexture.ReleaseTemporary(pendingTexture);
            }

            // Create a new render texture or reuse the provided one
            if (!reusableBuffer || reusableBuffer.width != width || reusableBuffer.height != height)
            {
                if (reusableBuffer)
                    RenderTexture.ReleaseTemporary(reusableBuffer);
                reusableBuffer = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default);
            }

            // Add to pending cache
            k_PendingRenderCache[renderKey] = reusableBuffer;

            // Start rendering asynchronously
            _ = RenderAnimationAsync(renderKey, animationClip, time, width, height, reusableBuffer);

            return reusableBuffer;
        }

        static async Task RenderAnimationAsync(RenderKey renderKey, AnimationClip animationClip, float time, int width, int height, RenderTexture buffer)
        {
            // Wait if we've reached the concurrency limit
            while (s_RenderConcurrency >= k_RenderMaxConcurrency)
                await EditorTask.Yield();

            // Increment concurrency counter
            ++s_RenderConcurrency;

            try
            {
                // Perform the actual rendering
                s_AvatarPreviewRenderUtility ??= new AvatarPreviewRenderUtility();

                var animTime = time % animationClip.length;
                animationClip.SampleAnimation(s_AvatarPreviewRenderUtility.previewObject, animTime);

                if (!s_AvatarPreviewRenderUtility.IsValid())
                    return;

                var rt = s_AvatarPreviewRenderUtility.DoRenderPreview(new Rect(0, 0, width, height), k_GUIStyle);
                if (rt == null)
                    return;

                var previous = RenderTexture.active;
                Graphics.Blit(rt, buffer);
                Graphics.SetRenderTarget(previous);
            }
            finally
            {
                // Decrement concurrency counter
                --s_RenderConcurrency;

                // Remove from pending cache after rendering is complete
                k_PendingRenderCache.Remove(renderKey);
            }
        }

        class AvatarPreviewRenderUtility
        {
            /// The animator isn't used except to identify the hips, animation is stepped using low level sampling
            Animator animator => previewObject != null ? previewObject.GetComponent<Animator>() : null;

            public GameObject previewObject { get; }

            readonly Transform m_Hips;

            static GameObject GetHumanoidFallback() => (GameObject)EditorGUIUtility.Load("Avatar/DefaultAvatar.fbx");

            PreviewRenderUtility m_PreviewUtility;
            readonly GameObject m_ReferenceInstance;
            readonly GameObject m_DirectionInstance;
            readonly GameObject m_PivotInstance;
            readonly GameObject m_RootInstance;

            const bool k_ShowReference = true;

            readonly Vector2 m_PreviewDir = new(120, -20);
            readonly float m_AvatarScale = 1.0f;
            readonly float m_ZoomFactor = 1.0f;
            readonly Vector3 m_PivotPositionOffset;
            static readonly Vector3 k_InitialPivotPositionOffset = new(0, 0.75f, 0);

            void SetPreviewCharacterEnabled(bool enabled, bool showReference)
            {
                if (previewObject != null)
                    SetEnabledRecursive(previewObject, enabled);
                SetEnabledRecursive(m_ReferenceInstance, showReference && enabled);
                SetEnabledRecursive(m_DirectionInstance, showReference && enabled);
                SetEnabledRecursive(m_PivotInstance, showReference && enabled);
                SetEnabledRecursive(m_RootInstance, showReference && enabled);
            }

            static void SetEnabledRecursive(GameObject go, bool enabled)
            {
                foreach (var componentsInChild in go.GetComponentsInChildren<Renderer>())
                    componentsInChild.enabled = enabled;
            }

            PreviewRenderUtility previewUtility
            {
                get
                {
                    if (m_PreviewUtility != null)
                        return m_PreviewUtility;

                    m_PreviewUtility = new PreviewRenderUtility
                    {
                        camera =
                        {
                            fieldOfView = 24.0f,
                            allowHDR = false,
                            allowMSAA = false
                        },
                        ambientColor = new Color(.1f, .1f, .1f, 0)
                    };
                    m_PreviewUtility.lights[0].intensity = 1.4f;
                    m_PreviewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
                    m_PreviewUtility.lights[1].intensity = 1.4f;
                    return m_PreviewUtility;
                }
            }

            public void Cleanup()
            {
                if (m_PreviewUtility == null)
                    return;

                m_PreviewUtility.Cleanup();
                m_PreviewUtility = null;
            }

            public AvatarPreviewRenderUtility()
            {
                var go = GetHumanoidFallback();

                previewObject = EditorUtilityWrapper.InstantiateForAnimatorPreview(go);
                previewUtility.AddSingleGO(previewObject);

                if (animator)
                {
                    m_AvatarScale = m_ZoomFactor = animator.humanScale;
                    if (animator.isHuman)
                        m_Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                }

                var referenceGo = (GameObject)EditorGUIUtility.Load("Avatar/dial_flat.prefab");
                m_ReferenceInstance = Object.Instantiate(referenceGo, Vector3.zero, Quaternion.identity);
                EditorUtilityWrapper.InitInstantiatedPreviewRecursive(m_ReferenceInstance);
                previewUtility.AddSingleGO(m_ReferenceInstance);

                var directionGo = (GameObject)EditorGUIUtility.Load("Avatar/arrow.fbx");
                m_DirectionInstance = Object.Instantiate(directionGo, Vector3.zero, Quaternion.identity);
                EditorUtilityWrapper.InitInstantiatedPreviewRecursive(m_DirectionInstance);
                previewUtility.AddSingleGO(m_DirectionInstance);

                var pivotGo = (GameObject)EditorGUIUtility.Load("Avatar/root.fbx");
                m_PivotInstance = Object.Instantiate(pivotGo, Vector3.zero, Quaternion.identity);
                EditorUtilityWrapper.InitInstantiatedPreviewRecursive(m_PivotInstance);
                previewUtility.AddSingleGO(m_PivotInstance);

                var rootGo = (GameObject)EditorGUIUtility.Load("Avatar/root.fbx");
                m_RootInstance = Object.Instantiate(rootGo, Vector3.zero, Quaternion.identity);
                EditorUtilityWrapper.InitInstantiatedPreviewRecursive(m_RootInstance);
                previewUtility.AddSingleGO(m_RootInstance);

                SetPreviewCharacterEnabled(false, false);
                m_PivotPositionOffset = k_InitialPivotPositionOffset;
            }

            static bool ContainsNaN(Vector3 v) => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z);

            public bool IsValid() => !m_Hips || !ContainsNaN(m_Hips.position);

            public Texture DoRenderPreview(Rect rect, GUIStyle background)
            {
                var previewRect = rect;
                previewRect.height = Mathf.Max(previewRect.height, 64f);

                var probe = RenderSettings.ambientProbe;
                previewUtility.BeginPreview(previewRect, background);

                var bodyRot = Quaternion.identity;
                var rootRot = Quaternion.identity;
                var rootPos = Vector3.zero;
                var bodyPos = previewObject ? previewObject.transform.position : Vector3.zero;
                var pivotPos = Vector3.zero;

                if (animator)
                {
                    rootRot = animator.rootRotation;
                    rootPos = animator.rootPosition;
                    bodyRot = animator.bodyRotation;
                    pivotPos = animator.pivotPosition;
                }

                previewUtility.lights[0].intensity = 1.4f;
                previewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
                previewUtility.lights[1].intensity = 1.4f;
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.1f, 1.0f);
                RenderSettings.ambientProbe = probe;

                var direction = bodyRot * Vector3.forward;
                if (m_Hips)
                {
                    direction = m_Hips.forward;
                    bodyPos = m_Hips.position;
                }

                direction[1] = 0;
                bodyPos[1] = 0;
                var directionRot = Quaternion.LookRotation(direction);
                var directionPos = rootPos;
                var pivotRot = rootRot;

                m_ReferenceInstance.transform.position = rootPos;
                m_ReferenceInstance.transform.rotation = rootRot;
                m_ReferenceInstance.transform.localScale = Vector3.one * m_AvatarScale * 1.25f;

                m_DirectionInstance.transform.position = directionPos;
                m_DirectionInstance.transform.rotation = directionRot;
                m_DirectionInstance.transform.localScale = Vector3.one * m_AvatarScale * 2;

                m_PivotInstance.transform.position = pivotPos;
                m_PivotInstance.transform.rotation = pivotRot;
                m_PivotInstance.transform.localScale = Vector3.one * m_AvatarScale * 0.1f;

                m_RootInstance.transform.position = bodyPos;
                m_RootInstance.transform.rotation = bodyRot;
                m_RootInstance.transform.localScale = Vector3.one * m_AvatarScale * 0.25f;

                previewUtility.camera.orthographic = false;
                previewUtility.camera.nearClipPlane = 0.5f * m_ZoomFactor;
                previewUtility.camera.farClipPlane = 100.0f * m_AvatarScale;
                var camRot = Quaternion.Euler(-m_PreviewDir.y, -m_PreviewDir.x, 0);

                var camPos = camRot * (Vector3.forward * -5.5f * m_ZoomFactor) + bodyPos + m_PivotPositionOffset;
                previewUtility.camera.transform.position = camPos;
                previewUtility.camera.transform.rotation = camRot;

                var clearMode = previewUtility.camera.clearFlags;
                var clearColor = previewUtility.camera.backgroundColor;
                previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
                previewUtility.camera.backgroundColor = Color.clear;
                SetPreviewCharacterEnabled(true, k_ShowReference);
                previewUtility.Render(false);
                SetPreviewCharacterEnabled(false, false);
                previewUtility.camera.clearFlags = clearMode;
                previewUtility.camera.backgroundColor = clearColor;

                clearMode = previewUtility.camera.clearFlags;
                previewUtility.camera.clearFlags = CameraClearFlags.Nothing;
                previewUtility.Render(false);
                previewUtility.camera.clearFlags = clearMode;
                return previewUtility.EndPreview();
            }
        }
    }
}
