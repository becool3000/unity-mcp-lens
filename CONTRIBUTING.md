# Contributing

This repository is the Unity MCP Lens package. Contributions should keep the package standalone, MCP-first, and side-by-side safe with Unity's official Assistant package.

## Scope

Good candidates for pull requests:

- MCP bridge reliability fixes.
- Tool-pack, schema-cache, and compact-output improvements.
- Unity editor/dev tools that fit the Lens pack model.
- Documentation and migration guidance.
- Static gates that prevent package identity regressions.

Usually out of scope:

- Assistant chat UI.
- Cloud asset generation.
- Assistant Gateway or ACP product workflows.
- Reintroducing `Unity.AI.Assistant.*` runtime or editor dependencies.
- Broad old-name aliases for Assistant tools.

## Local Setup

Clone with Git LFS enabled if this repository contains any binary artifacts:

```bash
git lfs install
git clone https://github.com/becool3000/unity-mcp-lens.git
```

Use the package from a Unity project with:

```json
"com.becool3000.unity-mcp-lens": "file:../UnityAIAssistantPatch"
```

## Before Opening A PR

1. Keep the diff focused.
2. Explain the Unity/MCP behavior change.
3. Note compatibility risks for existing Lens users.
4. Run the relevant `Tools~/Test-*.ps1` gates.
5. If the change touches Unity editor code, smoke it in a Unity project before merging.
