using System.Collections.Generic;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class ScaleToFitObjectFieldImage : Manipulator
    {
        static readonly HashSet<ScaleToFitObjectFieldImage> k_Manipulators = new();

        Image m_TargetImage;

        ObjectField objectField => target as ObjectField;

        protected override void RegisterCallbacksOnTarget()
        {
            if (objectField == null)
                return;

            k_Manipulators.Add(this);

            objectField.RegisterValueChangedCallback(OnObjectValueChanged);
            objectField.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            objectField.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);

            Init();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            k_Manipulators.Remove(this);

            if (objectField == null)
                return;

            objectField.UnregisterValueChangedCallback(OnObjectValueChanged);
            objectField.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            objectField.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        void Init()
        {
            if (objectField.panel != null)
                m_TargetImage = objectField.Q<Image>(className: "unity-object-field-display__icon");
            if (m_TargetImage != null)
                m_TargetImage.scaleMode = ScaleMode.StretchToFill;
        }

        void OnAttachToPanel(AttachToPanelEvent evt) => Init();

        void OnObjectValueChanged<T>(ChangeEvent<T> evt) => UpdateImageScaling();

        void OnGeometryChanged(GeometryChangedEvent evt) => UpdateImageScaling();

        void UpdateImageScaling()
        {
            if (m_TargetImage == null || m_TargetImage.contentRect.height <= 0)
                return;

            var texture = m_TargetImage.image;
            if (!ImageFileUtilities.TryGetAspectRatio(texture, out var aspectRatio))
                aspectRatio = texture ? texture.width / (float)texture.height : 1;

            m_TargetImage.style.width = m_TargetImage.contentRect.height * aspectRatio;
        }

        class AssetReferenceReimportMonitor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                foreach (var manipulator in k_Manipulators)
                {
                    if (manipulator.m_TargetImage == null)
                        continue;
                    if (!manipulator.m_TargetImage.image)
                        continue;
                    foreach (var path in importedAssets)
                    {
                        if (string.IsNullOrEmpty(path))
                            continue;
                        if (AssetDatabase.GetAssetPath(manipulator.m_TargetImage.image) == path)
                            manipulator.UpdateImageScaling();
                    }
                }
            }
        }
    }
}
