# `Unity.AI.Mesh` Module

This document provides an overview of the `Unity.AI.Mesh` module, which contains the foundational logic for AI-powered 3D model generation within the Unity AI Toolkit.

This assembly is responsible for state management, backend communication, 3D model data processing, and editor integration for generating `GameObject` prefabs from text prompts.

## 1. Features

This module provides a comprehensive set of features for generating and managing AI-driven 3D models:

#### Generation Capabilities
- **Text-to-Mesh**: Generate static 3D models as `GameObject` prefabs directly from a descriptive text prompt.
- **Multi-Format Support**: Natively handles and imports industry-standard file formats like `.fbx` and `.glb`, automatically converting them into ready-to-use prefabs.
- **Extensible Backend Architecture**: Features a pluggable service architecture (`IGenerationService`) that allows for the integration of multiple third-party model generation backends.

#### Editor Integration & Workflow
- **Dedicated "Generate" Window**: A primary `EditorWindow` serves as the main user interface for interacting with the generation service, setting prompts, and viewing results.
- **Inspector Integration**: Adds a "Generate" button directly to the `GameObject` prefab inspector header and a "Generate 3D Object" option to the `Assets/Create` and `GameObject/3D Object` context menus for a seamless workflow.
- **Asset Creation**: Supports creating new, blank `GameObject` prefabs that are immediately ready for generation.
- **Generation History**: Tracks and displays previously generated models for a given asset, allowing for easy comparison and reuse.

#### UI & Visualization
- **Interactive Turntable Previews**: Features a high-performance, animated turntable preview for all generated models, allowing users to inspect them from all angles directly in the UI.
- **Asynchronous Preview Caching**: Turntable animations are pre-rendered and cached to disk in the background, ensuring a smooth and responsive user experience when browsing the generation history.

#### Backend & State Management
- **Resilient Backend Communication**: Manages all communication with backend AI services for quoting and generation, including robust error handling and retry logic.
- **Download Recovery**: Features a download recovery system that can resume interrupted generations when the project is reopened.
- **Redux Architecture**: Built on a predictable and maintainable Redux state management pattern, consistent with all other generator modules.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Mesh` module. Understanding these concepts is crucial for any developer working on this system.

### Foundational Architecture: Redux

The entire module is built around a **Redux** architecture. This provides a single, predictable source of truth for the application's state.

-   **Single Source of Truth**: All application state (UI state, user settings, generation results, etc.) is stored in a single object tree within the `SharedStore`.
-   **State is Read-Only**: The only way to modify the state is to dispatch an **Action**.
-   **Changes are Pure**: State mutations are handled by pure functions called **Reducers**.

The typical data flow is unidirectional:

`UI Interaction` -> `Dispatch(Action)` -> `Middleware (Thunk)` -> `Reducer` -> `New State` -> `UI Update (via Selectors)`

### Key Namespaces and Responsibilities

#### `Unity.AI.Mesh.Services.Stores`
This is the heart of the Redux implementation.
-   **`Slices`**: Each slice (e.g., `GenerationSettingsSlice`, `GenerationResultsSlice`) defines the initial state, actions, and reducers for a specific part of the application state.
-   **`Actions`**: Defines all possible state change requests. The `Backend` sub-namespace contains the `AsyncThunk`s that communicate with the AI services.
-   **`Selectors`**: Contains pure functions that extract and compute derived data from the state tree. The UI and other logic should **always** use selectors to read data.
-   **`States`**: Defines the serializable data structures (records and classes) that make up the application state. `MeshResult` is the core state object representing a single generated model.

#### `Unity.AI.Mesh.Services.Stores.Actions.Backend`
This namespace contains the logic for communicating with different generation backends.
-   **`IGenerationService`**: An interface that defines the contract for a backend service, requiring methods for `GetModelsAsync`, `QuoteAsync`, `GenerateAsync`, and `DownloadAsync`.
-   **`GenerationServices`**: A static class that discovers and registers all `IGenerationService` implementations at startup.
-   **`GenerationBackendMuxer`**: A critical `AsyncThunk` that acts as a router. It inspects the selected model ID and forwards the generation request to the appropriate registered `IGenerationService`.

#### `Unity.AI.Mesh.Services.Utilities`
A collection of essential helper classes.
-   **`MeshResultExtensions`**: A critical utility class containing the logic for handling different incoming model formats. It manages the process of importing `.fbx` or `.glb` files, configuring their `ModelImporter` settings, and creating a final `.prefab` asset.
-   **`MeshPreviewCache` & `MeshRenderingUtils`**: This pair of classes manages the turntable preview visualization. `MeshRenderingUtils` uses a `PreviewRenderUtility` to render frames of the model at different rotations, while `MeshPreviewCache` manages the asynchronous generation and disk caching of these frames for performance.
-   **`GenerationRecovery`**: Manages the JSON file that tracks interrupted downloads, allowing them to be resumed.
-   **`AssetUtils`**: Provides helpers for creating the initial blank `GameObject` prefab that serves as the target for generation.

#### `Unity.AI.Mesh.Services.SessionPersistence`
This handles the saving and loading of application state and user settings.
-   **`MeshGeneratorSettings`**: A `ScriptableSingleton` that persists session data to the project's `UserSettings` folder.
-   **`SharedStore`**: Provides the singleton instance of the Redux store and applies middleware to automatically persist state.

#### `Unity.AI.Mesh.Windows`
Contains the `EditorWindow` classes and the logic for integrating with the Unity Editor.
-   **`MeshGeneratorWindow`**: The main window for generation settings and viewing results.
-   **`MeshGeneratorInspectorButton`**: The static class that injects "Generate" buttons into the `GameObject` prefab inspector and project context menus.
