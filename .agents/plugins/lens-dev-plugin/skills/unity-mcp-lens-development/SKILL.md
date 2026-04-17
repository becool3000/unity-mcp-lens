---
name: unity-mcp-lens-development
description: Develop, test, and improve Unity MCP Lens tools, packs, bridge behavior, package UI, and Unity editor automation workflows. Use when working on Lens itself, adding or debugging Lens MCP tools, changing tool packs, validating bridge behavior, or making Unity editor-authored persistent changes for Lens projects.
---

# Unity MCP Lens Development

## Prime Directive

Dogfood Lens. Use the Lens MCP bridge for Unity editor state inspection and editor mutations whenever Unity work is involved.

Do not bypass Lens by inventing runtime bootstrap code, temporary scene constructors, manual wrapper paths, or one-off editor hacks just to finish the task.

## Allowed Work Paths

- Use MCP/Lens tools for Unity editor actions.
- Edit source files directly only when writing package code, scripts, C# tools, tests, docs, or config.
- Use normal shell commands for git, static checks, package builds, text search, and non-Unity file maintenance.
- Use Unity editor-authored persistent changes for scenes, prefabs, assets, settings, and bindings.

## Persistence Rule

Build like a human Unity developer would:

- Create or edit scene objects in edit mode.
- Save scenes and prefabs.
- Bind serialized references.
- Update importer settings.
- Create assets on disk.
- Verify serialized or persistent state afterward.

Do not rely on runtime-only bootstrap creation for durable project structure unless the user explicitly asks for runtime generation architecture.

## Missing Tool Rule

If the task requires a Unity editor action and no Lens tool can do it cleanly:

1. Stop the Unity-facing work.
2. Do not work around it through runtime bootstrap code or manual editor simulation.
3. Write a short missing-tool report.
4. Include the proposed tool name, pack, inputs, output contract, safety rules, and validation test.
5. Ask whether to implement the Lens tool next.

## Missing Tool Report Format

- Needed action:
- Why existing Lens tools are insufficient:
- Proposed tool:
- Pack:
- Inputs:
- Output:
- Persistence/safety rules:
- Compactness/detailRef behavior:
- Smoke test:
