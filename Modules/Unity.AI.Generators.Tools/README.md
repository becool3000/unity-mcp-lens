# `Unity.AI.Generators.Tools` Module

This document provides an overview of the `Unity.AI.Generators.Tools` module. This module serves as a high-level, unified C# API for programmatic asset generation. It is designed to be the primary entry point for agentic systems like the **Unity AI Assistant** or **MCP**, allowing them to programmatically interact with the various generator modules (`Image`, `Material`, `Sound`, etc.) without needing to know their internal details.

## 1. Features & Core API

This module provides a simplified, powerful API for scripting asset generation workflows.

#### Unified Generation API
- **`AssetGenerators.GenerateAsync<TSettings>()`**: A single, static method to handle all types of asset generation. It uses a generic `GenerationParameters<TSettings>` struct to define the request, where `TSettings` can be `SpriteSettings`, `MaterialSettings`, `AnimationSettings`, etc.
- **`GenerationHandle<T>`**: The `GenerateAsync` method returns a `GenerationHandle<T>`. This object provides immediate, synchronous access to a placeholder asset and an awaitable `Task<T>` that resolves to the final generated asset upon completion. This enables non-blocking, asynchronous workflows.

#### Image References Support
- **Image References for Sprite Generation**: The `SpriteSettings` struct includes an `ImageReferences` array, allowing you to provide one or more reference images (such as a Texture2D) to guide the generation process.
    - **Current Limitation**: Only the first element (`ImageReferences[0]`) is currently used as a prompt image reference. This is for future extensibility; support for multiple references may be added later.
    - **Usage**: To use an image reference, assign a `Texture2D` (and optionally a label) to the first element of the `ImageReferences` array in your `SpriteSettings`. The referenced image will be used as a visual guide for the generated sprite, provided the selected model supports image references.

#### Multi-Modal & Refinement Support
- **Broad Asset Support**: Natively supports generating `AnimationClip`, `Texture2D` (Sprites), `Cubemap`, `AudioClip`, `Material`, `TerrainLayer`, and `GameObject` (Meshes).
- **Chained Operations**: The API supports chaining multiple operations. For example, you can request a sprite generation and specify that the background should be removed; the handle will automatically await the initial generation and then chain the background removal task. Similar workflows exist for PBR material generation and cubemap upscaling.
- **Direct Refinement**: Provides direct-access methods for refinement tasks, such as `RemoveSpriteBackgroundAsync()` and `UpscaleCubemapAsync()`, which operate on existing assets.

#### Recovery Support & Download Resume
- **Automatic Download Recovery**: The module provides a unified API for detecting and resuming interrupted asset downloads across all generator modalities (Image, Material, Sound, Animate, Mesh).
- **Usage**: Use `AssetGenerators.HasInterruptedDownloads()` to check for resumable downloads and `AssetGenerators.ResumeInterruptedDownloads()` to attempt recovery. These methods allow agentic systems or editor tools to present recovery options to users, automatically resume downloads on project load, or clean up interrupted states.

#### Model & Context Management
- **`AssetGenerators.GetAvailableModelsAsync()`**: A method to programmatically retrieve a list of available AI models, which can be used to provide a valid `ModelId` in the generation parameters.
- **UI Context Providers**: Includes a set of extension methods (`SetImageContext`, `SetMaterialContext`, etc.) to easily provide the necessary Redux store and asset context to a `VisualElement`, enabling the integration of generator UI panels into other editor windows.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Generators.Tools` module.

### The Facade Pattern

The `Unity.AI.Generators.Tools` module is a classic implementation of the **Facade** design pattern. It provides a simple, unified interface over a complex subsystem—in this case, the entire collection of individual generator modules.

- **Decoupling**: It decouples the client (e.g., an agentic system like the Unity AI Assistant) from the specific implementations of the `Image`, `Material`, `Sound`, `Animate`, and `Mesh` modules. The client only needs to know about the `AssetGenerators` API.
- **Simplification**: It simplifies the process of asset generation down to a single method call (`GenerateAsync`). The facade handles the complexity of determining which module's Redux store to use, what actions to dispatch, and how to configure the generation settings for the specific asset type.

### Relationship to Other Generator Modules

This module sits on top of all other generator modules. It has references to their Redux stores, actions, selectors, and utility classes. When `GenerateAsync` is called, it performs the following steps:

1.  **Parameter-Based Routing**: It inspects the `GenerationParameters<TSettings>` to determine the target asset type.
2.  **Placeholder Creation**: It uses the appropriate module's `AssetUtils` to create a blank placeholder asset in the project.
3.  **Store & Action Selection**: It selects the correct Redux store (e.g., `ImageStore.Store`, `MaterialStore.Store`).
4.  **State Configuration**: It dispatches a series of actions to the selected store to configure the generation (e.g., setting the prompt, model ID, refinement mode, and other parameters).
5.  **Execution**: It dispatches the main generation `AsyncThunk` (e.g., `generateImagesMainWithArgs`) and awaits its completion.
6.  **Result Handling**: It wraps the entire asynchronous process in a `GenerationHandle`, returning it to the caller. If chained operations are requested, it constructs a new task that sequences the required `AsyncThunk` calls.

By centralizing this logic, the `Tools` module ensures that programmatic generation behaves identically to user-driven generation from the dedicated UI windows, as they both ultimately operate on the same Redux stores and actions.

### Download Recovery Architecture

The recovery system is integrated into the facade. It queries each generator module for interrupted downloads and exposes unified methods for recovery. When `ResumeInterruptedDownloads()` is called, it creates a `GenerationHandle<Object>` for each resumable download, allowing asynchronous resumption and feedback collection. This ensures that asset generation workflows are robust against editor restarts or network interruptions.
