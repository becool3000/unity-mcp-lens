---
uid: generate-cubemap
---

# Create a cubemap from a prompt

Create a new cubemap from a text-based prompt to use in your Unity project.

Cubemap Generator uses artificial intelligence (AI) models to produce equi-rectangular maps to use as cubemaps based on descriptive text. You can use these assets as skyboxes, environmental reflections, or lighting sources for your scenes.

To generate cubemaps, follow these steps:

1. Right-click an empty area in the **Project** window.
1. Select **Create** > **Rendering** > **Generate Cubemap**.

   A new cubemap appears in the **Assets** window.

1. Rename the cubemap.

   The Cubemap Generator window opens.

1. Select **Change** to choose a model.
1. In the **Select AI Model** window, browse or search for a model suited for your cubemap type (for example, **Cinematic** or **Cartoon**).
1. In the **Prompt** field, describe the cubemap you want to generate.

   For example, `Medieval path in a forest next to a castle`.

1. (Optional) To exclude specific elements, enter the keywords in the **Negative Prompt** field.

   For more information on negative prompts, refer to [Remove unwanted elements with negative prompts](xref:negative-prompt).

1. To set the number of cubemaps to generate, move the **Count** slider.
1. (Optional) To specify a custom seed to generate consistent results, enable **Custom Seed** and enter a seed number.

      For more information, refer to [Use a custom seed to generate consistent sprites](xref:custom-seed).
2.  Select **Generate**.

   The generated cubemap appears in the **Generations** panel and the **Inspector** window. Hover over the cubemap to view the prompt and model details.

   To apply the cubemap to your scene, refer to [Sky](xref:um-sky-landing).

## Additional resources

* [Create a cubemap from a reference image](xref:reference-cubemap)
* [Edit a cubemap](xref:modify-cubemap)