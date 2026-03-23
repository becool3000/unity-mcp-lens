---
uid: trim-reference
---

# Trim tab reference

Explore the options in the **Trim** tab to refine the generated animations for consistency and loop quality.

The **Trim** tab provides settings to normalize position and direction, enable or disable root motion, and fine-tune loop behavior for the generated animation clips. For information on how to access the **Trim** tab, refer to [Adjust direction and looping](xref:animation-trim).

## Trim tab

The **Trim** tab contains the following settings:

| **Settings** | **Description** |
| ---------------- | --------------- |
| **Root Motion** | Enables or disables the root motion. When disabled (default), the animation plays in place. This prevents the character from drifting away from the origin. |
| **Best Loop** | When enabled (default), the Animation Generator finds the best start and end points for a seamless loop. When disabled, use the loop markers manually.<br>A color bar scores the loop quality. The following options are available: <ul><li>**Green**: excellent</li> <li>**Yellow**: good</li> <li>**Red**: bad</li></ul> |
| **Minimum Duration** | Defines how long (in seconds) a loop must be to be valid. Higher values create smoother, more natural loops, but might reduce the available loop options. |

## Additional resources

* [Create an animation clip](xref:animation-create)
* [Adjust direction and looping](xref:animation-trim)
* [Apply animation to a character](xref:animation-apply)