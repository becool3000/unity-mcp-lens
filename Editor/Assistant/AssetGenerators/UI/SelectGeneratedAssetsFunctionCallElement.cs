using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    [FunctionCallRenderer(SelectGeneratedAssetsTool.ToolName)]
    class SelectGeneratedAssetsFunctionCallElement : VisualElement, IFunctionCallRenderer
    {
        public string Title => "Select Generated Assets";
        public string TitleDetails { get; private set; }
        public bool Expanded => true;

        string[] m_AllPaths;
        string m_Title;
        string m_ButtonText = "Confirm Selection";
        int m_CostPerAsset = 0;

        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = string.Empty;

            if (functionCall.Parameters != null)
            {
                if (functionCall.Parameters.TryGetValue("title", out var titleToken))
                {
                    m_Title = titleToken.ToString();
                }

                if (functionCall.Parameters.TryGetValue("buttonText", out var buttonTextToken))
                {
                    m_ButtonText = buttonTextToken.ToString();
                }

                if (functionCall.Parameters.TryGetValue("costPerAsset", out var costToken))
                {
                    if (int.TryParse(costToken.ToString(), out var cost))
                    {
                        m_CostPerAsset = cost;
                    }
                }

                if (functionCall.Parameters.TryGetValue("assetPaths", out var pathsToken))
                {
                    m_AllPaths = pathsToken.ToObject<string[]>();
                    if (m_AllPaths != null && m_AllPaths.Length > 0)
                    {
                        BuildUI(m_AllPaths, null, functionCall.CallId);
                    }
                }
            }
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            TitleDetails = string.Empty;
            Clear(); // Remove pending UI

            try
            {
                var output = result.GetTypedResult<SelectAssetsOutput>();

                // Build UI with all paths if available, otherwise just use the selected ones
                BuildUI(m_AllPaths ?? output.SelectedPaths, output.SelectedPaths);
            }
            catch (Exception ex)
            {
                var label = new Label($"Error parsing result: {ex.Message}");
                Add(label);
            }
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            TitleDetails = "Error";
            Clear();
            Add(new Label($"Error: {error}"));
        }

        void BuildUI(string[] allPaths, string[] selectedPaths, Guid? callId = null)
        {
            var uxmlPath = "Packages/com.unity.ai.assistant/Editor/Assistant/AssetGenerators/UI/SelectGeneratedAssetsInteraction.uxml";
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }

            var ussPath = "Packages/com.unity.ai.assistant/Editor/Assistant/AssetGenerators/UI/SelectGeneratedAssetsInteraction.uss";
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            var header = this.Q<Label>(className: "select-assets-header");
            if (header != null)
            {
                if (selectedPaths == null)
                {
                    header.text = string.IsNullOrEmpty(m_Title) ? "Please select the generated assets to keep:" : m_Title;
                }
                else
                {
                    header.text = "Selected Assets:";
                }
            }

            var confirmBtn = this.Q<Button>("confirm-button");
            var confirmBtnText = this.Q<Label>("confirm-button-text");
            var pointsIndicator = this.Q<Label>("points-indicator");

            if (confirmBtnText != null)
            {
                confirmBtnText.text = m_ButtonText;
            }

            void UpdatePointsDisplay(int selectedCount)
            {
                if (pointsIndicator != null)
                {
                    if (m_CostPerAsset <= 0 || selectedCount == 0)
                    {
                        pointsIndicator.AddToClassList("points-indicator-hidden");
                    }
                    else
                    {
                        pointsIndicator.RemoveFromClassList("points-indicator-hidden");
                        pointsIndicator.text = (m_CostPerAsset * selectedCount).ToString();
                    }
                }
            }

            if (confirmBtn != null)
            {
                if (selectedPaths != null)
                {
                    // Hide button if already completed
                    confirmBtn.style.display = DisplayStyle.None;
                }
            }

            var grid = this.Q<VisualElement>("asset-container");
            if (grid != null && allPaths != null)
            {
                var selectedSet = selectedPaths != null ? new HashSet<string>(selectedPaths) : null;
                var currentSelection = new HashSet<string>();

                if (selectedPaths == null && allPaths.Length > 0)
                {
                    // Interactive mode: select only the first valid path by default
                    var firstValid = allPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p));
                    if (firstValid != null)
                    {
                        currentSelection.Add(firstValid);
                    }
                }

                foreach (var path in allPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;

                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    var item = new VisualElement();
                    item.AddToClassList("asset-item");

                    if (tex != null)
                    {
                        var img = new Image { image = tex };
                        img.AddToClassList("asset-image");
                        item.Add(img);
                    }

                    var lbl = new Label(System.IO.Path.GetFileName(path));
                    lbl.AddToClassList("asset-label");
                    item.Add(lbl);

                    bool isSelected = false;
                    if (selectedPaths != null)
                    {
                        isSelected = selectedSet != null && selectedSet.Contains(path);
                    }
                    else
                    {
                        isSelected = currentSelection.Contains(path);
                    }

                    if (isSelected)
                    {
                        item.AddToClassList("asset-item--selected");
                    }

                    if (selectedPaths == null)
                    {
                        // Interactive mode
                        var currentPath = path;
                        item.RegisterCallback<ClickEvent>(evt =>
                        {
                            if (currentSelection.Contains(currentPath))
                            {
                                currentSelection.Remove(currentPath);
                                item.RemoveFromClassList("asset-item--selected");
                            }
                            else
                            {
                                currentSelection.Add(currentPath);
                                item.AddToClassList("asset-item--selected");
                            }
                            UpdatePointsDisplay(currentSelection.Count);
                        });
                    }

                    var contextPath = path;
                    item.AddManipulator(new ContextualMenuManipulator((ContextualMenuPopulateEvent evt) =>
                    {
                        evt.menu.AppendAction("Select in Project", (x) =>
                        {
                            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(contextPath);
                            if (obj != null)
                            {
                                UnityEditor.Selection.activeObject = obj;
                                EditorGUIUtility.PingObject(obj);
                            }
                        });
                    }));

                    grid.Add(item);
                }

                UpdatePointsDisplay(currentSelection.Count);

                if (confirmBtn != null && callId.HasValue)
                {
                    confirmBtn.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (SelectGeneratedAssetsTool.PendingInteractions.TryGetValue(callId.Value, out var interaction))
                        {
                            interaction.CompleteInteraction(new SelectAssetsOutput { Completed = true, SelectedPaths = currentSelection.ToArray() });
                        }
                    });
                }
            }
        }
    }
}
