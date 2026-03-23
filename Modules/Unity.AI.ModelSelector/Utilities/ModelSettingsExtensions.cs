using System;
using AiEditorToolsSdk.Components.Common.Enums;
using Unity.AI.ModelSelector.Services.Stores.States;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    static class ModelSettingsExtensions
    {
        public static bool IsValid(this ModelSettings model) => model != null && !string.IsNullOrEmpty(model.id);

        public static bool IsBaseModel(this ModelSettings model)
        {
            if (!model.IsValid())
                return false;
            return model.modelType == ModelTypeEnum.Base;
        }

        public static bool MatchSearchText(this ModelSettings model, string searchText)
        {
            if (string.IsNullOrEmpty(searchText) || model == null)
                return true;

            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            if (model.name.IndexOf(searchText, comparison) >= 0)
                return true;
            if (model.description.IndexOf(searchText, comparison) >= 0)
                return true;
            if (model.provider.ToString().IndexOf(searchText, comparison) >= 0)
                return true;
            foreach (var tag in model.tags)
            {
                if (tag.IndexOf(searchText, comparison) >= 0)
                    return true;
            }
            return false;
        }
    }
}
