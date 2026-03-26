# AI Assistant Skill Creation Guidelines

> **Canonical version:** The full, up-to-date skill creation guidelines live in the
> [muse-skills wiki — Skill Creation Guidelines](https://github.cds.internal.unity3d.com/unity/muse-skills/wiki/Skill-Creation-Guidelines).
>
> This file is kept as a quick-reference pointer. **Always refer to the wiki for the latest content.**

## Summary

The wiki page covers:

- **How skills work at runtime** — activation flow, progressive disclosure
- **SKILL.md format** — YAML frontmatter fields (`name`, `description`, `required_packages`, `tools`, `enabled`)
- **Skill structure** — folder layout, references, resources
- **How to write effective skills** — 12 best-practice rules (token budgets, success criteria, naming, body structure, feedback loops, anti-patterns, etc.)
- **How to use tools in skills** — tool registration, the full available-tools table (with "Needs `tools:` reference?" column), and examples
- **Skill Patterns Cookbook** — concrete patterns for decision trees, preventing unwanted actions, ask-before-acting, passive analysis, and more

## Related docs in this folder

- [SKILL_DEVELOPMENT.md](SKILL_DEVELOPMENT.md) — Local setup, C# API, editor UI testing
- [INTEGRATION.md](INTEGRATION.md) — AI Assistant integration guide
- [TOOL_GUIDELINES.md](TOOL_GUIDELINES.md) — Guidelines for creating new tools
- [SAMPLE.md](SAMPLE.md) — Sample code and usage examples
