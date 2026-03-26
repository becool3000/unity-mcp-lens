using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [FunctionCallRenderer(typeof(MultiAngleSceneViewTool), nameof(MultiAngleSceneViewTool.CaptureMultiAngleSceneView))]
    class MultiAngleSceneViewFunctionCallElement : VisualElement, IFunctionCallRenderer, IAssistantUIContextAware
    {
        public string Title => "Capture Multi-Angle Scene View";
        public string TitleDetails { get; private set; }
        public bool Expanded => true;
        public AssistantUIContext Context { get; set; }

        VisualElement m_ImageContainer;

        Texture2D m_PreviewTexture;

        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = string.Empty;

            m_ImageContainer = new VisualElement();
            Add(m_ImageContainer);
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            JObject jsonResult = null;
            try
            {
                jsonResult = result.Result is JObject jObj ? jObj : JObject.Parse(result.Result?.ToString() ?? "{}");
            }
            catch
            {
                m_ImageContainer.Add(new Label("Invalid response format."));
                return;
            }

            var imageUrl = jsonResult["sas_url"]?.ToString() ?? jsonResult["url"]?.ToString();

            if (string.IsNullOrEmpty(imageUrl))
            {
                m_ImageContainer.Add(new Label("No image URL available."));
                return;
            }

            var width = jsonResult["width"]?.ToObject<int>() ?? 0;
            var height = jsonResult["height"]?.ToObject<int>() ?? 0;
            DisplayScreenshot(imageUrl, width, height);
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            m_ImageContainer.Add(new Label($"Multi-angle scene view capture failed: {error}"));
        }

        async void DisplayScreenshot(string imageUrl, int width, int height)
        {
            var loadingLabel = new Label("Loading multi-angle scene view...");
            loadingLabel.AddToClassList("screenshot-preview-loading");
            m_ImageContainer.Add(loadingLabel);

            m_PreviewTexture = await DownloadImageAsync(imageUrl, width, height);

            loadingLabel.RemoveFromHierarchy();

            if (m_PreviewTexture == null)
            {
                m_ImageContainer.Add(new Label("Failed to load multi-angle scene view."));
                return;
            }

            var image = new Image
            {
                image = m_PreviewTexture,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("screenshot-preview-image");
            m_ImageContainer.Add(image);
        }

        static async Task<Texture2D> DownloadImageAsync(string url, int width, int height)
        {
            using var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
                return null;

            var texture = new Texture2D(width > 0 ? width : 2, height > 0 ? height : 2);
            return texture.LoadImage(request.downloadHandler.data) ? texture : null;
        }
    }

    [FunctionCallRenderer(typeof(Capture2DSceneTools), nameof(Capture2DSceneTools.Capture2DScene))]
    class Capture2DSceneFunctionCallElement : VisualElement, IFunctionCallRenderer, IAssistantUIContextAware
    {
        public string Title => "Capture 2D Scene";
        public string TitleDetails { get; private set; }
        public bool Expanded => true;
        public AssistantUIContext Context { get; set; }

        VisualElement m_ImageContainer;

        Texture2D m_PreviewTexture;

        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = string.Empty;

            m_ImageContainer = new VisualElement();
            Add(m_ImageContainer);
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            JObject jsonResult = null;
            try
            {
                jsonResult = result.Result is JObject jObj ? jObj : JObject.Parse(result.Result?.ToString() ?? "{}");
            }
            catch
            {
                m_ImageContainer.Add(new Label("Invalid response format."));
                return;
            }

            var imageUrl = jsonResult["sas_url"]?.ToString() ?? jsonResult["url"]?.ToString();

            if (string.IsNullOrEmpty(imageUrl))
            {
                m_ImageContainer.Add(new Label("No image URL available."));
                return;
            }

            var width = jsonResult["width"]?.ToObject<int>() ?? 0;
            var height = jsonResult["height"]?.ToObject<int>() ?? 0;
            DisplayScreenshot(imageUrl, width, height);
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            m_ImageContainer.Add(new Label($"Scene region capture failed: {error}"));
        }

        async void DisplayScreenshot(string imageUrl, int width, int height)
        {
            var loadingLabel = new Label("Loading scene region capture...");
            loadingLabel.AddToClassList("screenshot-preview-loading");
            m_ImageContainer.Add(loadingLabel);

            m_PreviewTexture = await DownloadImageAsync(imageUrl, width, height);

            loadingLabel.RemoveFromHierarchy();

            if (m_PreviewTexture == null)
            {
                m_ImageContainer.Add(new Label("Failed to load scene region capture."));
                return;
            }

            var image = new Image
            {
                image = m_PreviewTexture,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("screenshot-preview-image");
            m_ImageContainer.Add(image);
        }

        static async Task<Texture2D> DownloadImageAsync(string url, int width, int height)
        {
            using var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success)
                return null;

            var texture = new Texture2D(width > 0 ? width : 2, height > 0 ? height : 2);
            return texture.LoadImage(request.downloadHandler.data) ? texture : null;
        }
    }
}
