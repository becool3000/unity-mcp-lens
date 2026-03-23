---
uid: audio-edit-overview
---

# Introduction to editing audio clips

Learn how Assistant modifies audio clips through silence removal, time-range extraction, volume adjustment, and seamless loop creation to support audio workflows in Unity.

Assistant uses natural-language prompts to edit audio clips directly in the Unity Editor. You can clean recordings, extract specific segments, adjust volume, and create looping ambience without external audio-editing tools.

Assistant provides the following audio-editing operations:

* [Trim silence](#trim-silence)
* [Trim by time range](#trim-a-time-range)
* [Change volume](#adjust-volume)
* [Generate loops](#create-seamless-loops)

## Trim silence

Use this operation to remove silent sections from the beginning and end of an audio clip to clean up the recording. By eliminating dead space, you can make sounds trigger exactly when intended.

Example prompts include:

* Remove the silence at the start and end of this clip.
* Clean the audio by trimming silence from both ends.
* Cut the silent sections so the clip starts immediately.

## Trim a time range

Use this operation to create a sub audio clip from a start time and an end time. You can extract a part of an audio clip between two timestamps to isolate specific beats, phrases, impacts, or gestures from a longer recording. You can reference timestamps directly in your prompt.

Example prompts include:

* Trim this clip from 00:03 to 00:07.
* Extract the segment between 12 seconds and 18 seconds.
* Keep the segment between 30 seconds and 35 seconds.

## Adjust volume

Use this operation to increase or decrease the volume of a clip by a factor. Prompts must reference a relative change, such as a percentage or simple multiplier.

Example prompts include:

* Increase the volume of this clip by 50%.
* Reduce the volume by half.

## Create seamless loops

Use this operation to blend the end of an audio clip with the beginning of the same clip using a crossfade algorithm to produce a smooth, gap-free loop. This is ideal for audio clips that must repeat without clicks or pops.

For best results, remove any silence at the beginning and end of the clip before creating the loop. This ensures that crossfade blends active audio rather than silent sections.

Example prompts include:

* Create a seamless loop from this clip.
* Remove silence and then loop this audio.

## Additional resources

* [Edit an audio clip](xref:audio-editing)
* [Edit a generated sound clip with Sound Generator](xref:sound-edit)