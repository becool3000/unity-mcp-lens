# `Unity.AI.Pbr` Module

This document provides an overview of the `Unity.AI.Pbr` module, which contains the foundational logic for the AI-powered material and texture generation features within the Unity AI Toolkit.

This assembly is responsible for state management, backend communication, image data processing, and editor integration for generating PBR-ready `Material` and `TerrainLayer` assets.

## 1. Features

This module provides a comprehensive set of features for generating and managing AI-driven materials:

#### Generation & Refinement Modes
- **Text-to-Material**: Generate a full set of PBR (Physically Based Rendering) texture maps from a descriptive text prompt.
- **Image-to-Material (PBR Generation)**: Generate a complete PBR texture set (Normal, Height, AO, etc.) from a single source image, such as a photo or concept art.
- **AI Upscaling**: Increase the resolution of an existing texture map using AI, enhancing its detail and quality.
- **Reference-Based Generation**: Guide the generation process by providing a pattern or style reference image.

#### PBR Workflow & Automation
- **Automatic PBR Map Generation**: Intelligently creates missing PBR maps from the ones that were generated, ensuring a complete material. This includes:
  - Ambient Occlusion from Height maps.
  - Smoothness from Roughness maps (and vice-versa).
  - Mask maps and Metallic/Smoothness maps required by specific Unity render pipelines (URP, HDRP).
- **Shader Property Auto-Detection**: Automatically detects and maps the generated texture maps to the correct texture slots in the target material's shader (e.g., `_BaseMap`, `_MainTex`, `_BumpMap`). This works across different shaders and render pipelines.
- **Automatic Normal Map Import**: Correctly configures generated normal maps upon import by setting their `Texture Type` to "Normal map".

#### Broad Asset Support
- **`UnityEngine.Material`**: Directly generate and apply textures to standard Material assets.
- **`UnityEngine.TerrainLayer`**: Directly generate and apply textures to Terrain Layer assets for use with Unity's terrain system.
- **Shader Graph Support**: Initiate generation directly from a Shader Graph asset in the Project window, which creates a new Material instance to work with.

#### Editor Integration & Workflow
- **Dedicated "Generate" Window**: A primary `EditorWindow` serves as the main user interface for interacting with the generation service.
- **Inspector Integration**: Adds a "Generate" button directly to the `Material` and `TerrainLayer` inspector headers and context menus for a seamless workflow.
- **Asset Creation**: Supports creating new, blank `Material` or `TerrainLayer` assets that are immediately ready for generation.
- **Generation History**: Tracks and displays previously generated materials for a given asset, allowing users to easily switch between variations.

#### Backend & State Management
- **Resilient Backend Communication**: Manages all communication with the backend AI service for quoting and generation, including robust error handling and retry logic.
- **Multi-File Download Recovery**: Features a download recovery system that can resume interrupted generations, which is critical for materials that consist of multiple texture files.
- **Redux Architecture**: Built on a predictable and maintainable Redux state management pattern.

#### Performance & Caching
- **Asynchronous Pre-caching**: Intelligently pre-caches generated textures in the background to ensure a smooth user experience when browsing generation history.
- **Efficient In-Memory Previews**: Generates temporary, in-memory `Material` instances for previewing, avoiding unnecessary disk writes and asset imports until a generation is explicitly applied.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Pbr` module. Understanding these concepts is crucial for any developer working on this system.

### Foundational Architecture: Redux

The entire module is built around a **Redux** architecture. This provides a single, predictable source of truth for the application's state.

-   **Single Source of Truth**: All application state (UI state, user settings, generation results, etc.) is stored in a single object tree within the `SharedStore`.
-   **State is Read-Only**: The only way to modify the state is to dispatch an **Action**, which is an object describing what happened.
-   **Changes are Pure**: State mutations are handled by pure functions called **Reducers**. A reducer takes the previous state and an action, and returns the *next* state.

The typical data flow is unidirectional:

`UI Interaction` -> `Dispatch(Action)` -> `Middleware (Thunk)` -> `Reducer` -> `New State` -> `UI Update (via Selectors)`

### Key Namespaces and Responsibilities

#### `Unity.AI.Pbr.Services.Stores`
This is the heart of the Redux implementation.
-   **`Slices`**: Each slice (e.g., `GenerationSettingsSlice`, `GenerationResultsSlice`) defines the initial state, actions, and reducers for a specific part of the application state.
-   **`Actions`**: Defines all possible state change requests. Asynchronous operations, like API calls, are handled by **`AsyncThunk`s**. The `Backend` sub-namespace contains the thunks that communicate with the AI service SDK (e.g., `generateMaterials`, `downloadMaterials`).
-   **`Selectors`**: Contains pure functions that extract and compute derived data from the state tree. A key selector is `GetTexturePropertyName`, which contains the logic for auto-detecting shader texture slots.
-   **`States`**: Defines the serializable data structures. The most important is **`MaterialResult`**, which is a dictionary mapping a `MapType` enum to a `TextureResult`. This structure represents a full PBR material as a collection of its texture maps.

#### `Unity.AI.Pbr.Services.Utilities`
A collection of essential helper classes that contain most of the procedural and asset-handling logic.
-   **`MaterialAdapterFactory` & `IMaterialAdapter`**: This is a critical architectural pattern (Adapter/Strategy) that allows the system to treat `UnityEngine.Material` and `UnityEngine.TerrainLayer` objects identically. It abstracts away their different APIs for getting and setting textures, simplifying the rest of the codebase.
-   **Map Generation Utilities**: A set of classes (`AmbientOcclusionUtils`, `MaskMapUtils`, `MetallicSmoothnessUtils`, `SmoothnessUtils`) that contain the procedural logic for deriving missing PBR maps from existing ones using shaders and `ComputeBuffer`s.
-   **`MaterialResultExtensions`**: A central utility class for handling `MaterialResult` objects. Its responsibilities include:
    -   Saving a `MaterialResult` to the project, which involves creating a dedicated subfolder for the texture maps.
    -   Generating temporary, in-memory `Material` instances for previewing in the UI.
    -   Orchestrating the automatic post-processing steps, such as generating missing maps.
-   **`GenerationRecovery`**: Manages the JSON file that tracks interrupted downloads. It is designed to handle the multi-file nature of material generations.
-   **`ImageResizeUtilities`**: Contains logic for pre-processing images (resizing, stripping alpha) before they are sent to the backend service to meet API requirements.

#### `Unity.AI.Pbr.Services.SessionPersistence`
This handles the saving and loading of application state and user settings to survive editor restarts and domain reloads.
-   **`MaterialGeneratorSettings`**: A `ScriptableSingleton` that persists session data (like last used models and shader property mappings) to the project's `UserSettings` folder.
-   **`SharedStore`**: Provides the singleton instance of the Redux store. It applies middleware to automatically persist parts of the state when actions are dispatched.
-   **`ObjectPersistence` / `MemoryPersistence`**: A system that serializes the Redux state to another `ScriptableSingleton` to survive domain reloads within a single editor session.

#### `Unity.AI.Pbr.Windows`
Contains the `EditorWindow` classes that serve as the entry points for the user interface.
-   **`MaterialGeneratorWindow`**: The main window for generation settings and viewing results.
-   **`MaterialGeneratorInspectorButton` & `TerrainLayerGeneratorInspectorButton`**: Static classes that inject "Generate" buttons into the inspector headers and project context menus for their respective asset types.undefined
