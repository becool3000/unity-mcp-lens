---
uid: reference
---

# Create a texture from a reference image

You can use reference images to guide the image generation process. Each reference type influences the output differently. A reference image can be an image in your **Project** window, in the **Scene** view, or in your own generated images.

To use reference images for Texture2D asset generation, follow these steps:

1. In the **Generate** window, select **Add More Controls to Prompt**.
1. Choose from the following reference types:

   * **Image Reference**: uses the reference image to define a source image to modify.
   * **Style Reference**: conditions the generated image to follow the artistic style of the reference image.
   * **Composition Reference**: influences the spatial arrangement of elements in the generated image.
   * **Depth Reference**: adds depth information to the generated image based on the reference image.
   * **Line Art Reference**: conditions the output image to follow the line art style of the reference image.
   * **Feature Reference**: matches specific visual details from the reference image, such as color patterns or shapes, to the generated image.

1. In the reference section, select the browse icon to open the **Select Texture 2D** window.
1. Select a reference image from the **Assets** tab.
1. Adjust the **Strength** slider to control how much the reference image influences the generated image.
1. Select **Generate**.

## Additional resources

* [Create a Texture2D asset from a prompt](xref:generate-texture2d)
* [Manage textures](xref:manage)
* [Edit a texture](xref:modify)