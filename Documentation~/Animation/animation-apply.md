---
uid: animation-apply
---

# Apply an animation to a character

After you’ve generated an animation, you need to apply it to a character in your scene. This involves the following workflow:

* Create an Animator Controller
* Add the animation clip to it
* Assign the controller to the character’s Animator component

The Animator Controller manages how and when animations play. It lets you control the character movements during gameplay.

## Prerequisites

Before assigning the animation to a character, ensure the following:

* Locate the character prefab in Unity's **Project** window. For example, `Assets/Characters/Prefabs`.
* Drag the character prefab into the **Scene** view.

Now that your character is added to the scene, you can assign the `.anim` file to it.

## Set up your character to play the animation

To set up your character to play the animation, follow these steps:

1. In your **Project** window, right-click and select **Create** > **Animation** > **Animator Controller**.

   The controller is saved in the `Assets` folder. Rename the controller, for example, `CharacterAnimator`.

1. Double-click the new Animator Controller to open it in the **Animator** window.
1. Drag the `.anim` file (animation clip) from the **Assets** window into the **Animator** window.

   This creates a state that contains your animation.

1. (Optional) To set the animation as the default state, right-click the state and select **Set as Layer Default State**.

   You can set up transitions in the **Animator** if you plan on having [multiple animations](#managing-multiple-animations).

1. Select your character prefab in the **Scene** or **Hierarchy** window.
1. In the **Inspector** window > **Animator** component:

   * Drag your Animator Controller (the one you just created) into the **Controller** field.
   * Ensure the **Avatar** field displays the name of your character prefab.

1. To test the animation, open the **Game** window and select **Play** in the Unity Editor.

   Your character now performs the assigned animation.

### Manage multiple animations

You can add multiple animation clips to an Animator Controller and link them together with transitions. For example, a character might start with a walk and then transition into a run animation.

1. Drag additional `.anim` files into the Animator Controller.
1. Create transitions between animations.
1. Select **Play** in the Unity Editor to test the sequence.

## Additional resources

* [Troubleshooting animation issues](xref:animation-troubleshoot)
* [Unity animation clips](https://docs.unity3d.com/Manual/AnimationClips.html)
* [Prompt guidelines for asset generation](xref:prompts)