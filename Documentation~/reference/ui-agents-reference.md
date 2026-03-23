---
uid: ui-agent-reference
---

# UI Agent tool reference

Learn about the tools that UI Agent uses during UI generation workflows. Assistant automatically selects these tools based on the mode (**Ask** or **Agent**) you use.

## UI Agent tools by mode

| Tool name | Available in | Description |
|----------|----------------|-------------|
| `Unity.FindPanelSettings` | **Ask** | Searches for existing panel settings required for UI Toolkit UI rendering. |
| `Unity.FindOrCreateDefaultPanelSettings` | **Agent** | Finds existing panel settings or creates default settings if none exist. |
| `Unity.GenerateUxmlSchemas` | **Ask**, **Agent** | Generates missing UXML schema files required for UI validation. |
| `Unity.ValidateUIAsset` | **Ask** | Validates generated `.uxml` and `.uss` files without saving them. |
| `Unity.SaveAndValidateUIAsset` | **Agent** | Validates and saves generated UI assets to disk. |
| `Unity.GetUIAssetPreview` | **Ask**, **Agent** | Generates a preview image of the UI layout which can be analyzed by the Assistant. |

When you hover over a UI Agent tool in the Assistant interface, the tooltip displays the following information:

- The tool name
- The parameters passed to the tool
- The actual values used during execution

## Additional resources

- [UI Agent overview](xref:ui-agent-intro)
- [UI Agent workflow](xref:ui-agent-workflow)