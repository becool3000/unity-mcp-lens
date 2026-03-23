## Overview

The Unity AI Assistant framework provides three primary mechanisms to integrate with you features:

**Agent Skill**: Skills are **modular** capabilities that extend the core agent functionality. Packaged as instructions and metadata, skills teach the AI specific workflows, reducing repetition and **specializing the agent for domain-specific tasks** without requiring hardcoded logic. The main advantages of agent skill are that we benefit from the agent using **multiple skills in combination for complex tasks** within the Editor, and that skills are activated using **progressive disclosure**, which means only the meta data part of the skills are always loaded in the context window.

Refer to [Skill Development](SKILL_DEVELOPMENT.md) for details, and for best practices [Skill Creation Guidelines](SKILL_CREATION_GUIDELINES.md).

**Custom Agent**: Agents define the identity, expertise, and operational boundaries of the AI. By configuring a system prompt, you establish a specific persona, their goals, constraints, and the relevant engine context they need to assist users effectively. The system prompt is always loaded in the context window of that custom agent's conversation. Custom agents **work in isolation** with their own tools, thus they should be preferred for **one-shot tasks**.

Refer to [Custom Agent / Integration](INTEGRATION.md) for details.

**Agent Tool**: Tools are structured commands with parameters that the LLM uses to request detailed information or execute actions. They are exposed C# methods that allow the agent to **interact directly with your domain's internal or public APIs**. Skills also benefit from all available tools and we can define skill-specific tools.

Refer to [Agent Tool](TOOL_GUIDELINES.md) for details.
