---
uid: sound-record
---

# Record and transform audio

You can record custom sounds and use them as a reference to create new audio clips. Sound Generator lets you generate custom sound effects or modify existing ones based on your recorded input.

To record and generate a new sound, follow these steps:

1. Open the **Generate New Audio Clip** window.
1. In the **Sound Reference** section, select **Start Recording**.

   ![Generate window with fields to record a sound](../images/sound.png)

1. Speak or make a sound into your microphone.

   > [!NOTE]
   > Ensure Unity Hub has access to your microphone.

1. Select **Stop Recording** to finish recording.
1. Adjust the **Strength** slider to control how closely the generated sound matches the reference.
1. Enable **Overwrite Sound Reference asset** to use your recorded sound as the reference.
1. Select **Generate**.

Sound Generator saves your recorded sound as `New Audio Clip.wav` in the `Assets` folder.

## Additional resources

* [Manage sound clips](xref:sound-manage)
* [Edit a generated sound clip with Sound Generator](xref:sound-edit)