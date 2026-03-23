using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PaletteImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/PaletteImageReference.uxml";

        readonly UnityEngine.UIElements.Image m_ImagePalette;

        Texture2D m_PaletteTexture;

        ~PaletteImageReference()
        {
            try
            {
                m_PaletteTexture?.SafeDestroy();
            }
            catch
            {
                // ignored
            }
        }

        public PaletteImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("palette-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<PaletteImageReference, Services.Stores.States.PaletteImageReference>();

            m_ImagePalette = this.Q<UnityEngine.UIElements.Image>(className: "palette-colors");
            this.Use(state => state.SelectPaletteImageBytesTimeStamp(this), OnPaletteChanged);

            m_ImagePalette.SetEnabled(false);
        }

        void OnPaletteChanged(Timestamp payload)
        {
            if (!m_PaletteTexture)
                m_PaletteTexture = new Texture2D(2, 2) { filterMode = FilterMode.Point };
            _ = LoadPalette();
            return;

            async Task LoadPalette()
            {
                await using var stream = await this.GetState().SelectPaletteImageStream(this);
                if (stream is not { Length: > 0 })
                {
                    m_ImagePalette.image = null;
                    m_ImagePalette.Q<Label>().SetShown();
                    m_ImagePalette.SetEnabled(false);
                    return;
                }

                await EditorTask.Yield();
                var paletteAssetBytes = TextureUtils.CreatePaletteApproximation(await stream.ReadFullyAsync());
                m_PaletteTexture.LoadImage(paletteAssetBytes);
                m_ImagePalette.Q<Label>().SetShown(false);
                m_ImagePalette.image = m_PaletteTexture;
                m_ImagePalette.SetEnabled(true);
                m_ImagePalette.MarkDirtyRepaint();
            }
        }

        public ImageReferenceType type => ImageReferenceType.PaletteImage;

        public bool showBaseImageByDefault => false;

        public bool invertStrength => false;

        public bool allowEdit => true;
    }
}
