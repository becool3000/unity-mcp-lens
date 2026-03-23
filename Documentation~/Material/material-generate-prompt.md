---
uid: material-generate-prompt
---

# Create a material from a prompt

Use Material Generator to generate a new material using a text-based prompt and refine it using reference images and material properties.

To generate a new material, follow these steps:

1. Right-click an empty area in the **Project** window.
1. Select **Create** > **Rendering** > **Generate Material**.

   The **New Material** window opens.
1. Select **Change** to open the **Select Model** window and choose a model.
1. Enter a text description of the material you want to create in the **Prompt** field.

   For example, `Wooden floor with natural grain`.

1. Enter elements you want to exclude from the generated material in the **Negative Prompt** field.

   For example, `No scratches or dirt`.

   For more information on negative prompts, refer to [Remove unwanted elements with negative prompts](xref:negative-prompt).

1. Use the **Materials** slider to set the number of variations of the material to generate.
1. Enable **Custom Seed** to generate consistent results.
1. Enter a seed number or let the tool generate one automatically.

    For more information on custom seed, refer to [Use a custom seed to generate consistent sprites](xref:custom-seed).
1. (Optional) In the **Pattern Reference** field, do one of the following:
    * Use built-in patterns

       1. Select **Patterns** to open the **Search** window.
       2. Enter a keyword and select a pattern from the search results.

          For example, if you want to generate a basket material to have a weave pattern, specify `basket` in the **Prompt** field and `weave` as the pattern in the **Search** window.

          The selected pattern appears in the **Pattern Reference** preview and guides the generation to follow a structured layout, such as brick, weave, or circle.

   * Use your own custom pattern

      1. Select the browse icon to open the **Select Texture 2D** window.
      2. Assign a texture from your **Assets** folder to use as a pattern reference.

          The custom pattern appears in the **Pattern Reference** preview and guides generation based on the selected pattern. For best results, use monochrome black and white patterns, which provide clearer structure for seamless material generation.

      > [!NOTE]
      > For best results, use the provided `pattern_##` textures, which are optimized for seamless tiling and enhance material quality.

2. Select **Generate** to create the material.

The generated material appears in the **Generations** panel. Hover over a material to view details like the model used and prompt settings.

> [!NOTE]
> To generate and assign assets directly, refer to [Assign generated assets with the Object Picker](xref:asset-picker).

> [!NOTE]
> Material Generator stores the generated material maps in the `/GeneratedAssets` folder located at the root of your project. These assets remain in that folder until you remove them manually.

## Work with generated material

The generated material appears in the **Inspector** window. The preview sphere and cube provide a live update as you modify textures, patterns, and settings.

* The sphere helps visualize how the material behaves on curved surfaces.
* The cube helps to evaluate for tiling and seamlessness, especially for patterns and structured materials.

The generated material includes a base map, which acts as a simple foundation. Use the [**PBR**](xref:material-pbr) tab to achieve advanced material properties for an extra layer of realism.

You can apply the material to your objects in the **Scene** view.

## Additional resources

* [Configure physically based material properties](xref:material-pbr)
* [Upscale material outputs](xref:material-upscale)
* [Use a custom seed to generate consistent sprites](xref:custom-seed)
* [Assign generated assets with the Object Picker](xref:asset-picker)