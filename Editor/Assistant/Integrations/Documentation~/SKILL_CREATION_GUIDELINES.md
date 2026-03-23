# AI Assistant Skill Creation Guidelines

# Background

To bring more domain knowledge to AI Assistant, we developed the AI Assistant Skills framework in backend. We want to use Skills to bring in the context and expertise the LLM typically lacks. With skills, our core agent can operate as an expert when needed, eliminating the need to build separate expert agents for most specialized tasks. 

We plan to use this document to guide us how to add Skills to AI Assistant with minimal risks. We will keep upgrading the document based on the new learnings. 

# How Skills Work at Runtime

1. At startup, only each skill's name and description are loaded into the system prompt.  
2. When the user's request matches a skill, the agent calls ActivateSkill and the full SKILL.md body is loaded into context.  
3. Additional reference files are loaded **only when needed** (progressive disclosure).  
4. Skills can also dynamically register **tools** that become available only when the skill is active.

## Skill Structure

Each skill is a folder containing a `SKILL.md` file plus optional supporting files.

```
my-skill-name/
├── SKILL.md              # Required: frontmatter + instructions
├── references/           # Optional: detailed reference material
│   ├── api-notes.md
│   └── common-patterns.md
└── resources/            # Optional: code templates, scripts, schemas
    └── template.cs
```

## SKILL.md Format

A SKILL.md file has two parts: YAML frontmatter and a markdown body.

```
---
name: my-skill-name
description: Does X for Y scenarios. Use when the user asks about Z.
required_packages:
  com.unity.some-package: 1.2.3
tools:
  - Unity.Camera.Capture
  - Unity.EnterPlayMode
enabled: true
```

Frontmatter fields

| Field | Required | Description |
| ----- | ----- | ----- |
| `name` | Yes | Unique skill identifier. **Lowercase letters, numbers, and hyphens only.** Max 64 chars. |
| `description` | Yes | What the skill does and when to use it. Max 1024 chars. Used for skill discovery. |
| `required_packages` | No | Unity package dependencies as `package-name: version` pairs. Semver format (e.g. `1.2.3`, `1.0.0-preview.1`). |
| `tools` | No | List of tool `function_id` strings to make available when the skill is activated (e.g. `Unity.Camera.Capture`). |
| `enabled` | No | Boolean, defaults to `true`. Set `false` to disable without deleting. |

# How to Write Effective Skills

