using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.AIDropdownIntegrations
{
    class GenerativeSubMenu : VisualElement
    {
        public GenerativeSubMenu()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/Components/AIDropdownIntegrations/AIDropdownIntegration.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdownRoot/AIDropdownRoot.uss"));
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdown/AIDropdown.uss"));

            AddToClassList("sub-menu");
            Add(CreateStandardLabel("3D Object", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.meshButtonItem)));
            Add(CreateStandardLabel("Animation", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.animationMenuItem)));
            Add(CreateStandardLabel("Material", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.materialMenuItem)));
            Add(CreateStandardLabel("Sound", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.soundMenuItem)));
            Add(CreateStandardLabel("Sprite", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.spriteMenuItem)));
            Add(CreateStandardLabel("Texture", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.textureMenuItem)));
            Add(CreateStandardLabel("Cubemap", () => EditorApplication.ExecuteMenuItem(Toolkit.Utility.MenuItems.cubemapMenuItem), false));
        }

        static Label CreateStandardLabel(string text, Action onClick, bool bottomMargin = true)
        {
            var label = new Label(text);
            label.AddToClassList("text-menu-item");
            label.AddToClassList("label-button");
            label.EnableInClassList("dropdown-item-with-margin", bottomMargin);
            label.AddManipulator(new Clickable(onClick));
            return label;
        }
    }

    static class MeshGeneratorInternals
    {
        const string k_EnableMeshGeneratorMenu = "AI Toolkit/Internals/Feature Flags/Enable Mesh Generator (BYOK)";
        const string k_EnableMeshGeneratorKey = "Feature_Flags_Enable_Mesh_Generator_BYOK";

        [MenuItem("internal:" + k_EnableMeshGeneratorMenu, false, -1000)]
        static void EnableMeshGeneratorOwnKey() => MeshGeneratorOwnKeyEnabled = !MeshGeneratorOwnKeyEnabled;

        [MenuItem("internal:" + k_EnableMeshGeneratorMenu, true, -1000)]
        static bool ValidateEnableMeshGeneratorOwnKey()
        {
            Menu.SetChecked(k_EnableMeshGeneratorMenu, MeshGeneratorOwnKeyEnabled);
            return true;
        }

        public static bool MeshGeneratorOwnKeyEnabled
        {
            get => EditorPrefs.GetBool(k_EnableMeshGeneratorKey, false);
            set => EditorPrefs.SetBool(k_EnableMeshGeneratorKey, value);
        }
    }
}
