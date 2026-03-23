using System;
using System.Linq;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TextureSizeHint = Unity.AI.Generators.UI.Utilities.TextureSizeHint;

namespace Unity.AI.ModelSelector.Components
{
    class ModelTile : VisualElement
    {
        ModelSettings m_Model;

        readonly ModelTileCarousel m_ModelTileCarousel;
        readonly Image m_PartnerIcon;
        readonly Button m_FavoriteButton;
        readonly ModelTitleCard m_ModelTitleCard;
        readonly VisualElement m_FavoriteButtonSpinner;
        readonly Clickable m_Clickable;

        internal Action<ModelSettings> showModelDetails;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.ModelSelector/Components/ModelSelector/ModelTile.uxml";

        public ModelSettings model => m_Model;

        public ModelTile()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            EnableInClassList("light-theme", !EditorGUIUtility.isProSkin);
            EnableInClassList("dark-theme", EditorGUIUtility.isProSkin);

            AddToClassList("model-tile");

            m_ModelTileCarousel = this.Q<ModelTileCarousel>();
            m_ModelTitleCard = this.Q<ModelTitleCard>();
            m_PartnerIcon = this.Q<Image>(className: "model-tile-icon");
            m_FavoriteButton = this.Q<Button>(className: "model-tile-favorite-button");
            m_FavoriteButtonSpinner = m_FavoriteButton.Q<VisualElement>("favorite-spinner");

            m_Clickable = new Clickable(OnClick);
            m_ModelTitleCard.AddManipulator(m_Clickable);
            m_FavoriteButton.clicked += () =>
            {
                if (!string.IsNullOrEmpty(m_Model?.id))
                    this.Dispatch(Services.Stores.Actions.ModelSelectorActions.toggleFavoriteModel.Invoke(m_Model.id));
            };
        }

        void OnClick() => showModelDetails?.Invoke(m_Model);

        public void OnModelSelected(string modelID) => EnableInClassList("is-selected", m_Model != null && !string.IsNullOrEmpty(modelID) && m_Model.id == modelID);

        public async void SetModel(ModelSettings modelSettings)
        {
            m_Model = modelSettings;
            _ = m_ModelTitleCard.SetModelAsync(m_Model);
            tooltip = m_Model.description;
            if (Unsupported.IsDeveloperMode())
                tooltip += $"\n{m_Model.id}";

            if (this.GetState() != null)
                OnModelSelected(this.GetState().SelectSelectedModelID());

            m_ModelTileCarousel.SetImages(m_Model.thumbnails.Select(s => new Uri(s)));

            if (m_PartnerIcon != null && !string.IsNullOrEmpty(modelSettings.icon))
                m_PartnerIcon.image = await TextureCache.GetPreview(new Uri(modelSettings.icon), (int)TextureSizeHint.Partner);

            m_FavoriteButton.SetEnabled(!m_Model.favoriteProcessing);
            m_FavoriteButton.Q<Image>().SetShown(!m_Model.favoriteProcessing);
            if (m_Model.favoriteProcessing)
                m_FavoriteButton.Add(m_FavoriteButtonSpinner);
            else
                m_FavoriteButtonSpinner.RemoveFromHierarchy();

            EnableInClassList("is-favorite", m_Model.isFavorite);
        }
    }

    class ModelTileItem : VisualElement
    {
        ModelTile m_Tile;

        public ModelTile tile
        {
            get => m_Tile;
            set
            {
                if (m_Tile != null)
                    Remove(m_Tile);
                if (value != null)
                    Add(value);
                m_Tile = value;
            }
        }
    }
}
