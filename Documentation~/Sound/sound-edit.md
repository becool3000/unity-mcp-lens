---
uid: sound-edit
---

# Edit a generated sound clip with Sound Generator

After you generate a sound clip with a prompt, you can refine and modify it to adjust playback, looping, volume, and other settings.

To edit a sound clip, follow these steps:

1. In the **Generate New Audio Clip** window, select the audio clip you want to edit.
1. In the **Inspector** window, select **Edit**.

   The **Edit New Audio Clip** window opens. It contains the following key controls:

   | Control | Icon | Description |
   | ------- | ---- | ----------- |
   | Playback icon | ![play icon](../images/play.png) | Plays the audio clip. The button changes to a stop (![stop icon](../images/stop.png)) icon when the clip is playing. |
   | Loop icon | ![loop icon](../images/loop.png) | Enables or disables looping. When enabled, the clip continuously repeats during playback. |
   | Sound envelope icon | ![sound envelope](../images/sound-envelop.png) | Opens the sound envelope editor. You can adjust the fade-in and fade-out points of the audio clip. The green line in the waveform indicates the playback volume. Drag the points on the envelope to adjust the fade-in and fade-out positions. |
   | Crop icon | ![crop icon](../images/crop.png) | Enables the cropping mode. You can trim the beginning and end of the audio clip. Drag the crop bars (green vertical lines) to select the section you want to keep. Only the section between the crop bars plays back after cropping. |
   | Zoom | ![zoom list](../images/zoom.png) | Adjusts the zoom level of the waveform. You can also use the mouse wheel to zoom in and out for more precise editing. |
   | Save icon | ![save icon](../images/save.png) | Saves the edited audio clip to the `Assets` folder. |

## Additional resources

* [Create a sound clip from a prompt](xref:sound-prompt)
* [Create a sound clip from a reference](xref:sound-reference)
* [Record and transform audio](xref:sound-record)