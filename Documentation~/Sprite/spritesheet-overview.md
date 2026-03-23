---
uid: spritesheet-overview
---

# Introduction to spritesheet

Learn how Unity generates animation-ready spritesheets from AI-created images.

A spritesheet is a single texture that contains multiple animation frames arranged in a grid. Each frame represents a moment in time. When played sequentially, these frames create a 2D animation, such as a character action or a rotating object.

A single static image doesn’t contain enough visual information to create motion. For example, a front-facing image doesn’t show the back or sides of an object. To solve this, Unity first generates a short animation video, typically a 5-second turntable-style motion. It then samples evenly spaced frames from that video and arranges them into a 4 × 4 grid, resulting in 16 frames total.

Unity saves the generated animation video as an `.mp4` file in the `GeneratedAssets` folder and creates a corresponding spritesheet texture that you can use to generate an animation clip.

You can generate spritesheets manually using the [Sprite Generator](xref:spritesheet-generation) or automatically with [Assistant](xref:spritesheet-assistant).

Unity creates spritesheets in the following three stages:

1. Source image generation: Create or provide an image that acts as the first animation frame.
2. Motion generation: Generate a short animation video, such as a turntable rotation.
3. Frame extraction and layout: Sample evenly spaced frames and arrange them in a grid.

## Additional resources

- [Create a spritesheet with Sprite Generator](xref:spritesheet-generation)
- [Generate and use a spritesheet with Assistant](xref:spritesheet-assistant)