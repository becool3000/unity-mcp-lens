---
uid: reference-cubemap
---

# Create a cubemap from a reference image

Use a reference image to guide the generation of a cubemap.

Reference images define lighting, composition, and spatial arrangement for your cubemap. You can use assets from your **Project** window, Scene view, or previously generated images as references.

To generate a cubemap from a reference image, follow these steps:

1. In the **Generate** window, select **Add More Controls To Prompt**.
2. In the **Select which operator to Add** window, choose a reference type:

   * **Image Reference**: uses the reference image to define the base appearance.
   * **Composition Reference**: guides spatial arrangement of the scene elements.
   * **Depth Reference**: adds depth data for realistic environments.

3. In the reference section, select the browse icon to open the **Select Texture 2D** window.
4. Select a reference image from the **Assets** tab.
5. Adjust the **Strength** slider to control how much the reference image influences the generated cubemap.
6. Select **Generate**.

The new cubemap appears in the **Generations** panel with its reference data saved for reuse.

## Additional resources

* [Manage cubemaps](xref:manage-cubemap)
* [Edit a cubemap](xref:modify-cubemap)