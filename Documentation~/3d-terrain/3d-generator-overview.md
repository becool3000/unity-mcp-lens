---
uid: 3d-generator-overview
---

# Create 3D objects

Generate 3D objects from a reference image or text prompt and add them to your Unity project.

Use the 3D Object Generator to create a three-dimensional (3D) mesh that you can place and use in a Scene. This workflow is best suited for simple, single-part props, such as household items, environment details, or decorative objects.

The generated 3D objects have the following characteristics:

- Can be viewed from all sides.
- Don't contain subdivisions or separate moving parts.
- Not rigged or animated.

You can generate 3D objects either directly from the Generators window for more control or use Assistant that guides the process with prompts and project context. In both cases, the result is a prefab that contains a mesh and materials.

> [!NOTE]
> For higher visual quality and more predictable results, use a reference image.

## Prerequisites

Before you start, make sure you meet the following prerequisites:

- A Unity project with the **Unity AI** package installed.
- A Scene open in the Unity Editor.

## Generate a 3D object with Generators

Use the 3D Object Generator to control the generation flow or reuse an existing image.

To generate a 3D object from a reference image, follow these steps:

1. In the Unity Editor, select **AI** > **Generate New** > **3D Object**.

   A new mesh file is created in the **Assets** folder.

1. Double-click the file to open the 3D Object Generator.
1. (Optional) Select **Change** to choose an AI model.

      By default, 3D Object Generator uses the **Hunyuan 3D 3.0 (Pro)** model.
1. Provide a reference image by doing one of the following:
   - Select an image already in your project.
   - Import an image from disk.
   - Generate an image using another Generator.
1. Drag the reference image to **Image Reference**.
1. Select **Generate**.

The Generator creates a 3D object prefab that you can use in your Scene.

## Generate a 3D object with Assistant

Use Assistant to generate a 3D object by describing what you want in natural language. For example, `Create a 3D model of a cereal box.`.

You can generate 3D objects from:

- Text only
- A reference image that you include in the conversation

If you don’t provide an image, Assistant first generates a reference image and then uses it to create the 3D object. When an image is provided, Assistant uses it directly and handles any required preprocessing, such as background removal, before generating the 3D object.

Unity saves the generated 3D object in the `Assets` folder as a prefab and you can add it to the Scene like any other asset.

## Additional resources

* [About Generators](xref:overview)
* [Generators integration with Assistant](xref:generator-assistant-landing)