using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Services
{
    static class DropdownExtension
    {
        internal static readonly List<(int order, Action<VisualElement> callback)> onShow = new();
        internal static readonly List<(int order, Action<VisualElement> callback)> onExtend = new();
        internal static readonly List<(int order, Action<VisualElement> callback)> onExtendMain = new();

        /// <summary>
        /// Registers a callback whenever the AI dropdown is about to be shown.
        ///
        /// This allows any kind of custom modifications to it.
        /// </summary>
        /// <param name="callback">The callback which will receive the dropdown as a VisualElement</param>
        /// <param name="order">When the callback should be called (high values means earlier)</param>
        /// <returns>A action to de-register the callback.</returns>
        public static Action RegisterOnShow(Action<VisualElement> callback, int order = 0)
        {
            var item = (order, callback);
            onShow.Add(item);
            return () => onShow.Remove(item);
        }

        /// <summary>
        /// Registers a callback to add a new menu item to the AI dropdown in the extension area.
        ///
        /// This method is usually only called once, ever per editor session.
        /// <example>
        /// DropdownExtension.RegisterMenuExtension(container => container.Add(new Label("my new menu item")));
        /// </example>
        /// </summary>
        /// <param name="callback">The callback which will receive the VisualElement of the menu item container to which to append new items to.</param>
        /// <param name="order">When the callback should be called (high values means earlier)</param>
        /// <returns>A action to de-register the callback.</returns>
        public static Action RegisterMenuExtension(Action<VisualElement> callback, int order = 0)
        {
            var item = (order, callback);
            onExtend.Add(item);
            return () => onExtend.Remove(item);
        }

        /// <summary>
        /// Registers a callback to add a new menu item to the AI dropdown in the general area.
        ///
        /// This method is usually only called once, ever per editor session.
        /// <example>
        /// DropdownExtension.RegisterMenuExtensionGeneral(container => container.Add(new Label("my new menu item")));
        /// </example>
        /// </summary>
        /// <param name="callback">The callback which will receive the VisualElement of the menu item container to which to append new items to.</param>
        /// <param name="order">When the callback should be called (high values means earlier)</param>
        /// <returns>A action to de-register the callback.</returns>
        public static Action RegisterMainMenuExtension(Action<VisualElement> callback, int order = 10)
        {
            var item = (order, callback);
            onExtendMain.Add(item);
            return () => onExtendMain.Remove(item);
        }
    }
}
