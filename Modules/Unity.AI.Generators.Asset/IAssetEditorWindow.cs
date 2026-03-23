using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Generators.Asset
{
    /// <summary>
    /// Interface for EditorWindows with asset context
    /// </summary>
    interface IAssetEditorWindow
    {
        /// <summary>
        /// Initial or current asset context
        /// </summary>
        AssetReference asset { get; set; }

        /// <summary>
        /// Context lock/unlock padlock button
        /// </summary>
        bool isLocked { get; set; }

        /// <summary>
        /// The store
        /// </summary>
        IStore store { get; }

        /// <summary>
        /// A collection of base types that are allowed to be swapped in the asset context.
        /// If the current and next asset types derive from any of these types, the swap is allowed.
        /// </summary>
        IEnumerable<Type> allowedTypes => null;
    }
}
