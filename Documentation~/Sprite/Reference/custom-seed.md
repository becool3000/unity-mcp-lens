---
uid: custom-seed
---

# Use a custom seed for consistent sprites

A custom seed is a numerical value that controls the randomness of the image generation process. When you enter the same prompt with the same seed, the generator produces the same or similar results each time. Without a seed, the results might vary with each generation, even if the prompt doesn’t change.

Use a custom seed when you want to do the following:

* Reproduce a specific result consistently across different sessions.
* Make small changes to a prompt while keeping the core output style or composition similar.
* Share a prompt and seed combination with others to achieve consistent results across the project.

## Best practices

Follow these suggestions to get the most out of custom seeds in sprite generation:

| Suggestion | Description |
| ---------- | ------------ |
| **Use a memorable seed** | Although you can use any integer as a seed, choose one that’s easy to remember or reference if you want to reuse it.
| **Pair the seed with your prompt** | Use your custom seed alongside your prompt to control the randomness, instead of relying on the default custom seed. |
| **Keep a record of seed and prompt combinations** | Maintain a record of the seed and the corresponding sprite prompt for future reference, especially if you plan to share sprites or iterate designs later. <br>You can find this information in the **Generations** panel. To view it, right-click a generated sprite and open the context menu. For more information, refer to [Manage sprites](xref:manage-sprite).|
| **Regenerate sprites with identical results** | Use the same seed and prompt combination whenever you need to recreate a sprite. |

## Example workflow

To use the custom seed, follow these steps:

1.  In the **Prompt** field, describe the sprite you want to generate.

    For example, `Generate a medieval castle with flags in pixel art style. Use vivid colors and a sunny backdrop.`

1. To specify a custom seed to generate consistent results, enable **Custom Seed** and enter a seed number.

   For example, `8675309`

    This combination will consistently generate a similar sprite and make it easy to iterate on or share with others.
1. Select **Generate** to generate a sprite that matches the description.

   The generator creates a detailed pixel-art castle based on the prompt.

1. Share the custom seed, `8675309` with your team or collaborators so they can recreate the same castle sprite on their systems.

   If you refine the prompt (for example, add `moat and drawbridge`), use the same seed to maintain consistent randomness patterns.

## Additional resources

* [Create a sprite from a prompt](xref:generate-sprite)
* [Create a Texture2D asset from a prompt](xref:generate-texture2d)
* [Create a sound clip from a prompt](xref:sound-prompt)
* [Create a material from a prompt](xref:material-generate-prompt)