using System;
using System.IO;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Provides a base class to manage the lifecycle of a VisualElement template including helper functions for templates and other assets.
    /// To use this class, create a new class that inherits from ManagedTemplate and implement the InitializeView method to set up the template.
    /// For each element Initialize() must be called to initialize.
    ///
    /// Templates will also get a shared stylesheet loaded from the shared asset path if it is set.
    /// </summary>
    abstract class ManagedTemplate : TemplateContainer
    {
        readonly StyleCache m_StyleCache;
        readonly ViewCache m_ViewCache;

        readonly string m_AssetPath;
        string m_SharedAssetPath;

        string m_ResourceNameOverride = string.Empty;
        string m_ResourcePrefix = string.Empty;
        string m_ResourceSuffix = string.Empty;

        Type m_ElementType;

        /// <summary>
        /// Creates a new ManagedTemplate with the specified custom element type.
        /// </summary>
        /// <param name="customElementType">The type of the custom templated element</param>
        /// <param name="basePath">The base path to use for this template</param>
        protected ManagedTemplate(Type customElementType, string basePath = null)
            : this(basePath)
        {
            m_ElementType = customElementType;
        }

        /// <summary>
        /// Creates a new ManagedTemplate
        /// </summary>
        /// <param name="basePath">The base path to use for this template</param>
        /// <param name="subPath">The custom sub-path to use, can be left empty or null if not applicable</param>
        protected ManagedTemplate(string basePath = null, string subPath = null)
        {
            pickingMode = PickingMode.Ignore;

            m_ElementType = GetType();

            if (string.IsNullOrEmpty(basePath))
            {
                basePath = AssistantUIConstants.UIEditorPath;
            }

            m_ViewCache = ViewCache.Get(basePath, subPath);
            m_StyleCache = StyleCache.Get(basePath, subPath);

            m_AssetPath = basePath + AssistantUIConstants.AssetFolder;
        }

        /// <summary>
        /// Event that is triggered when the visibility of the template changes.
        /// </summary>
        public event Action<bool> VisibilityChanged;

        /// <summary>
        /// Determines if the template is currently shown.
        /// </summary>
        public virtual bool IsShown { get; protected set; }

        /// <summary>
        /// Determines if the template has been initialized.
        /// </summary>
        protected bool IsInitialized { get; private set; }

        /// <summary>
        /// The overall context of the current UI visual tree (i.e window or visible control)
        /// </summary>
        protected AssistantUIContext Context { get; private set; }

        /// <summary>
        /// Initializes the template, this is mandatory to call before using the template.
        /// </summary>
        /// <param name="context">The application context in which the current element operates</param>
        /// <param name="autoShowControl">If true the template will be shown by default, otherwise it will be loaded in its default state</param>
        public virtual void Initialize(AssistantUIContext context, bool autoShowControl = true)
        {
            Context = context;

            ResourceName = string.IsNullOrEmpty(m_ResourceNameOverride)
                ? m_ElementType.Name
                : m_ResourceNameOverride;

            ResourceNameInvariant = ResourceName.ToLowerInvariant();

            DoInitView();
            DoInitStyle();

            if (autoShowControl)
            {
                Show();
            }

            IsInitialized = true;
        }

        /// <summary>
        /// Get a class name prefixed with the current template
        /// </summary>
        /// <param name="className">The class name to prefix</param>
        /// <returns>The fully prefixed class name as applicable to this template</returns>
        public string GetPrefixedClassName(string className)
        {
            return $"{ResourceNameInvariant}__{className}";
        }

        /// <summary>
        /// Add a prefixed class to the template
        /// </summary>
        /// <param name="className">the base class name to add</param>
        public void AddPrefixedClass(string className)
        {
            string prefixedName = GetPrefixedClassName(className);
            AddToClassList(prefixedName);
        }

        /// <summary>
        /// Remove a prefixed class from the template
        /// </summary>
        /// <param name="className">the base class name to remove</param>
        public void RemovePrefixedClass(string className)
        {
            string prefixedName = GetPrefixedClassName(className);
            this.RemoveFromClassList(prefixedName);
        }

        /// <summary>
        /// Hides the templated element
        /// </summary>
        public virtual void Hide(bool sendVisibilityChanged = true)
        {
            if (!IsShown)
            {
                return;
            }

            style.display = DisplayStyle.None;
            IsShown = false;

            if (sendVisibilityChanged)
            {
                VisibilityChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// Shows the templated element
        /// </summary>
        public virtual void Show(bool sendVisibilityChanged = true)
        {
            if (IsShown)
            {
                return;
            }

            style.display = DisplayStyle.Flex;
            IsShown = true;

            if (sendVisibilityChanged)
            {
                VisibilityChanged?.Invoke(true);
            }
        }

        protected Type ElementType => m_ElementType;

        protected bool IsVirtualControl { get; }

        protected string ResourceName { get; private set; }

        protected string ResourceNameInvariant { get; private set; }

        protected abstract void InitializeView(TemplateContainer view);

        protected void SetElementType(Type type)
        {
            m_ElementType = type;
        }

        protected void SetResourceName(string value)
        {
            m_ResourceNameOverride = value;
        }

        protected void SetResourcePrefix(string value)
        {
            m_ResourcePrefix = value;
        }

        protected void SetResourceSuffix(string value)
        {
            m_ResourceSuffix = value;
        }

        protected bool LoadStyle(VisualElement target, string styleName, bool fullFileName = false)
        {
            string styleFile = $"{m_ResourcePrefix}{styleName}{m_ResourceSuffix}";

#if UNITY_EDITOR
            if (!fullFileName)
            {
                styleFile = string.Concat(styleFile, AssistantUIConstants.StyleExtension);
            }
#endif

            var styleSheet = m_StyleCache.Load(styleFile);
            if (styleSheet == null)
            {
                return false;
            }

            target.styleSheets.Add(styleSheet);
            return true;
        }

        protected bool LoadStyle(string styleName, bool fullFileName = false)
        {
            return LoadStyle(this, styleName, fullFileName);
        }

        protected bool LoadView<T>(out VisualTreeAsset view)
        {
            return LoadView(typeof(T).Name, out view);
        }

        protected bool LoadView(string viewName, out VisualTreeAsset view)
        {
            string viewFile = $"{m_ResourcePrefix}{viewName}{m_ResourceSuffix}";

#if UNITY_EDITOR
            viewFile = string.Concat(viewFile, AssistantUIConstants.TemplateExtension);
#endif

            view = m_ViewCache.Load(viewFile);
            return view != null;
        }

        protected bool LoadAsset<T>(string relativePath, ref T target)
            where T : UnityEngine.Object
        {
            string assetFile = $"{m_AssetPath}{m_ResourcePrefix}{relativePath}";
            return UXLoader.LoadAsset(assetFile, ref target);
        }

        protected bool LoadSharedAsset<T>(string relativePath, ref T target)
            where T : UnityEngine.Object
        {
            string assetFile = $"{m_SharedAssetPath ?? m_AssetPath}{m_ResourcePrefix}{relativePath}";
            return UXLoader.LoadAsset(assetFile, ref target);
        }

        protected void LoadImage(VisualElement parentElement, string elementName, string iconPath, ref Texture2D cache)
        {
            var targetElement = parentElement.Q<Image>(elementName);
            if (targetElement == null)
            {
                return;
            }

            LoadImage(targetElement, iconPath, ref cache);
        }

        protected void LoadImage(Image target, string iconPath, ref Texture2D cache)
        {
            if (LoadAsset(iconPath, ref cache))
            {
                target.image = cache;
            }
        }

        protected void LoadBackgroundImage(VisualElement target, string iconPath, ref Texture2D cache)
        {
            if (LoadAsset(iconPath, ref cache))
            {
                target.style.backgroundImage = cache;
            }
        }

        protected void LoadSharedImage(VisualElement parentElement, string elementName, string iconPath, ref Texture2D cache)
        {
            var targetElement = parentElement.Q<Image>(elementName);
            if (targetElement == null)
            {
                return;
            }

            LoadSharedImage(targetElement, iconPath, ref cache);
        }

        protected void LoadSharedImage(Image target, string iconPath, ref Texture2D cache)
        {
            if (LoadSharedAsset(iconPath, ref cache))
            {
                target.image = cache;
            }
        }

        protected void LoadSharedBackgroundImage(VisualElement target, string iconPath, ref Texture2D cache)
        {
            if (LoadSharedAsset(iconPath, ref cache))
            {
                target.style.backgroundImage = cache;
            }
        }

        protected void SetSharedAssetPath(string newSharedPath)
        {
            m_SharedAssetPath = newSharedPath;
        }

        protected void RegisterAttachEvents(EventCallback<AttachToPanelEvent> attachEvent, EventCallback<DetachFromPanelEvent> detachEvent)
        {
            RegisterCallback(attachEvent);
            RegisterCallback(detachEvent);
        }

        void DoInitView()
        {
            if (this.IsVirtualControl)
            {
                return;
            }

            if (!LoadView(ResourceName, out VisualTreeAsset viewTree))
            {
                throw new InvalidDataException(ResourceName);
            }

            TemplateContainer view = viewTree.CloneTree();
            view.pickingMode = PickingMode.Ignore;

            view.AddToClassList(GetPrefixedClassName("element-root"));

            try
            {
                InitializeView(view);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            Add(view);
        }

        void DoInitStyle()
        {
            AddPrefixedClass("element");
        }
    }
}
