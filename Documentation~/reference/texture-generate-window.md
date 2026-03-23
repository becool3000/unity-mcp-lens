---
uid: generate-window
---

# Texture creation window reference

The **Generate** window enables you to generate a new Texture2D asset or modify an existing Texture2D asset using generative models. When you select a generated or modified asset in this window, Unity applies it to the corresponding Texture2D Asset Importer in your project.

The **Generate** window shows the mapping between the `Project/Generated Assets` folder and the `Project/Assets` folder.

To generate or modify an asset, provide input text prompts and (optional) reference images. You can then import the generated or modified asset into your project's Texture2D asset for use in a game.

Generated assets are saved as `.png` files by default. The Unity Texture2D Asset Importer supports importing `.png`, `.jpg`, and `.exr` files to let you to work with different file formats as needed.

The following table describes the options available on the **Generate** window:

| Option | Description |
| ------ | ----------- |
| **Prompt** | Enter a text description of the asset you want to create. |
| **Negative prompt** | Enter elements you want to exclude from the asset. |
| **Model selection** | Select **Change** to choose the AI model used for asset generation. |
| **Reference options** | Select **Add More Controls to Prompt** to define how reference images influence the generated asset. |
| **Images** | Set the number of images to generate. |
| **Dimensions** | Set the size of the asset output. |
| **Custom seed** | Set a fixed seed for consistent asset generation. |
| **Generate** | Generate the asset. |

For more information, refer to [Create a Texture2D asset from a prompt](xref:generate-texture2d).

## Additional resources

* [Create a Texture2D asset from a prompt](xref:generate-texture2d)
* [Create a texture from a reference image](xref:reference)
* [Manage textures](xref:manage)