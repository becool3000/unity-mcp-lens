---
uid: integration
---

# Asset creation in Assistant

Understand how Assistant uses Unity Generators to create assets from natural-language prompts.

Assistant has asset generation available through its integration with Unity Generators. You can create artificial intelligence (AI)-powered assets directly in the Assistant interface using simple text prompts without switching windows. Assistant identifies the asset type you describe, uses the matching Generator, and produces the asset automatically. Generated assets appear in the **Project** window and in the Generations panel in Generators, where you can preview, promote, or inspect them.

You can stay in Assistant to create assets, or open the Generator window to refine results. For step-by-step instructions, refer to [Create assets in Unity Editor](xref:use-generators-assistant).

For most work, you can remain in Assistant. Open the Generator window only when you need to:

* Adjust model selection
* Modify resolution or output count
* Add reference images
* Change material or lighting details
* Upscale a preview into a production-ready asset

Assistant automatically transfers all rewritten prompts and settings to the Generator window.
# Use Assistant to generate assets

Create artificial intelligence (AI)-generated assets directly from Assistant in the Unity Editor.

Assistant now integrates with [Unity Generators](xref:overview) to generate sprites, 2D textures, materials, sounds, animations, and cubemaps without leaving the Assistant interface.

When you prompt Assistant to create an asset (for example, `generate a cubemap of a tropical island`), Assistant identifies the request, generates the requested asset, and populates it with your input. You can then preview or fine-tune the results directly in the Generator window.

Assistant supports all Generator types available in Unity, including:

- [Sprite Generator](xref:sprite-overview)
- [Texture2D Generator](xref:texture2d-overview)
- [Sound Generator](xref:sound-intro)
- [Animation Generator](xref:animation-intro)
- [Material Generator](xref:material-overview)
- [Cubemap Generator](xref:cubemap-overview)
- [Terrain Layer Generator](xref:terrain-overview)

## Additional resources

- [Create assets in Unity Editor](xref:use-generators-assistant)
- [About Generators](xref:overview)
- [Open and use Assistant](xref:get-started)