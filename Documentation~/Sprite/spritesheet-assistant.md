---
uid: spritesheet-assistant
---

# Generate and use a spritesheet with Assistant

Use Assistant to generate a spritesheet from a text prompt and turn it into an animation in your scene. Assistant creates a source image, generates motion, and converts the result into a spritesheet that's ready for animation.

To generate a spritesheet, follow these steps:

1. In the Unity Editor menu, select **AI** > **Open Assistant**.

   The **Assistant** window opens.
2. In the text field, enter a prompt to describe the object or character you want to generate. For example, `Generate the image of a human statue.`.
3. Submit the prompt.
   Assistant generates the asset and displays a preview in the conversation panel.
4. If the image includes a background, enter a prompt to remove it and isolate the asset. For example, `Remove the background`.
5. Enter a prompt to generate the spritesheet animation. For example, `Create a spritesheet of the statue prompting it to raise its hands.`.
   Assistant generates the animation and converts it into a spritesheet, which is saved as a sprite of type "multiple" in the `Assets` folder. You can now create an animation clip for use in your scene.
6. Enter a prompt to add the spritesheet to your scene. For example, `Use the spritesheet to make an animation clip and add the resulting animated sprite to my scene.`.

   Assistant uses tools such as `Unity.ConvertSpriteSheetToAnimationClip` and `Unity.CreateAnimationControllerFromClip` to generate the animation clip and controller automatically.

7. In the Unity Editor, select **Play** to preview the animation.

## Tips for better result

Follow these suggestions for better results:

* Use simple and precise prompts when requesting animation generation. Overly detailed or complex prompts may produce unexpected results. For example, use `turntable` instead of `create a turntable animation of the statue rotating 360 degrees`.

* Review the generated prompt if the animation doesn't match your expectations. You can inspect this generated prompt in the Sprite Generator window or in the chat to understand what was actually sent to the animation model. If needed, revise your instruction to Assistant with a simpler or more specific prompt.

* Remove the background first for the best results when generating spritesheet animations. Alternatively, you can request a uniform background when generating your reference image.

## Additional resources

- [Introduction to spritesheet](xref:spritesheet-overview)
- [Create a spritesheet with Sprite Generator](xref:spritesheet-generation)