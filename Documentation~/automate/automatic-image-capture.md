---
uid: automatic-image-capture
---

# Capture screenshots automatically

Allow Assistant to capture screenshots within your Unity Editor when visual input provides a better response.

When you submit a text-only request that requires visual context, such as a lighting or material question, Assistant might request permission to capture images within your Unity Editor. Assistant might capture different types of visual information depending on what your request requires, but all automatic captures are limited to the Unity Editor and your project views.

This feature works as follows:

1. Enter a prompt that involves visual elements in Assistant's text field.

   For example, `Based on my scene’s look, what kind of lighting changes would you recommend to get an airport feel?`.
2. If Assistant determines that visual input might help, a request to capture an image appears.

3. Choose one of the following:
- **Allow**: Assistant takes a screenshot within your Unity Editor (not the entire desktop). When you grant permission, Assistant captures the visuals it needs and processes your request.

- **Don't Allow**: Assistant continues with your text prompt only.

> [!NOTE]
> Automatic visual tools only capture specific views within your Unity project or the entire Unity Editor view. Assistant never captures anything outside the Unity Editor window.

You can attach your own images manually at any time. For more information, refer to [Attach images to prompts](xref:attach-image).

## Additional resources

* [Examples: Guide Assistant with images and screenshots](xref:example-cube)
* [Annotate screenshots for Assistant questions](xref:annotation)
