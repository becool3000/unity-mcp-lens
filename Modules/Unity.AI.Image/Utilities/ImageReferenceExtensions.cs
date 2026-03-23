using System;
using System.Linq;
using Unity.AI.Image.Components;
using Unity.AI.Image.Services.Contexts;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Utilities;
using Unity.AI.Image.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Services.Utilities
{
    interface IImageReference
    {
        ImageReferenceType type { get; }
        bool showBaseImageByDefault { get; }
        bool invertStrength { get; }
        bool allowEdit { get; }
    }

    static class ImageReferenceExtensions
    {
        public static void Bind<TImageReferenceElement,TImageReferenceState>(this TImageReferenceElement e)
            where TImageReferenceElement: VisualElement, IImageReference
            where TImageReferenceState : ImageReferenceSettings, new()
        {
            var objectField = e.Q<ObjectField>();
            var imageReferenceObjectField = e.Q<VisualElement>("image-reference-object-field");
            var doodleBackground = imageReferenceObjectField.Q<VisualElement>("doodle-pad-background-image");
            var doodleCanvas = imageReferenceObjectField.Q<UnityEngine.UIElements.Image>("doodle-pad-canvas");
            var settingsButton = e.Q<Button>("image-reference-settings-button");
            var doodlePad = e.Q<DoodlePad>("image-reference-object-field__doodle-pad");
            var editButton = e.Q<Button>("edit-image-reference");
            var strengthSlider = e.Q<SliderInt>("image-reference-strength-slider");
            var deleteImageReference = e.Q<Button>("delete-image-reference");
            var unsupportedModelOperationIconContainer = e.Q<VisualElement>("warning-icon-container");
            var unsupportedModelOperationBackground = e.Q<VisualElement>("warning-background");

            var iconWarning = unsupportedModelOperationIconContainer.Q<UnityEngine.UIElements.Image>("warning-icon");
            if (EditorGUIUtility.isProSkin)
            {
                iconWarning?.AddToClassList("dark-warning-icon");
                unsupportedModelOperationBackground?.AddToClassList("dark-warning-background");
            }
            else
            {
                iconWarning?.AddToClassList("light-warning-icon");
                unsupportedModelOperationBackground?.AddToClassList("light-warning-background");
            }

            // Make the doodle kind of "read-only"
            doodleBackground.pickingMode = PickingMode.Ignore;
            doodleCanvas.pickingMode = PickingMode.Ignore;
            doodlePad.backGroundImageOpacity = 0;
            editButton.SetShown(e.allowEdit);

            // UI events
            objectField.AddManipulator(new ScaleToFitObjectFieldImage());
            objectField.RegisterValueChangedCallback(async evt =>
            {
                var wasTempAsset = ExternalFileDragDrop.tempAssetDragged;
                var assetRef = Unity.AI.Generators.Asset.AssetReferenceExtensions.FromObject(evt.newValue);
                if (wasTempAsset)
                {
                    ExternalFileDragDrop.EndDragFromExternalPath();

                    byte[] data;
                    {
                        await using var stream = await assetRef.GetCompatibleImageStreamAsync();
                        data = await stream.ReadFullyAsync();
                    }
                    objectField.SetValueWithoutNotify(null); // force the field to be empty to not hold a reference to the temp asset
                    e.Dispatch(GenerationSettingsActions.setImageReferenceAsset, new (e.type, null));
                    e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Doodle));
                    e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, data));

                    ExternalFileDragDrop.CleanupDragFromExternalPath();
                }
                else
                {
                    e.Dispatch(GenerationSettingsActions.setImageReferenceAsset, new (e.type, assetRef));
                    e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Asset));
                    e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, null));
                }
            });

            objectField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Backspace && evt.actionKey) || evt.keyCode == KeyCode.Delete)
                {
                    var isClear = e.GetState().SelectImageReferenceIsClear(e, e.type);
                    if (!isClear) Clear();
                }
            });

            strengthSlider?.RegisterValueChangedCallback(evt => {
                e.Dispatch(GenerationSettingsActions.setImageReferenceStrength, new (e.type, e.invertStrength ? 1 - evt.newValue / 100.0f : evt.newValue / 100.0f));
            });
            deleteImageReference.clicked += () => {
                e.Dispatch(GenerationSettingsActions.setImageReferenceSettings, new (e.type, new TImageReferenceState()));
            };
            editButton.clicked += Edit;
            settingsButton.clicked += () => ShowMenu();
            imageReferenceObjectField.RegisterCallback<ContextClickEvent>(_ => ShowMenu(true));

            var wasDoodle = false;
            imageReferenceObjectField.RegisterCallback<DragEnterEvent>(_ => {
                wasDoodle = e.GetState().SelectImageReferenceMode(e, e.type) == ImageReferenceMode.Doodle;
                e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Asset));
            });
            imageReferenceObjectField.RegisterCallback<DragLeaveEvent>(_ =>
                e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, wasDoodle ? ImageReferenceMode.Doodle : ImageReferenceMode.Asset)));

            // State changes
            e.Use(state => state.SelectImageReferenceAsset(e, e.type), asset => objectField.SetValueWithoutNotify(asset.GetObject()));
            if (strengthSlider != null)
            {
                e.Use(state => state.SelectImageReferenceStrength(e, e.type), strength =>
                {
                    strengthSlider.SetValueWithoutNotify(
                        Mathf.RoundToInt((e.invertStrength ? 1 - strength : strength) * 100));
                });
            }
            e.Use(state => state.SelectImageReferenceIsActive(e, e.type), isActive => {
                e.EnableInClassList("hide-image-reference", !isActive);
            });
            e.Use(state => state.SelectSelectedModel(e)?.id, _ => {
                if (!e.GetState().SelectImageReferenceAllowed(e, e.type))
                    e.schedule.Execute(() => e.Dispatch(GenerationSettingsActions.setImageReferenceActive, new (e.type, false)));
            });

            e.Use(DoodleWindowSelectors.SelectState, s =>
            {
                if (e.GetContext<ExternalDoodleEditor>(ExternalDoodleEditor.key) is {enabled: true})
                    return;
                var selected = s.assetReference == e.GetAsset() && s.imageReferenceType == e.type;
                e.SetSelected(selected);
            });
            e.UseArray(state => state.SelectImageReferenceDoodle(e, e.type), image => doodlePad.SetValueWithoutNotify(image as byte[]));
            e.Use(state => state.SelectImageReferenceMode(e, e.type), mode =>
            {
                imageReferenceObjectField.RemoveFromClassList(ImageReferenceMode.Asset.ToString().ToLower());
                imageReferenceObjectField.RemoveFromClassList(ImageReferenceMode.Doodle.ToString().ToLower());
                imageReferenceObjectField.AddToClassList(mode.ToString().ToLower());

                if (mode == ImageReferenceMode.Doodle)
                    objectField.SetValueWithoutNotify(null); // force the field to be empty to not hold a reference to the previous asset
            });

            e.Use(state => state.SelectSelectedModelOperationIsValid(e, e.type.GetOperationSubTypeEnumForType().FirstOrDefault()), isValid =>
            {
                var isActive = e.GetState().SelectImageReferenceIsActive(e, e.type);
                if (isActive)
                {
                    unsupportedModelOperationIconContainer?.EnableInClassList("hidden", isValid);
                    unsupportedModelOperationBackground?.EnableInClassList("hidden", isValid);
                }
            });

            e.Use(state => state.SelectImageReferenceIsActive(e, e.type), isActive =>
            {
                if (isActive)
                {
                    var isValid = e.GetState().SelectSelectedModelOperationIsValid(e, e.type.GetOperationSubTypeEnumForType().FirstOrDefault());
                    unsupportedModelOperationIconContainer?.EnableInClassList("hidden", isValid);
                    unsupportedModelOperationBackground?.EnableInClassList("hidden", isValid);
                }
            });

            return;

            void ShowMenu(bool isContextClick = false)
            {
                var menu = new GenericMenu();

                if (isContextClick && e.allowEdit)
                {
                    menu.AddItem(new GUIContent("Edit"), false, Edit);
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Import from Project"), false, () => objectField.ShowObjectPicker());
                }
                menu.AddItem(new GUIContent("Import from disk"), false, () =>
                {
                    var path = EditorUtility.OpenFilePanel("Import image", "", "png,jpg,jpeg");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var data = FileIO.ReadAllBytes(path);
                        e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Doodle));
                        e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, data));
                    }
                });
                menu.AddSeparator("");
                var isClear = e.GetState().SelectImageReferenceIsClear(e, e.type);
                if (!isClear)
                    menu.AddItem(new GUIContent("Clear"), false, Clear);
                else
                    menu.AddDisabledItem(new GUIContent("Clear"));

                var canPaste = EditorGUIUtility.systemCopyBuffer.StartsWith("MetadataDoodleBytes:") ||
                    EditorGUIUtility.systemCopyBuffer.StartsWith("MetadataAssetRef:");
                if (canPaste)
                    menu.AddItem(new GUIContent("Paste"), false, TryPaste);
                else
                    menu.AddDisabledItem(new GUIContent("Paste"));

                if (isContextClick)
                    menu.ShowAsContext();
                else
                    menu.DropDown(settingsButton.worldBound);
            }

            void Clear()
            {
                var previousValue = doodlePad.value;
                doodlePad.value = null;
                var imgRefDoodle = doodlePad.value;
                var window = DoodleWindow.GetWindow(e.GetAsset(), e.type);
                if (window)
                {
                    var layers = new [] { new DoodleLayer { data = imgRefDoodle }};
                    if (!window.ResetData(layers))
                    {
                        // the reset operation has been canceled, revert changes
                        doodlePad.value = previousValue;
                        return;
                    }
                    // close the window if the reset operation was successful for a better flow,
                    // since we are going to switch to the asset mode
                    window.Close();
                }

                e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Asset));
                e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, null));
                e.Dispatch(GenerationSettingsActions.setImageReferenceAsset, new (e.type, null));
                // also set the asset to None for consistency
                objectField.value = null;
            }

            void TryPaste()
            {
                try
                {
                    var buffer = EditorGUIUtility.systemCopyBuffer;
                    const string doodlePrefix = "MetadataDoodleBytes:";
                    const string assetPrefix = "MetadataAssetRef:";

                    if(buffer.StartsWith("MetadataDoodleBytes:"))
                    {
                        var imageString = buffer.Substring(doodlePrefix.Length);
                        var bytes = Convert.FromBase64String(imageString);
                        e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Doodle));
                        e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, bytes));
                    }
                    else if(buffer.StartsWith("MetadataAssetRef:"))
                    {
                        var imageGuid = buffer.Substring(assetPrefix.Length);
                        var assetPath = AssetDatabase.GUIDToAssetPath(imageGuid);
                        var assetRef = Unity.AI.Generators.Asset.AssetReferenceExtensions.FromPath(assetPath);

                        e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Asset));
                        e.Dispatch(GenerationSettingsActions.setImageReferenceAsset, new (e.type, assetRef));
                    }
                }
                catch
                {
                    Debug.LogError("Invalid data");
                }
            }

            async void Edit()
            {
                if (e.GetContext<ExternalDoodleEditor>(ExternalDoodleEditor.key) is {enabled: true})
                    return;
                var state = e.GetState();
                var imgRefAsset = state.SelectImageReferenceAsset(e, e.type);
                var imgRefDoodle = state.SelectImageReferenceDoodle(e, e.type);
                var mode = state.SelectImageReferenceMode(e, e.type);
                byte[] data;
                if (mode == ImageReferenceMode.Doodle)
                    data = imgRefDoodle;
                else if (imgRefAsset.IsValid())
                {
                    await using var stream = await imgRefAsset.GetCompatibleImageStreamAsync();
                    data = await stream.ReadFullyAsync();
                }
                else
                    data = null;
                var windowArgs = new DoodleWindowArgs(e.GetAsset(), e.type, data, doodlePad.GetDoodleSize(), e.showBaseImageByDefault);
                if (DoodleWindow.Open(windowArgs) is {hasUnsavedChanges: false})
                {
                    e.Dispatch(GenerationSettingsActions.setImageReferenceMode, new (e.type, ImageReferenceMode.Doodle));
                    e.Dispatch(GenerationSettingsActions.setImageReferenceDoodle, new (e.type, data));
                }
            }
        }
    }
}
