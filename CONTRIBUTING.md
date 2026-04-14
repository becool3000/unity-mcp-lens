# Contributing

This repository is a narrow patch fork of `com.unity.ai.assistant`. Contributions are welcome, but changes should stay focused on stability, interoperability, and public-repo hygiene.

## Scope

Good candidates for pull requests:

- MCP and relay startup stability fixes
- Bug fixes with clear regressions or reproducible failures
- Documentation improvements for local package use
- Public-readiness cleanup such as metadata, comments, or repo policy files

Changes that are usually out of scope unless they directly support the patch goal:

- Large feature additions unrelated to stability
- Broad product redesigns
- Replacing bundled upstream binaries without a strong justification

## Before Opening a PR

1. Keep the diff focused.
2. Explain the user-visible problem and the fix.
3. Note any tradeoffs or compatibility risks.
4. Include verification steps you actually ran.

## Local Setup

Because the repository includes bundled binaries tracked with Git LFS, clone it with Git LFS enabled:

```bash
git lfs install
git clone https://github.com/becool3000/unity-mcp-lens.git
```

To use the package in a Unity project, point `Packages/manifest.json` at a local checkout:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

## Notes

- This fork is not an official Unity release channel.
- Upstream package updates may supersede some patches here. When possible, keep changes easy to diff against upstream.
