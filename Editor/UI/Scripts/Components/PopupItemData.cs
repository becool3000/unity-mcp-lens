using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Type of popup item for rendering purposes.
    /// </summary>
    enum PopupItemType
    {
        Normal,
        Separator
    }

    /// <summary>
    /// Data for a single item in a popup selector.
    /// </summary>
    class PopupItemData
    {
        public string Id { get; }
        public string DisplayText { get; }
        public string Description { get; }
        public Texture2D Icon { get; }
        public PopupItemType ItemType { get; }

        public PopupItemData(string id, string displayText, string description = null, Texture2D icon = null)
            : this(id, displayText, description, icon, PopupItemType.Normal)
        {
        }

        public PopupItemData(string id, string displayText, string description, Texture2D icon, PopupItemType itemType)
        {
            Id = id;
            DisplayText = displayText;
            Description = description;
            Icon = icon;
            ItemType = itemType;
        }

        /// <summary>
        /// Creates a separator item for visual grouping in popups.
        /// </summary>
        public static PopupItemData CreateSeparator()
            => new(null, null, null, null, PopupItemType.Separator);
    }

    /// <summary>
    /// Configuration for how a selector button is displayed.
    /// </summary>
    class SelectorDisplayConfig
    {
        public Texture2D Icon { get; set; }
        public string DisplayText { get; set; }
        public bool ShowCaret { get; set; }
    }
}
