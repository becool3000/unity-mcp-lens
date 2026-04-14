# Patched Fork Research Study

This package now includes additive study hooks for benchmarking the patched `com.unity.ai.assistant` fork without changing the primary MCP or relay workflow.

## Baseline workflow

1. Freeze the patched fork state from the current local package source.
2. Generate a baseline manifest:

```powershell
.\Tools~\Write-ResearchBaselineManifest.ps1 -HostProjectRoot C:\Path\To\UnityProject
```

3. Run the scenario matrix against the host project's local package baseline.
4. Analyze host-side metrics written to `Library/AI.Gateway.PayloadStats.jsonl`:

```powershell
.\Tools~\Analyze-PayloadStats.ps1 -StatsPath C:\Path\To\UnityProject\Library\AI.Gateway.PayloadStats.jsonl
```

5. For an in-editor view inside any host project using this local fork, open `Window/AI/Assistant Usage`.

6. To benchmark Lens pack switching and schema-fetch volume against a live host project:

```powershell
.\Tools~\Benchmark-LensPackSwitch.ps1 -ProjectPath C:\Path\To\UnityProject
```

This runs three isolated same-process scenarios against `unity-mcp-lens`:

- `foundation -> scene`
- `scene -> console`
- `scene -> foundation`

The benchmark reports exported tool counts, `get_tool_schema` request and response volume, and total bridge traffic for each transition.

The benchmark wrapper uses a small `net8.0` helper app under `Tools~`, so it requires a local .NET SDK install on the machine running the benchmark.

## Measurement scope

The instrumented baseline emits:

- Prompt markdown, attached-context, and full chat-envelope payload stats.
- Prompt/context conversion timing spans and JSONL correlation fields.
- Capability and skill response sizes, hashes, and ordering-sensitivity signals.
- Bridge command request/response coverage with durations and request IDs.
- Tool snapshot churn with both minimal-hash and full-hash tracking.
- MCP tool execution duration, success/failure, and output volume.
- External synthetic conversation reuse events for MCP-driven tool calls.
- ACP prompt-content sizing for relay-side prompt assembly.

## Comparison order

1. `patched-baseline-frozen` vs `patched-baseline-instrumented`
2. `patched-baseline-instrumented` vs `patched-optimized-research`
3. Optional overlap-only comparison against official `2.3.0-pre.2`

## Host-project notes

- Runtime measurements are written to the host project `Library`, not only the package repo.
- Keep the host project content snapshot fixed across benchmark runs.
- Treat payload-byte and normalized-hash trends as Stage 1 proxy metrics until API usage telemetry is available.
