---
uid: ui-agent-workflow
---

# UI Agent workflow

Create and preview UI Toolkit assets in the Unity Editor with automated validation and dependency checks.

Use this workflow when you want to prototype or generate UI Toolkit assets directly in the Unity Editor without manually creating `.uss`, `.uxml`, Panel Setting assets, or validation schemas.

If you only want to explore UI ideas without saving files, use the **Ask** mode instead of the **Agent** mode.

## UI Agent workflow in Agent mode

When you submit a UI-related prompt (for example, `Create a health bar UI`), Assistant runs the workflow matching the following sections.

### Route the task to the UI Agent

Assistant analyzes your prompt and if the task involves UI Toolkit, it calls the **UI Toolkit Manager** to trigger the UI Agent. The UI Agent receives instructions (UI type, layout, styles, and assets) about the type of UI to generate.

### Find or create Panel Settings

The UI Agent calls the **Find Or Create Default Panel Settings** tool to sort out the setting dependencies. If default settings exist, they're reused. If they don't exist, new default settings are created. This tool resolves all the UI Toolkit dependency requirements before generation begins.

### Generate and validate UI styles

The UI Agent generates the `.uss` (style) file. It calls the **UI Asset Validation** tool to check the generated file for errors. If validation passes, the file is saved, else the error is reported to the agent. The UI Agent regenerates the code and repeats the validation until the errors are resolved.

### Generate and validate UI document

The UI Agent generates the `.uxml` UI document. The same validation loop occurs to do the following:

  - Detect the errors.
  - Regenerate or repair the code.
  - Revalidate until clean.

### Generate UXML schemas (if required)

If validation indicates missing or outdated schema dependencies, the UI Agent calls **Generate Uxml Schemas** to generate the missing schema files and rerun the validation process.

If valid UXML schemas already exist, the UI Agent skips this step.

### Preview the UI

After validation passes, the UI Agent calls **Get UI Asset Preview** to render a visual preview of the generated UI for review. This step is required for the visual validation of the generation result. If the result doesn't meet the request, refine the UI document or style with the generate and validation tool again.

### Save assets

The UI Agent asks for permission to save the following files:

- `.uss` (styles) in the `Assets/UI` folder.
- `.uxml` (UI document) in the `Assets/UI` folder.
- Panel Settings assets (if newly created)
   - `PanelSettings` in the `Assets/UI` folder.
   - `PanelTextSettings` in the `Assets/UI` folder.
   - `ThemeStyleSheet` in the `Assets/UI Toolkit/UnityThemes/` folder.

### Final visual check and completion

The UI Agent confirms that there are no outstanding validation errors, verifies dependencies, and performs a final visual confirmation using the preview. The task is complete.

## UI Agent workflow in Ask mode

In the **Ask** mode, the UI Agent doesn't write files. You receive validation results and visual previews without saving any UXML or USS files.

In Ask mode, Assistant routes requests through the **UI Toolkit Manager**. The UI Agent can then call the following tools:

  - **Validate UI Asset**
  - **Find Panel Settings**
  - **Generate UXML Schemas**
  - **Get UI Asset Preview** (for existing UI documents in your project)

## Additional resources

* [UI Agent tool reference](xref:ui-agent-reference)
* [UI Agent](xref:ui-agent-landing)
