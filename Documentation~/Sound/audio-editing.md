---
uid: audio-editing
---

# Edit an audio clip

Modify an audio clip by removing silence, trimming time ranges, adjusting volume, or creating seamless loops. Assistant creates a new audio clip for each operation so your original file remains unchanged.

## Prerequisites

To modify an audio clip with Assistant:

- Install the Unity AI [Generators](xref:ai-menu-access) package.
- Import the audio clip into your Unity project.
- Open the **Assistant** window.

## Modify an audio clip

Attach your audio once, then provide the instructions you need.

To edit an audio:

1. Attach the original audio clip to the Assistant text field.
2. Enter an instruction for one of the available edit types:

   **Remove silence**
   - `Remove the silence at the start and end of this clip`.
   - `Clean this audio by trimming silence from both ends`.
   - `Cut the silent sections so the clip starts immediately`.

   **Trim a time range**
   - `Trim this clip from 00:03 to 00:07`.
   - `Extract the segment between 12 seconds and 18 seconds`.

   **Adjust volume**
   - `Increase the volume of this clip by 50%`.
   - `Reduce the volume by half`.

   **Create a seamless loop**
   - `Create a seamless loop from this clip`.
   - `Remove silence and then loop this audio`.

3. Submit the prompt.
4. Allow Assistant to generate a new audio asset with the requested modification.

Assistant creates a new audio clip in your project that reflects the operation you requested.

## Additional resources

- [Edit audio with Assistant](xref:audio-edit-landing)
- [Introduction to editing audio clips](xref:audio-edit-overview)
