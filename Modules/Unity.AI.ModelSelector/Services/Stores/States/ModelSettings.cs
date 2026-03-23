using System;
using System.Collections.Generic;
using System.Linq;
using AiEditorToolsSdk.Components.Common.Enums;
using UnityEngine;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    [Serializable]
    record ImageDimensions
    {
        public int width;
        public int height;
    }

    [Serializable]
    record ModelSettings
    {
        public string id;
        public string name;
        public List<string> tags = new();
        public string description;
        public List<string> thumbnails = new();
        public string icon;
        public string provider = ModelConstants.Providers.None;
        public string modality = ModelConstants.Modalities.None;
        public List<string> operations = new();
        public ImageDimensions nativeResolution = new() { width = 1024, height = 1024 };
        public List<ImageDimensions> imageSizes = new[]{ new ImageDimensions { width = 1024, height = 1024 } }.ToList();
        public string baseModelId;
        public bool isFavorite;
        public bool favoriteProcessing;
        public bool isCustom;
        public ModelTypeEnum modelType;
        public List<string> capabilities = new();
        public List<string> limitations = new();
    }
}
