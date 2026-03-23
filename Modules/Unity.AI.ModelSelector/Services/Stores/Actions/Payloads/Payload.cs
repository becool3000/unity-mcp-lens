namespace Unity.AI.ModelSelector.Services.Stores.Actions.Payloads
{
    record DiscoverModelsData(string environment);

    record FavoriteModelPayload(string modelId, bool isFavorite);
}
