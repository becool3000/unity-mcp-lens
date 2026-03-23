---
uid: assistant-intro
---

# Unity AI Assistant

Unity AI Assistant (Assistant) is a generative artificial intelligence (AI) tool integrated into the Unity Editor. Use it to create assets, automate workflows, analyze performance, and perform project actions using natural-language prompts.

Assistant works in a conversation-based interface and operates in two modes depending on what you want to accomplish:

- [**Ask**](xref:assistant-modes#ask-mode) mode provides explanations, guidance, and project insights using read-only tools. It doesn't modify your scene or assets.
- [**Agent**](xref:assistant-modes#agent-mode) mode performs actions in your project, such as create assets, modify GameObjects, or update settings. All modifying actions require your approval and respect your permission settings.

Use **Ask** mode when you need information or suggestions. Use **Agent** mode when you want Assistant to perform changes in the Unity Editor.

## Create assets with Generators and Assistant

Assistant integrates with Unity [Generators](xref:overview) to create and refine visual, audio, and animation assets directly in your project.

With Generators and Assistant, you can:

- Create assets, such as sprites, textures, materials, and cubemaps.
- Generate 3D objects and terrain layers.
- Create and edit sound clips.
- Generate and refine animation clips.
- Edit audio clips using natural-language instructions.

You can use Generators directly for fine-grained control or use Assistant to guide the workflow using prompts and project context.

## Use skills to extend capabilities

Assistant includes skills, which are specialized modules that provide expert guidance and specific tools for a particular area of Unity development. In **Ask** mode, skills provide focused guidance. In **Agent** mode, skills can perform tool-enabled actions in the Unity Editor.

For example, a Cinemachine skill can assist with setting up virtual cameras, dolly tracks, and cinematic shots.

You can discover and invoke skills using natural-language prompts, such as:

- `What specialized skills do you have?`
- `List the specialized skills available in this project and what each can do.`
- `Use the Cinemachine skill to set up a dolly camera shot.`

## Monitor usage and manage access

Use the Unity Dashboard to [monitor your AI usage](https://cloud.unity.com/home/ai/usage), [manage your point balance](https://cloud.unity.com/home/ai/points), and [configure settings](https://cloud.unity.com/home/ai/settings) across your organization. This helps you understand how often Unity AI is used, control costs, and ensure the right team members have access to the appropriate tools.

For more information, refer to the [Unity Dashboard AI settings](https://docs.unity.com/en-us/ai) documentation.

## Install and open Assistant

To install and launch Assistant in the Unity Editor, refer to [Install Assistant](xref:install-assistant).

## Explore Assistant documentation

Use the following sections to learn how to work with Assistant across asset creation, automation, optimization, and integration workflows.

| Topic | Description |
| ----- | ----------- |
| [Install and configure Assistant](xref:install-config) | Install Assistant and prepare your Unity project. |
| [Create visual assets](xref:visual-assets) | Generate sprites, textures, materials, cubemaps, 3D objects, and terrain layers. |
| [Create and edit audio](xref:sound-intro) | Generate sound clips and edit audio directly in the Unity Editor. |
| [Create and apply animations](xref:animation-intro) | Generate animation clips and refine direction and looping. |
| [Work with assets in Unity Editor](xref:assets-landing) | Create, assign, and manage generated assets in your project. |
| [Automate the Unity Editor](xref:automate-landing) | Use UI Agent, screenshots, and checkpoints to automate workflows. |
| [Analyze and optimize](xref:assistant-profiler-integration) | Analyze profiler data and optimize project performance. |
| [Integrate models and tools](xref:integration-landing) | Configure Model Context Protocol (MCP), AI Gateway, and external agents. |
| [Manage Assistant](xref:manage-assistant) | Control history, messages, permissions, and feedback. |
| [Best practices](xref:bestpractice-landing) | Improve prompts, choose models, and follow recommended workflows. |
| [Troubleshooting issues with Assistant](xref:troubleshoot-landing) | Resolve common setup and workflow issues. |
| [Tool and interface reference](xref:tool-reference) | Review Assistant windows, tools, preferences, and references. |

## Additional resources

- [Use Ask mode and Agent mode](xref:assistant-modes)
- [About Generators](xref:overview)
- [Generators integration with Assistant](xref:generator-assistant-landing)
