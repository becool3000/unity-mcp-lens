---
uid: ai-gateway-intro
---

# AI Gateway overview

Run third-party coding agents in Assistant so you can use your preferred provider inside the Unity Editor.

AI Gateway adds an agent selector to the **Assistant** window. Agents run locally on your machine using your provider credentials. Assistant routes your prompts to the selected agent and returns responses in the same conversation.

AI Gateway supports agents designed for coding workflows, including CLI-based assistants. Depending on your license and configuration, these agents can chat and generate code or perform actions directly in Unity using Model Context Protocol (MCP) tools.

To connect Assistant to external MCP servers that you host or configure, refer to the [Model Context Protocol overview](xref:mcp-landing) documentation.

AI Gateway provides a secure, Unity-managed environment to run supported agents, and control permissions and entitlements. Use AI Gateway to perform the following tasks:

- Ask questions about your project.
- Request code changes.
- Generate scripts and assets.
- Debug and fix issues.
- Modify GameObjects, scenes, and prefabs.

Use AI Gateway when you want:

- A preferred third-party agent inside Unity.
- Provider-specific models and commands in the Assistant workflow.
- A tool-enabled workflow that lets the agent act directly in the Unity Editor.

## Additional resources

- [Configure AI Gateway](xref:ai-gateway-get-started)
- [Use third-party agents in Assistant](xref:use-third-party-agents)