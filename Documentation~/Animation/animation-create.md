---
uid: animation-create
---

# Create an animation clip

Use **Animation Generator** to create animation from scratch with a text prompt.

There are two methods to generate an animation:

* [Text to Motion](#text-to-motion)
* [Video to Motion](#video-to-motion)

## Text to Motion

Use **Text to Motion** to generate an animation using a text prompt.

1. Right-click an empty area in the **Project** window to open Animation Generator.
1. Select **Create** > **Animation** > **Generate Animation Clip**.
1. Select the **Text to Motion** tab.
1. To choose a model, select **Change**.
1. Select a model from the **Select Model** window.
1. In the **Prompt** field, describe the animation you want to generate.

   For example, `walk` or `jump`.

1. Use the **Duration** slider to specify the length of the generated animation in seconds.
1. To specify a custom seed to generate consistent results, enable **Custom Seed** and enter a seed number.

    For more information on custom seed, refer to [Use a custom seed to generate consistent sprites](xref:custom-seed).
1. Select **Generate**.

   > [!TIP]
   > Use the **Trim** tab to adjust the animation's direction, position, and looping behavior after generation. For more information, refer to [Adjust direction and looping](xref:animation-trim).

## Video to Motion

Use **Video to Motion** to replicate complex or realistic movements from a reference video. This method captures detailed motions, such as martial arts or dance routines, directly from video footage.

1. Right-click an empty area in the **Project** window to open Animation Generator.
1. Select **Create** > **Animation** > **Generate Animation Clip**.
1. Select the **Video to Motion** tab.
1. To choose a model, select **Change**.
1. Select a model from the **Select Model** window.
1. To specify a custom seed to generate consistent results, enable **Custom Seed** and enter a seed number.
1. Select the browse icon to open the **Select Video Clip** window and select a video file from your Unity project folder.

   > [!TIP]
   > Drag the video into your project folder if it’s not already imported.

   > [!NOTE]
   > The video file must be in one of the supported Unity video formats. MP4 is the preferred format for best compatibility and performance. For more information, refer to [Unity video format compatibility](https://docs.unity3d.com/Manual/VideoSources-FileCompatibility.html).


1. Select **Generate**.

> [!NOTE]
> To generate and assign assets directly, refer to [Assign generated assets with the Object Picker](xref:asset-picker).

### Guidelines for Video to Motion

* **Only one person in the frame**: Place only one person in the frame, and ensure good contrast and that their feet are clearly visible and distinguishable from the ground.

* **Full body in frame**: Ensure the entire body and all the movements are visible in the video. The video starts with the person on the ground, facing the camera.

* **Stable camera**: Ensure the video is continuous, without cuts or zooms. Keep the camera stable by using a tripod or resting it on a stable surface.

## Generated animation clip

The generated animation appears in the **Generations** panel. Hover over the animation to play it and view details, such as the model used and prompt settings.

The animation is saved in the `Assets` folder as a `.anim` file. For example, `walk.anim`.

After generating an animation clip, you can modify or refine it with Unity's standard animation editing tools. For more information, refer to [Unity animation clips](https://docs.unity3d.com/Manual/AnimationClips.html).

> [!TIP]
> Use the **Trim** tab to refine your animation after generation. For more information, refer to [Adjust direction and looping](xref:animation-trim) and [Trim tab reference](xref:trim-reference).

## Unityai label

[!include[](../snippets/unityai-label.md)]

## Additional resources

* [Apply animation to a character](xref:animation-apply)
* [Use a custom seed to generate consistent sprites](xref:custom-seed)
* [Troubleshooting animation issues](xref:animation-troubleshoot)
* [Unity animation clips](https://docs.unity3d.com/Manual/AnimationClips.html)
* [Assign generated assets with the Object Picker](xref:asset-picker)