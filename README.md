# unity-mcp-lens

`unity-mcp-lens` is my maintained fork of Unity's `com.unity.ai.assistant` package, evolved into a focused Unity MCP bridge and tool-surface project.

This repository started as a patch fork of `com.unity.ai.assistant 2.3.0-pre.2`. It still tracks that upstream package closely enough to stay usable as a local Unity package source, but it is no longer just a small patch mirror. The main direction now is to build a cleaner, quieter, more controllable MCP path for external agents while preserving compatibility with the existing Unity Assistant package where that still helps.

## What This Is

This repo currently serves two roles at once:

1. It is still a drop-in local package source for `com.unity.ai.assistant` inside Unity projects.
2. It is the home of `unity-mcp-lens`, an owned MCP bridge/server direction focused on:
   - lower bridge noise
   - narrower model-facing tool exposure
   - event-driven tool sync instead of constant polling
   - more compact tool outputs
   - better telemetry and benchmarking
   - cleaner recovery behavior during Unity reload/build/import windows

The package name stays `com.unity.ai.assistant` for compatibility with Unity projects, but the repo direction is now explicitly `unity-mcp-lens`.

## Why This Exists

Unity's official package is the baseline, but it does not fully cover the workflow I care about:

- Codex-oriented Unity MCP usage
- low-noise bridge behavior
- deterministic, session-scoped tool exposure
- payload and latency measurement
- practical operation during compile/import/reload churn
- custom Unity tools that are useful for real project work

This repo exists to make that path better now, instead of waiting for upstream convergence. When upstream improves, I want to adopt the parts that help and keep the parts that are specific to this bridge direction.

## Current Direction

`unity-mcp-lens` is the quality lane.

That means:

- the legacy Unity relay path can still exist for compatibility
- the Lens path is where new MCP architecture work goes first
- the bridge is treated as a real product surface, not just a temporary experiment

The most important architectural ideas already in motion are:

- an owned MCP-only stdio server path
- bridge-owned manifest versioning
- event-driven tool sync
- narrow default tool exposure through pack-based export
- explicit pack control tools such as `Unity.ListToolPacks`, `Unity.SetToolPacks`, and `Unity.ReadDetailRef`
- detail-ref based compact output for noisy tools
- per-project MCP telemetry and benchmark tooling

## Status

Today this repo is:

- based on `com.unity.ai.assistant 2.3.0-pre.2`
- compatible with Unity `6000.3` and later in the same general range as the upstream package
- usable as a local package source
- able to install the owned `unity-mcp-lens` server alongside the legacy relay path

The Lens path is real and usable, but it is still under active iteration. Expect continued changes in:

- bridge/session protocol details
- pack definitions
- schema caching
- output compaction
- project settings and generated MCP config

## Installing In A Unity Project

Use this repo as a local package source from another Unity project:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

Or with an absolute path:

```json
"com.unity.ai.assistant": "file:C:/UnityAIAssistantPatch"
```

## Lens Installation Notes

- The owned Lens server installs separately from Unity's legacy relay.
- Lens currently installs under `~/.unity/unity-mcp-lens/`.
- The legacy relay still installs under `~/.unity/relay/` when enabled.
- For MCP-only projects, the package can suppress legacy relay install/startup and use Lens as the primary path.
- Until prebuilt Lens binaries are bundled in-repo, first-time local installation may publish from source and therefore needs a local .NET SDK 8+.

## Project Structure

The live Unity package source is primarily under:

- `Editor/`
- `Runtime/`
- `Modules/`

The owned Lens server source lives under:

- `UnityMcpLensApp~`

Benchmark and helper tooling lives under:

- `Tools~/`

When maintaining this repo, treat the live package folders as the source of truth and ignore `.codex-temp` snapshots unless a task explicitly says otherwise.

## Roadmap

The medium-term plan is straightforward:

1. Keep the package usable as a local fork of Unity Assistant.
2. Keep evolving `unity-mcp-lens` beside the upstream relay path instead of trying to replace everything at once.
3. Reduce bridge chatter further through event-driven sync and caching.
4. Keep shrinking the model-facing tool surface through pack-based export.
5. Compact large tool outputs by default and expose full detail only on demand.
6. Improve observability so changes are measured instead of guessed.
7. Stay close enough to upstream that useful updates can still be merged selectively.

Longer term, I want this repo to become a more robust Unity MCP foundation:

- better for Codex
- better for other MCP clients
- better for noisy real-world Unity projects
- and easier to reason about than the current poll-heavy bridge flow

## Relationship To Upstream

This is not an official Unity release channel.

It is an independently maintained fork that:

- keeps upstream as an important baseline
- adopts upstream improvements when they are useful
- carries local bridge/tooling work where upstream is not enough yet

The goal is not to diverge for its own sake. The goal is to build a better Unity MCP path while keeping the fork understandable and maintainable.

## More Technical Notes

For technical compatibility and fork-history notes, see:

- [README-CODEX-PATCH.md](README-CODEX-PATCH.md)
