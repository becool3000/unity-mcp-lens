---
uid: spritesheet-generation
---

# Create a spritesheet with Sprite Generator

Generate an animation-ready spritesheet from an AI-generated image with Sprite Generator.

When you create a spritesheet manually, it gives you direct control over the motion prompt, background removal, and generation steps. You can generate spritesheets directly from a source sprite and preview the resulting video before using it in your scene.

To generate a spritesheet, follow these steps:

1. [Generate a source image.](#generate-the-source-image)
2. [Generate the spritesheet from the source image.](#generate-the-spritesheet)

## Generate the source image

To create the base image, follow these steps:

   1. In the Unity Editor, select **AI** > **Generate New** > **Sprite**.
      The Sprite Generator opens.
   2. Select the **Generate** tab.
   3. Enter a prompt that describes your object or character.
   4. Select **Generate**.
   5. In the **Remove BG** tab, review the **Base Image**, and then select **Remove BG** to remove the background.

## Generate the spritesheet

To generate the animation and spritesheet, follow these steps:

   1. Select the **Spritesheet** tab.
   2. In the **Prompt** field, specify a motion type. For example: `Turntable`.
   3. Provide a reference image that acts as the first frame of the generated spritesheet.
   4. Select **Spritesheet**.

   The generator creates the following:
   - A 5 s animation video labeled in the Generations panel.
   - A corresponding spritesheet in the **Inspector** window.
   - The video is saved as an `.mp4` file in your project’s **GeneratedAssets** folder.

To use the generated spritesheet, select the spritesheet in the **Project** window and drop it into the **Scene**. When prompted, create and save an animation clip (`.anim`).

If your animation contains a background, open the Sprite Generator again and use the **Remove BG** tab to remove the background.

## Additional resources

* [Introduction to spritesheet](xref:spritesheet-overview)
* [Generate and use a spritesheet with Assistant](xref:spritesheet-assistant)