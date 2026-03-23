---
uid: example-cube
---

# Examples: Guide Assistant with images and screenshots

Learn how to attach images or Unity Editor screenshots to your prompt so the Assistant can interpret visual context and provide more accurate responses.

These examples show how to use image support to communicate visually when working on creative lighting or troubleshooting rendering issues. Use the first example to inspire your project’s look and feel, and the second to debug pink materials and shader errors.

## Create a lighting style from an inspiration image

Use image support to attach a reference or concept image that shows your desired lighting or mood. Assistant analyzes the image to suggest how you can recreate a similar atmosphere in your Unity scene.

To match your lighting to an inspiration image, follow these steps:

1. Open your Unity project and the **Assistant** window.
2. In the text prompt field, write a short request that describes your goal. For example, `How do I create this effect?`.
3. Choose and attach a lighting reference image from your computer. For more information, refer to [Attach images to prompts](xref:attach-image).
4. Press **Enter** on your keyboard or select the send icon to submit your question along with the image.

   Assistant analyzes and suggests changes to your scene lighting, post-processing, and skybox to match the color palette and tone of your reference image.

## Debug a pink material issue with a scene screenshot

Use image support to capture and send a screenshot of your Unity Editor when you encounter pink materials in your scene. This helps the Assistant detect missing or unsupported shaders and provide targeted fixes.

To identify and fix a pink material issue, follow these steps:

1. Open your scene in the Unity Editor and locate the pink object.
2. In the Assistant's text prompt field, type a question, such as `Why is my cube pink?`.
3. Select the camera button to capture a screenshot of your Editor view.

   The screenshot automatically attaches to your prompt.

4. Press **Enter** on your keyboard or select the send icon to submit your question along with the screenshot.

   Assistant analyzes the image, detects the issue, and responds with an advice.

## Additional resources

* [Use images and screenshots](xref:image-support)
* [Attach images to prompts](xref:attach-image)
* [Capture screenshots automatically](xref:automatic-image-capture)