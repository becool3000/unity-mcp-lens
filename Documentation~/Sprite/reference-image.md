---
uid: reference-sprite
---

# Create a sprite from a reference image

You can use reference images to guide the sprite generation process. Each reference type influences the output differently. A reference image can be an image in your **Project** window, in the **Scene** view, or in your own generated images.

To use reference images for sprite generation, follow these steps:

1. In the **Generate** window, select **Add More Controls to Prompt**.
2. In the **Select which operator to Add** window, review the available reference types. Each type includes a description and preview:

   * **Image Reference**: uses the reference image to define a source image to modify.
   * **Style Reference**: conditions the generated sprite to follow the artistic style of the reference image.
   * **Composition Reference**: influences the spatial arrangement of elements in the generated sprite.
   * **Depth Reference**: adds depth information to the generated sprite based on the reference image.
   * **Line Art Reference**: conditions the output sprite to follow the line art style of the reference image.
   * **Feature Reference**: matches specific visual details from the reference image, such as color patterns or shapes, to the generated sprite.

3. Select the reference style you want to use. The chosen style appears in the **Generate** window.
4. In the reference section, select the browse icon to open the **Select Texture 2D** window.
5. Select a reference image from the **Assets** tab.
6. Adjust the **Strength** slider to control how much the reference image influences the generated sprite.
7. Select **Generate**.

## Additional resources

* [Create a sprite from a prompt](xref:generate-sprite)
* [Manage sprites](xref:manage-sprite)
* [Edit a sprite](xref:modify-sprite)