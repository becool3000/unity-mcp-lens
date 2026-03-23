---
uid: animation-troubleshoot
---

# Troubleshooting animation issues

Solve common problems that might arise while working with Animation Generator.

## `.anim` file doesn’t show in Animator

Ensure the `.anim` file is imported correctly and saved in the `Assets`/`Animations` folder. Refresh Unity.

## Animation isn’t playing on character

Verify the Animator Controller is assigned in the prefab’s **Animator** component in the **Inspector** window. Ensure proper transitions.

## Character doesn't animate

Ensure the Animator Controller is assigned to the character. Also, ensure the animation clip is correctly added to the Animator Controller.

## Animation stops abruptly

Increase the clip duration or adjust transitions.

## Additional resources

* [Create an animation clip](xref:animation-create)
* [Unity animation clips](https://docs.unity3d.com/Manual/AnimationClips.html)