This guidance is adapted from the [official Anthropic Agent Skill guide](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview). It is highly recommended to use LLM with the [skill-creator](https://github.com/anthropics/skills/blob/main/skills/skill-creator/SKILL.md) skill when authoring skills for your domain expertise.

**1\. Only add context the LLM doesn't already have.** 

Challenge each piece of information:  
\*   "Does the LLM really need this explanation?"  
\*   "Can I assume it already knows this?"  
\*   "Does this paragraph justify its token cost?"

**Good** (\~50 tokens):

```
# Add CinemachineBrain
Check if MainCamera has `CinemachineBrain`. If missing, add it.
```

**Bad** (\~150 tokens):

```
# Add CinemachineBrain
CinemachineBrain is a Unity component that is part of the Cinemachine package.
It needs to be attached to the main camera in your scene. The main camera is usually the camera with the "MainCamera" tag. Without this component, Cinemachine virtual cameras will not work. Let me explain how to check for it...
```

**2\. Define Success Criteria**

How will you know your skill is working? These are aspirational targets \- rough benchmarks rather than precise thresholds. Aim for rigor but accept that there will be an element of vibes-based assessment. 

Quantitative metrics: 

* Skill triggers on 90% of relevant queries – How to measure: Run 10-20 test queries that should trigger your skill. Track how many times it loads automatically vs. requires explicit invocation.   
* Completes workflow in less tool calls (less X) – How to measure: Compare the same task with and without the skill enabled. Count tool calls and total tokens consumed. 


Qualitative metrics: 

* Users don't need to prompt the AI Assistant about next steps – How to assess: During testing, note how often you need to redirect or clarify.   
* Workflows complete without user correction – How to assess: Run the same request 3-5 times. Compare outputs for structural consistency and quality.   
* Consistent results across sessions – How to assess: Can a new user accomplish the task on first try with minimal guidance?

**3\. Write in Third Person**

The description is injected into the system prompt alongside other skills. Inconsistent point-of-view hurts discovery.

* **Good:** "Creates and configures Cinemachine 3.1 cameras for Unity scenes."  
* **Avoid:** "I help you create Cinemachine cameras."  
* **Avoid:** "You can use this skill to create cameras."

**4\. Set Appropriate Degrees of Freedom**

Match specificity to the task's fragility:

| Freedom Level | When to Use | Example |
| ----- | ----- | ----- |
| **High** (text guidance) | Multiple valid approaches, context-dependent | "Analyze scene structure and suggest optimizations" |
| **Medium** (pseudocode/patterns) | Preferred pattern exists, some variation OK | Code template with parameters to customize |
| **Low** (exact scripts) | Fragile operations, consistency critical | "Run exactly this script: `python scripts/migrate.py --verify`" |

For Unity-specific tasks, **err toward lower freedom** for tool calls and component setup (APIs are fragile), but **higher freedom** for planning and user interaction.

**5\. Use Consistent Terminology**

Pick one term and use it throughout:

* **Good:** Always say "Run Command tool" or always say "`run\_command`".  
* **Bad:** Mix "Run Command tool", "execute code", "RunCSharpCommand", "code execution tool".

**Naming Your Skill**

Use descriptive, action-oriented names with **lowercase letters, numbers, and hyphens** only.

**Good names:**

* `create-cinemachine-camera`  
* `scene-creator`  
* `optimize-shader-performance`  
* `setup-input-system`

**Avoid:**

* Vague: `helper`, `utils`, `tools`  
* Too generic: `camera`, `physics`, `ui`  
* Underscores or spaces: `create\_camera`, `Create Camera`  
* Avoid put “unity” in the name since it’s meaningless. 

Writing the Description

The description is **critical for skill discovery**. The agent uses it to choose the right skill from potentially dozens. Include both **what** and **when**.

**Good examples:**

```
description: Creates and configures Unity Cinemachine 3.1 cameras, including Basic, Third-Person, State-Driven, Spline Dolly, and Confiner setups. Use when the user asks about camera follow, camera orbit, or Cinemachine.
```

```
description: Generates and visually validates Unity scenes (2D/3D). Use when the user wants to create a new scene or set up an environment.
```

**Bad examples:**

```
description: Helps with cameras
description: Does stuff with scenes
```

**6\. Structuring the Body. Keep SKILL.md Under 500 Lines**

If your instructions exceed this, split into reference files. SKILL.md should be a guide that points to details, not an encyclopedia.Use Progressive Disclosure

Put the overview and common workflows in SKILL.md. Put detailed API references, code templates, and edge cases in separate files that the agent reads only when needed.

```
# My Skill
## Quick Start
[Core instructions here — loaded every time the skill activates]
## Detailed References
- **API specifics:** See [references/api-notes.md](references/api-notes.md)
- **Code templates:** See [resources/templates.md](resources/templates.md)
- **Troubleshooting:** See [references/common-issues.md](references/common-issues.md)
```

**7\. Keep References One Level Deep**

The agent may partially read deeply nested files. All reference files should link directly from SKILL.md.

* **Bad:** SKILL.md → advanced.md → details.md → actual info  
* **Good:** SKILL.md → api-notes.md, SKILL.md → templates.md, SKILL.md → troubleshooting.md

**8\. Add a Table of Contents for Long Reference Files**

For reference files over 100 lines, include a TOC at the top so the agent can see the full scope even when previewing.Workflow Pattern

**9\. Break complex tasks into numbered steps.** 

This is especially important for Unity tasks where operations must happen in a specific order.

```
# Workflow
### Step 1: Pre-flight Check
Verify prerequisites (packages installed, scene state).
### Step 2: Gather Information
Ask the user what's needed. List what to clarify.
### Step 3: Execute
Create objects, add components, configure properties.
### Step 4: Validate
Use visual tools (screenshots, captures) to verify the result.
### Step 5: Iterate (max 3 times)
If issues found, fix and re-validate. Don't exceed 3 iterations.
```

**10\. Feedback Loops**

For quality-critical tasks, include a validate-fix-repeat cycle:

```
### Validation Loop
1. Run validation (screenshot, compile check, etc.)
2. If issues found: fix and re-validate
3. Only proceed when validation passes
4. IMPORTANT: Never validate more than 3 times — ask the user for input instead
```

**11\. Template Pattern**

When output format matters, provide a template:

```
## Output Format

Use this structure for the C# script:

    namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor
    {
        internal class CommandScript : IRunCommand
        {
            public string Title => "[TITLE]";
            public string Description => "[DESCRIPTION]";
            public void Execute(ExecutionResult result)
            {
                // Your code here
            }
        }
    }
```

**12\. Common Anti-Patterns to Avoid**

| Anti-Pattern | Why It's Bad | Do This Instead |
| ----- | ----- | ----- |
| Explaining what PDFs/GameObjects/etc. are | LLM already knows | Skip the primer, go straight to specifics |
| Offering 5 different libraries for the same task | Causes decision paralysis | Recommend one default, mention alternatives only for specific cases |
| Time-sensitive instructions ("after August 2025...") | Becomes wrong silently | Document the current approach; put legacy patterns in a collapsed section |
| Including full API docs inline | Bloats SKILL.md | Move to reference files, link from SKILL.md |
| Mentioning other tools by name in descriptions | Hurts tool selection heuristics | Describe what the skill does, not what tools it uses |

# How to Use Tools in Skills

Skills can declare tools in the tools: frontmatter field. When the skill is activated, those tools become dynamically available to the agent.

1\. When to Use Tool Registration

* The task requires **specialized tools** beyond core agent's default toolset (e.g. run command tool)  
* You want to **scope tool availability** to when the skill is active.

2\. How It Works

1. Referring to tools:
    * If you author your skill as a SKILL.md file, you list tool IDs in the tools: field of your SKILL.md frontmatter.
    * If you author a skill via the C# API of `SkillDefinition` you add tools via the builder `WithTool()` methods.
2. When the agent activates your skill, SkillToolRetriever makes those tools available.  
3. When your skill's \<activated\_skill\> tag scrolls out of the conversation context, the tools are automatically removed.

3\. Available Tools

Tools must exist in the Unity Editor's capabilities response. The full list by category:

| Category | Tool ID | Description |
| ----- | ----- | ----- |
| **Smart Context** | `Unity.FindFiles` | Search for files in the project |
|  | `Unity.GetFileContent` | Read file contents |
|  | `Unity.FindSceneObjects` | Find GameObjects in scene hierarchy |
|  | `Unity.GetObjectData` | Get detailed object/component data |
|  | `Unity.GetConsoleLogs` | Get Unity console log entries |
|  | `Unity.GetProjectSettings` | Get project settings |
| **Camera & Screenshots** | `Unity.Camera.Capture` | Render image from a specific camera |
|  | `Unity.Camera.GetVisibleObjects` | Get objects visible from a camera |
|  | `Unity.EditorWindow.CaptureScreenshot` | Capture Unity Editor screenshot |
| **Assets** | `Unity.FindProjectAssets` | Search project assets by type/name |
|  | `Unity.GetTextAssetContent` | Read text asset content |
|  | `Unity.GetImageAssetContent` | Get image asset as base64 |
|  | `Unity.GetAssetLabels` | Get labels assigned to an asset |
| **Code Execution & Editing** | `Unity.RunCommand` | Execute C\# code in Unity |
|  | `Unity.RunCommandValidator` | Compile/validate C\# code |
|  | `Unity.CodeEdit` | Edit code files in project |
|  | `Unity.DeleteFile` | Delete a file from the project |
| **GameObject** (most are excluded by default — see exclusion list below) | `Unity.GameObject.GetComponentProperties` | Get component property values |
|  | `Unity.GameObject.GetSelection` | Get currently selected objects |
|  | `Unity.GameObject.GetGameObjectBounds` | Get object bounds/position |
|  | `Unity.GameObject.GetBuiltinAssets` | List built-in Unity assets |
| **Play Mode** | `Unity.EnterPlayMode` | Enter Unity Play Mode |
|  | `Unity.ExitPlayMode` | Exit Unity Play Mode |
| **UI Tools** | `Unity.FindPanelSettings` | Find PanelSettings asset |
|  | `Unity.FindOrCreateDefaultPanelSettings` | Find or create default PanelSettings |
|  | `Unity.GenerateUxmlSchemas` | Generate UXML schemas |
|  | `Unity.ValidateUIAsset` | Validate a UI asset |
|  | `Unity.SaveAndValidateUIAsset` | Save and validate UI asset |
|  | `Unity.GetUIAssetPreview` | Get preview of UI asset |
| **Project** | `Unity.GetUnityVersion` | Get Unity Editor version |
|  | `Unity.GetUnityDependenciesTool` | List installed packages/versions |
|  | `Unity.GetStaticProjectSettingsTool` | Get static project settings |
|  | `Unity.GetProjectData` | Get comprehensive project data |
|  | `Unity.SaveFile` | Save a text file to the project |
| **Asset Generation** | `Unity.AssetGeneration.GenerateAsset` | Generate an asset (texture, sprite, etc.) |
|  | `Unity.AssetGeneration.GetModels` | List available generation models |
|  | `Unity.AssetGeneration.GetCompositionPatterns` | Get composition pattern options |
|  | `Unity.AssetGeneration.ManageInterrupted` | Resume/cancel interrupted generations |
| **Package Management** | `Unity.PackageManager.GetData` | Get package manager data |
| **Input System** | `Unity.InputSystem.ReadStatus` | Read input system status |
|  | `Unity.InputSystem.WarnUnsupportedVersion` | Warn about unsupported version |
| **Profiler** | `Unity.Profiler.Initialize` | Initialize profiling session |
|  | `Unity.Profiler.GetFrameRangeTopTimeSummary` | Time summary across frames |
|  | `Unity.Profiler.GetFrameTopTimeSamplesSummary` | Top time samples in a frame |
|  | `Unity.Profiler.GetFrameSelfTimeSamplesSummary` | Top self-time samples in a frame |
|  | `Unity.Profiler.GetSampleTimeSummary` | Summary of a specific sample |
|  | `Unity.Profiler.GetBottomUpSampleTimeSummary` | Bottom-up time analysis |
|  | `Unity.Profiler.GetSampleTimeSummaryByMarkerPath` | Sample summary by marker path |
|  | `Unity.Profiler.GetRelatedSamplesTimeSummary` | Related samples on other threads |
|  | `Unity.Profiler.GetOverallGcAllocationsSummary` | Overall GC allocation summary |
|  | `Unity.Profiler.GetFrameGcAllocationsSummary` | GC allocations in a frame |
|  | `Unity.Profiler.GetFrameRangeGcAllocationsSummary` | GC allocations across frames |
|  | `Unity.Profiler.GetSampleGcAllocationSummary` | GC allocation of a sample |
|  | `Unity.Profiler.GetSampleGcAllocationSummaryByMarkerPath` | GC allocation by marker path |
|  | `Unity.Profiler.FindScriptFile` | Search for content in files |
|  | `Unity.Profiler.GetFileContentLineCount` | Get line count of a script |
|  | `Unity.Profiler.GetFileContent` | Get script file content |
|  | `Unity.Profiler.GetMarkerCode` | Get C\# code for a profiling marker |

Example skill that uses tools

```
name: analyze-profiler-data
description: Analyzes Unity Profiler data to identify performance bottlenecks.
  Use when the user asks about performance, frame rate, or profiling.
tools:
  - Unity.Profiler.Initialize
  - Unity.Profiler.GetFrameRangeTopTimeSummary
  - Unity.Profiler.GetFrameTopTimeSamplesSummary
```

# How to Add New Skills 

Please refer to [Skill Development](SKILL_DEVELOPMENT.md) for detailed workflow instructions
