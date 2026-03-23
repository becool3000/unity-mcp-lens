---
uid: model-picker
---

# Choose the right model with the Model Picker

Use the Model Picker to select the artificial intelligence (AI) model you want to use to generate the 2D assets. Each model is trained to produce a specific style. The generators use this model to interpret your prompt and reference images.

The Model Picker is available in the [Sprite Generator](xref:sprite-overview) and [Texture2D Generator](xref:texture2d-overview) windows. Other generators, such as [Material Generator](xref:material-overview), [Sound Generator](xref:sound-intro), and [Animation Generator](xref:animation-intro), use the default Unity model.

To select a model using the Model Picker, follow these steps:

1. In the **Generate** window, select **Change**.

   The **Select All Model** window shows all the models available for the current asset type (for example, Sprite or Texture2D). It also displays the name, type, and a visual preview of the model.

1. Use the filter options to narrow down the models:

   1. To find models that suit your asset type, such as **Characters** or **Props**, use the options in the **Tags** section.
   1. Reorder the model list with the **Sort** options: **Recently Used**, **Alphabetical**, **Reverse Alphabetical**.

1. (Optional) To mark a model as a favorite, move your pointer over the model thumbnail and select the white star in the top-right corner. The star turns yellow when marked as favorite.

    ![Select AI Model window with models, one marked as a favorite with a yellow star](../Images/favorite.png)

   1. To view only your favorites, select **Favorites** from the **Model** section.
   2. To remove a favorite, deselect the yellow star.

After you select a model, it shows in the **Generate** window.

## Additional resources

* [Create a sprite from a prompt](xref:generate-sprite)
* [Create a Texture2D asset from a prompt](xref:generate-texture2d)
* [Create a sound clip from a prompt](xref:sound-prompt)
* [Create a material from a prompt](xref:material-generate-prompt)