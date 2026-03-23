using System.Collections.Generic;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.WhatsNew.Pages
{
    class WhatsNewContentGenerators : WhatsNewContent
    {
        // Menu item paths for the generator windows
#if AI_TOOLKIT_PRESENT
        const string k_MeshMenuItem = Toolkit.Utility.MenuItems.meshButtonItem;
        const string k_CubemapMenuItem = Toolkit.Utility.MenuItems.cubemapMenuItem;
#else
        const string k_MeshMenuItem = "Assets/Create/3D/Generate 3D Object";
        const string k_CubemapMenuItem = "Assets/Create/Rendering/Generate Cubemap";
#endif
        const string k_GeneratorsPackageName = "com.unity.ai.assistant";

        public override string Title => "Asset Generation";
        public override string Description => "Create placeholder textures, meshes, sprites, animations, audio, skyboxes, and effects. Trace their use for replacement when going to production.";

        TemplateContainer m_View;
        ListRequest m_ListRequest;
        readonly List<Button> m_GeneratorButtons = new();
        readonly List<Button> m_InstallButtons = new();

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            view.SetupButton("openAssistantButton", OnOpenAssistant);

            RegisterPage(view.Q<VisualElement>("page1"), "Generate_1_GeneratorsUI");
            RegisterPage(view.Q<VisualElement>("page2"), "Generate_2_UI");
            RegisterPage(view.Q<VisualElement>("page3"), "Generate_3_3D-models");
            RegisterPage(view.Q<VisualElement>("page4"), "Generate_4_Skybox");

            m_View = view;

            CheckGeneratorsPackage();
            
            void OnOpenAssistant(PointerUpEvent evt)
            {
                AssistantWindow.ShowWindow();
            }
        }

        void CheckGeneratorsPackage()
        {
            m_ListRequest = Client.List();
            EditorApplication.update += WaitForPackageList;
        }

        void WaitForPackageList()
        {
            if (!m_ListRequest.IsCompleted)
            {
                if (m_ListRequest.Error != null && !string.IsNullOrEmpty(m_ListRequest.Error.message))
                {
                    // If there's an error, assume generators exist to avoid blocking the user.
                    SetupButtons(true);
                    EditorApplication.update -= WaitForPackageList;
                }
                return;
            }

            if (m_ListRequest.Status == StatusCode.Success)
            {
                bool areGeneratorsInstalled = false;
                foreach (var package in m_ListRequest.Result)
                {
                    if (package.name == k_GeneratorsPackageName)
                    {
                        areGeneratorsInstalled = true;
                        break;
                    }
                }
                SetupButtons(areGeneratorsInstalled);
            }
            else if (m_ListRequest.Status >= StatusCode.Failure)
            {
                // If the request fails, assume generators exist to avoid blocking the user.
                SetupButtons(true);
            }

            EditorApplication.update -= WaitForPackageList;
        }

        void SetupButtons(bool generatorsExist)
        {
            m_GeneratorButtons.Add(m_View.SetupButton("generateModelButton", OnOpenModelGenerator));
            m_GeneratorButtons.Add(m_View.SetupButton("generateCubemapButton", OnOpenCubemapGenerator));

            m_InstallButtons.Add(m_View.SetupButton("installGeneratorsPage3", OnInstallGenerators));
            m_InstallButtons.Add(m_View.SetupButton("installGeneratorsPage4", OnInstallGenerators));

            // If generators exist, show buttons to open them; otherwise, show install buttons.
            foreach (var button in m_GeneratorButtons)
            {
                button?.SetDisplay(generatorsExist);
            }
            foreach (var button in m_InstallButtons)
            {
                button?.SetDisplay(!generatorsExist);
            }
        }

        void OnInstallGenerators(PointerUpEvent evt)
        {
            // Disable as feedback and to avoid multiple clicks while installation is running.
            foreach (var button in m_InstallButtons)
            {
                button?.SetEnabled(false);
            }

            Debug.Log($"Installing AI Generators package: {k_GeneratorsPackageName}");
            Client.Add(k_GeneratorsPackageName);
        }

        static void OnOpenModelGenerator(PointerUpEvent evt) => EditorApplication.ExecuteMenuItem(k_MeshMenuItem);

        static void OnOpenCubemapGenerator(PointerUpEvent evt) => EditorApplication.ExecuteMenuItem(k_CubemapMenuItem);
    }
}
