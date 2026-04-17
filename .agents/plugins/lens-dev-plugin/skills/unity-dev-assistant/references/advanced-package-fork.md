# Advanced Package Fork

Use this only when normal bridge recovery is not enough.

Detect the local assistant package state from:

- `Packages/manifest.json`
- `Packages/com.unity.ai.assistant/package.json`
- any repo-local backlog note that names an external patch directory as the active source of truth

Modes:

- `RegistryDependency`: no local fork
- `LocalFolderDependency`: manifest points to a local folder or the package exists in `Packages/`
- `ExternalPatchSource`: the repo-local workflow intentionally patches an external mirror or package checkout first
- `Missing`: dependency not present

Advanced recovery may include:

- surfacing that a repo already carries a local fork
- surfacing when the repo expects package fixes in an external patch directory instead of the embedded package copy
- recommending work inside the local fork before touching bridge config again
- isolating relay, MCP, and play-mode fixes inside the package fork rather than scattering them through repo code

Package maintenance rules:

- treat `Editor/`, `Runtime/`, and other live package folders as the source of truth unless the repo-local docs say otherwise
- exclude `.codex-temp` snapshot content from source searches before deciding where to patch
- if the manifest dependency points to an external Lens checkout, patch that live external folder first and verify Unity compiles before testing
- use the compact session/bridge checks during normal feature work; reserve `-IncludeDiagnostics` for explicit bridge or package maintenance

Do not treat package patching as the default path. Use it only when bridge/package instability is the actual blocker.
