using System;
using System.Linq;
using GLTFast;
using Unity.AI.Generators.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    class GltfImporterProxy
    {
        static readonly Type k_GltfImporterType;

        readonly AssetImporter m_GltfImporter;

        static GltfImporterProxy()
        {
            var editorAssembly = AssemblyUtils.GetLoadedAssemblies().FirstOrDefault(a => a.GetName().Name == "glTFast.Editor");
            if (editorAssembly == null)
            {
                Debug.LogError("Could not find glTFast.Editor assembly.");
                return;
            }

            k_GltfImporterType = editorAssembly.GetType("GLTFast.Editor.GltfImporter");
            if (k_GltfImporterType == null)
            {
                Debug.LogError("Could not find GLTFast.Editor.GltfImporter type.");
            }
        }

        public GltfImporterProxy(AssetImporter importer)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            if (!IsGltfImporter(importer)) throw new ArgumentException("Importer is not a GltfImporter.", nameof(importer));

            m_GltfImporter = importer;
        }

        public static bool IsGltfImporter(AssetImporter importer)
        {
            return k_GltfImporterType != null && k_GltfImporterType.IsInstanceOfType(importer);
        }

        public void SetSceneObjectCreation(SceneObjectCreation value)
        {
            var serializedObject = new SerializedObject(m_GltfImporter);

            // --- THIS IS THE CORRECTED LINE ---
            // The property name is derived directly from the .meta file's structure.
            const string propertyPath = "instantiationSettings.sceneObjectCreation";
            var property = serializedObject.FindProperty(propertyPath);

            if (property == null)
            {
                Debug.LogError($"Could not find SerializedProperty '{propertyPath}' on GltfImporter. The property name may have changed in a new version of glTFast.");
                return;
            }

            property.enumValueIndex = (int)value;
            if (serializedObject.ApplyModifiedProperties())
            {
                m_GltfImporter.SaveAndReimport();
            }
        }
    }
}
