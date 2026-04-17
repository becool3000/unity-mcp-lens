# Builds

Use this reference for long custom exports, especially WebGL builds that spend minutes inside Bee and `wasm` compile/link steps.

## Scene preflight

- Before starting a long build, validate the exact enabled build-scene list if the requested scene order is known.
- Use `scripts/Test-UnityBuildSceneList.ps1 -ExpectedScenes <paths...>` for a read-only diff against `ProjectSettings/EditorBuildSettings.asset`.
- The preflight returns:
  - `enabledScenes` in current order
  - `missingScenes`
  - `unexpectedEnabledScenes`
  - `orderMismatch`
  - `exactMatch`
- Treat any non-exact match as a stop condition for the long build. Repo-specific builder scripts may fix build settings, but this shared skill should only detect and report the diff.

## Long WebGL build monitoring

- If a `Unity_RunCommand` launches a WebGL build, prefer `scripts/Invoke-UnityRunCommand.ps1 -MonitorBuildMode WebGL`.
- Provide any known paths:
  - `-BuildOutputPath`
  - `-BuildReportPath`
  - `-SuccessArtifactPath`
- If MCP stdout drops during the build, the wrapper should switch to passive monitoring instead of treating the session as a broken bridge immediately.

## Active-build signals

- Treat `Editor.log` lines such as `C_WebGL_wasm`, `Link_WebGL_wasm`, or Bee progress entries like `[585/596] ... wasm` as active build markers.
- Only classify the build as still active when there is no later terminal success or failure marker.

## Terminal-build signals

- Success:
  - success artifact exists
  - build report says `Result: Succeeded`
  - `Editor.log` reports `Build completed with a result of 'Succeeded'`
- Failure:
  - build report says `Result: Failed` or `Cancelled`
  - `Editor.log` reports `Build completed with a result of 'Failed'`
  - `Editor.log` reports `BuildPlayerWindow+BuildMethodException`

## Session checks during builds

- `scripts/Check-UnityDevSession.ps1` should report `RecommendedPath = MonitorActiveBuild` when the bridge check classifies the session as `BuildInProgress`.
- During that state, do not spam extra MCP recovery or reconnect prompts. Watch `Editor.log` and the output/report/artifact paths until the build finishes or fails.
