---
uid: animation-trim
---

# Adjust direction and looping

Refine the generated animations with the **Trim** tab to improve compatibility with the game logic and character motion.

The generated animations might include a root motion (where the character moves away from the origin) or face inconsistent directions. Use the **Trim** tab to adjust the direction and position of the animation.

Use the **Trim** tab when:

* You want the animation to play in place.
* You need all the generated clips to face the same standardized forward direction (positive z-axis in the Unity Editor).
* You want to detect the best loop segment within the animation.

After you have generated an animation with [Text to Motion](xref:animation-create#text-to-motion) or [Video to Motion](xref:animation-create#video-to-motion), follow these steps:

1. In the **Generations** panel, select a generated animation.
1. Select the **Trim** tab.
1. (Optional) Enable **Root Motion** to allow the character to walk away from the origin.
1. (Optional) Enable **Best Loop** to find the best loop segment in the animation. The **Start** and the **End** markers define the search range.
1. Review the loop quality color indicator in the timeline. The timeline bar displays the loop quality with the following colors:

   * **Green**: Excellent loop
   * **Yellow**: Good loop
   * **Red**: Poor loop

   If you disable **Best Loop**, the Animation Generator uses the exact **Start** and **End** markers you set, without searching for the best match. You can still evaluate the loop quality based on the color indicator and adjust markers manually to improve results.
1. To create a smooth transition between the start and the end of the loop, select **Loop Pose** in the **Inspector** window.
1. Enter a value in **Minimum Duration** (in seconds) to define the minimum acceptable loop length.
1. Select **Trim**.

For description of all the options in the **Trim** tab, refer to [Trim tab reference](xref:trim-reference).

The Animation Generator creates a new animation clip with a **TRIM** label in the **Generations** panel. This label indicates that the animation is a modified version of a generated clip.

## Additional resources

* [Create an animation clip](xref:animation-create)
* [Trim tab reference](xref:trim-reference)
* [Apply an animation to a character](xref:animation-apply)
