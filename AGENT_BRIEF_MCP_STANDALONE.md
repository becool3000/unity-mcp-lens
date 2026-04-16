# Agent Brief: Make MCP Fully Standalone (Option A)

## Goal
Refactor the Unity MCP package to be completely standalone with zero dependencies on Assistant modules. This will enable it to be open-sourced independently as a pure MCP protocol implementation for Unity.

## Current State
- MCP currently depends on: `Unity.AI.Assistant.Tools.Editor`, `Unity.AI.Assistant.Runtime`, `Unity.AI.Assistant.AssetGenerators.Editor`, and others
- The adapter pattern (`AgentToolMcpAdapter`) bridges between Assistant tools and MCP tools
- MCP Core already implements 60% of what's needed for standalone tool registry
- ~25 built-in tools use Assistant-specific utilities and frameworks

## Desired End State
- `com.unity.ai.mcp` package with zero external dependencies (except Unity core + Newtonsoft JSON)
- All 25 built-in tools remain functional and unchanged in behavior
- Tool registration uses native MCP framework instead of Assistant's FunctionCalling
- Can be published/open-sourced independently
- Existing assistant package can optionally depend on MCP (reverse dependency)

## Scope of Work

### Phase 1: Extract Utilities (Low Risk)
**Extract these utility modules into MCP:**
- `Modules/Unity.AI.Assistant.Tools.Editor/Utils/` → `Modules/Unity.AI.MCP.Editor/Utils/`
  - JSON type adapters
  - Serialization helpers
  - Type conversion methods
  
- `Modules/Unity.AI.Assistant.Backend.Socket.Tools/` → `Modules/Unity.AI.MCP.Editor/Utils/Editor/`
  - GameObjectHelper
  - SceneHelper
  - Other editor utilities

**Deliverable**: Copies of utility code with imports updated to remove Assistant references

### Phase 2: Build Core Tool Registry (Medium Risk)
**Current State**: MCP has basic `ToolRegistry` that discovers tools via attributes

**What needs to be added:**
- Tool registration from `@AgentTool` attributes (currently in FunctionCalling)
- Tool execution pipeline (ToolExecutionContext, parameter marshaling)
- Schema generation from method signatures
- Error handling and validation
- Tool discovery at editor startup

**Reference implementation to study:**
- `Modules/Unity.AI.Assistant.Tools.Editor/FunctionCalling/ToolRegistry.cs`
- `Modules/Unity.AI.Assistant.Tools.Editor/FunctionCalling/ToolExecutionContext.cs`
- `Modules/Unity.AI.Assistant.Tools.Editor/Backend/FunctionToolbox.cs`

**Deliverable**: 
- `Modules/Unity.AI.MCP.Editor/ToolRegistry/` expanded with full registry implementation
- New classes: `MCP.ToolExecutor`, `MCP.ToolSchema`, `MCP.ToolDiscovery`
- Attribute system: Keep `@AgentTool` attribute or rename to `@McpTool`

### Phase 3: Refactor Tool Attributes (Medium Risk)
**Current**: All 25 tools use `@AgentTool` attribute and inherit from patterns in Assistant.Tools

**What needs to change:**
- Update all tool classes to use MCP's native tool framework instead of Assistant's
- Remove any `Agent`-specific logic from tools
- Update imports from `Unity.AI.Assistant.*` to `Unity.AI.MCP.*`
- Refactor tool execution to use MCP's ToolExecutionContext

**Tools to refactor** (in `Modules/Unity.AI.MCP.Editor/Tools/`):
- RunCommand tools
- Resource/Asset tools
- SceneTools
- ShaderTools
- UITools
- EditorTools
- And all others (~25 total)

**Approach:**
- Create parallel tool framework in MCP
- Gradually migrate tools one-by-one
- Run tests after each migration to ensure functionality

**Deliverable**: All 25 tools refactored to use MCP registry, same external behavior

### Phase 4: Remove Adapter Pattern (Medium Risk)
**Current**: `AgentToolMcpAdapter` bridges Assistant tools → MCP tools

**What needs to happen:**
- Remove `AgentToolMcpAdapter.cs`
- Remove dependency on `Unity.AI.Assistant.Tools.Editor`
- Update `.asmdef` files to remove Assistant references
- Update any imports that pointed to the adapter

**Deliverable**: Clean removal of adapter layer, tools work directly via MCP registry

### Phase 5: Clean Assembly Dependencies (Low Risk)
**Update these .asmdef files:**
- `Modules/Unity.AI.MCP.Editor/Unity.AI.MCP.Editor.asmdef`
  - Remove all `Unity.AI.Assistant.*` references
  - Keep only: `Unity.AI.Toolkit.Async`, `Unity.AI.Tracing`, `Newtonsoft.Json`, Unity core modules
  
- Any tool-specific `.asmdef` files that import Assistant modules

**Deliverable**: Clean dependency graph with no Assistant module references

### Phase 6: Test & Validate (Low Risk)
**Testing**:
- Verify all 25 tools still register and execute correctly
- Test tool schema generation
- Verify MCP bridge still connects properly
- Run existing test suite if one exists

**Deliverable**: Passing tests, working MCP package

## Dependencies Between Phases
- Phase 1 (utilities) is independent - can start anytime
- Phase 2 (registry) must complete before Phase 3
- Phase 3 (tools) depends on Phase 2
- Phase 4 (adapter removal) depends on Phase 3
- Phase 5 (assembly cleanup) depends on Phase 4
- Phase 6 (testing) should run after each phase

## Key Files to Review First
- `Modules/Unity.AI.MCP.Editor/Bridge.cs` - Main entry point
- `Modules/Unity.AI.MCP.Editor/ToolRegistry/ToolRegistry.cs` - Current registry
- `Modules/Unity.AI.Assistant.Tools.Editor/FunctionCalling/ToolRegistry.cs` - Reference implementation
- `Modules/Unity.AI.MCP.Editor/Tools/` - All 25 tools to refactor
- `Modules/Unity.AI.MCP.Editor/Adapters/AgentToolMcpAdapter.cs` - Adapter to remove

## Success Criteria
- [ ] Zero imports of `Unity.AI.Assistant.*` in MCP packages
- [ ] All 25 tools functional (verified by testing)
- [ ] MCP package can be built standalone
- [ ] Tool registration and execution works via pure MCP framework
- [ ] No circular dependencies
- [ ] Documentation updated to reflect standalone status

## Estimated Effort
- Phase 1: 2-4 hours (straightforward copy/paste)
- Phase 2: 8-12 hours (moderate complexity, reference impl exists)
- Phase 3: 12-16 hours (repetitive but straightforward)
- Phase 4: 2-3 hours (cleanup)
- Phase 5: 1-2 hours (dependency management)
- Phase 6: 3-4 hours (testing and validation)

**Total: ~30-40 hours** (mostly mechanical refactoring, low risk once framework is in place)

## Notes
- Most of the work is boilerplate refactoring, not algorithmic complexity
- Keep tool behavior identical - only change how they're registered/executed
- Reference implementations exist in the codebase to copy from
- Can be done incrementally - Phase 1-2 establish foundation, then Phase 3 can be parallelized
- Once Phase 2 is stable, Phase 3 becomes very repetitive and could be semi-automated
