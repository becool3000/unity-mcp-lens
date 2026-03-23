using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Animate.Motion;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Animate.Services.Utilities
{
    static class AnimationClipUtilities
    {
        public static readonly BipedVersion bipedVersion = BipedVersion.biped_v1;

        public enum BipedVersion
        {
            biped_v0,
            biped_v1
        }

        public const float blankDuration = 0.01f;

        public static bool IsBlank(this AnimationClip clip) => clip.length < blankDuration || clip.empty;

        public static async Task<AnimationClip> AnimationClipFromResultAsync(this AnimationClipResult result)
        {
            // is it already an animation clip?
            if (Path.GetExtension(result.uri.GetLocalPath()).Equals(AssetUtils.defaultAssetExtension, StringComparison.InvariantCultureIgnoreCase))
                return result.ImportAnimationClipTemporarily();

            // is it an fbx?
            if (result.IsFbx())
                return await result.ImportFbxAnimationClipTemporarily();

            if (result.IsFailed())
                return null;

            // Load the motion data from file
            var response = await MotionResponse.FromFileAsync(result.uri.GetLocalPath());
            return AnimationClipFromMotion(response);
        }

        public static AnimationClip AnimationClipFromResult(this AnimationClipResult result)
        {
            // is it already an animation clip?
            if (Path.GetExtension(result.uri.GetLocalPath()).Equals(AssetUtils.defaultAssetExtension, StringComparison.InvariantCultureIgnoreCase))
                return result.ImportAnimationClipTemporarily();

            // is it an fbx?
            if (result.IsFbx())
            {
                if (!AnimationClipCache.TryGetAnimationClip(result.uri, out var clip))
                    throw new NotImplementedException("FBX animation clip must have been previously asynchronously imported and cached.");
                return clip;
            }

            // Load the motion data from file
            var response = MotionResponse.FromFile(result.uri.GetLocalPath());
            return AnimationClipFromMotion(response);
        }

        static AnimationClip AnimationClipFromMotion(MotionResponse response)
        {
            // Load a Biped prefab just as the original code did (and use it to get the ActorDefinition)
            var biped = AssetDatabase.LoadMainAssetAtPath(
                $"Packages/com.unity.ai.assistant/Modules/Unity.AI.Animate/Motion/Runtime/Prefabs/Actors/{bipedVersion}/Biped_Humanoid.prefab"
            ) as GameObject;

            try
            {
                biped = Object.Instantiate(biped);
                var armatureMapping = biped.GetComponent<ArmatureMapping>();

                // Initialize our timeline and bake the motion data into it
                var timeline = new Unity.AI.Animate.Motion.Timeline();
                timeline.BakeFromResponse(
                    response.frames,
                    response.framesPerSecond,
                    armatureMapping
                );

                // Finally export to an AnimationClip using the reference posing armature
                var clip = timeline.ExportToHumanoidClip(armatureMapping);
                clip.NormalizeRootTransform();
                return clip;
            }
            finally
            {
                biped.SafeDestroy();
            }
        }

        public static bool CopyTo(this AnimationClip from, AnimationClip to)
        {
            var wasBlank = to.IsBlank();
            to.ClearCurves();

            // Copy animation settings
            //AnimationUtility.SetAnimationClipSettings(to, AnimationUtility.GetAnimationClipSettings(from));

            var curveBindings = AnimationUtility.GetCurveBindings(from);
            var targetCurves = new AnimationCurve [curveBindings.Length];
            for (var i = 0; i < curveBindings.Length; i++)
            {
                var binding = curveBindings[i];
                targetCurves[i] = AnimationUtility.GetEditorCurve(from, binding);
            }

            // SetEditorCurves is much more efficient because it only executes synchronization once
            AnimationUtility.SetEditorCurves(to, curveBindings, targetCurves);

            if (wasBlank)
                to.SetDefaultClipSettings();

            EditorUtility.SetDirty(to);
            return true;
        }

        /// <summary>
        /// Determines if the AnimationClip can be edited by these tools.
        /// Only Unity humanoid animation clips are supported.
        /// </summary>
        public static bool CanBeEdited(this AnimationClip animClip)
        {
            if (animClip == null || !animClip.isHumanMotion)
                return false;

            // Check if it contains curves of type Animator (muscle clip)
            var hasMuscleClips = false;
            var bindings = AnimationUtility.GetCurveBindings(animClip);
            if (bindings.Length == 0)
                return false;

            foreach (var binding in bindings)
            {
                if (binding.type == typeof(Animator))
                {
                    hasMuscleClips = true;
                    break;
                }
            }

            return hasMuscleClips;
        }
    }
}
