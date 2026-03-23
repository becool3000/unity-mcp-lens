---
uid: sound-prompt
---

# Create a sound clip from a prompt

Use the **Sound Generator** tool to create custom audio clips from scratch with a text prompt.

To generate audio from natural language prompts, follow these steps:

1. To open the **Generate New Audio Clip** window, right-click an empty area in the **Project** window.
1. Select **Create** > **Audio** > **Generate Audio Clip**.
1. To choose a model, select **Change** > **Text to Sound**.

   > [!NOTE]
   > Currently, only one AI model is available for sound generation. After you select it, it will remain the default model for future generations.

1. In the **Prompt** field, describe the sound effect you want to generate, such as `jungle ambiance` or `robotic beep`.

1. To exclude specific elements from the generated sound, enter keywords in the **Negative Prompt** field. For example, `no echo`.

   For more information on negative prompts, refer to [Remove unwanted elements with negative prompts](xref:negative-prompt).
1. Use the **Duration** slider to specify the length of the generated audio clip in seconds.

   The model’s reference duration is 10 seconds, which produces the best results.
1. Use the **Count** slider to specify the number of variations of the audio clip to generate in a single request.
1. To specify a custom seed to generate consistent results, enable **Custom Seed** and enter a seed number.

    For more information on custom seed, refer to [Use a custom seed to generate consistent sprites](xref:custom-seed).
1. Select **Generate**.

The generated audio clip appears in the **Generations** panel. Hover over the audio clip to play it and view details, such as the model used and prompt settings.

> [!NOTE]
> To generate and assign assets directly, refer to [Assign generated assets with the Object Picker](xref:asset-picker).

> [!NOTE]
> Sound Generator stores the generated audio files in the `/GeneratedAssets` folder located at the root of your project. These assets remain in that folder until you remove them manually.

## Additional resources

* [Create a sound clip from a reference](xref:sound-reference)
* [Record and transform audio](xref:sound-record)