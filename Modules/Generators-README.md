# AI Generators: Foundational & Shared Modules

This document provides an overview of the foundational and shared modules that support the primary AI generator tools within the Unity AI Toolkit. These assemblies contain the underlying architecture, common utilities, and shared services that enable the functionality of the main generator modules.

For detailed information on the main generator modules, please see their respective README files:
-   [`/Unity.AI.Animate/README.md`](Unity.AI.Animate/README.md)
-   [`/Unity.AI.Image/README.md`](Unity.AI.Image/README.md)
-   [`/Unity.AI.Pbr/README.md`](Unity.AI.Pbr/README.md)
-   [`/Unity.AI.Mesh/README.md`](Unity.AI.Mesh/README.md)
-   [`/Unity.AI.Sound/README.md`](Unity.AI.Sound/README.md)

This document covers the following supporting modules:

1.  **`Unity.AI.Generators.Asset`**: Manages asset references and interactions with the `AssetDatabase`.
2.  **`Unity.AI.Generators.Contexts`**: A UI Toolkit context system for dependency injection.
3.  **`Unity.AI.Generators.Redux`**: The foundational Redux-like state management library.
4.  **`Unity.AI.Generators.Sdk`**: The client-side SDK for communicating with the backend AI services.
5.  **`Unity.AI.Generators.UI`**: Contains shared UI components, manipulators (e.g., drag-and-drop), and common Redux-like actions used by all generator windows.
6.  **`Unity.AI.Generators.IO`**: Shared file and asset I/O utilities (temporary asset lifecycle, resilient file operations, file-type detection, image format registry, and Windows long-path support).
7.  **`Unity.AI.ModelSelector`**: A shared UI and service for discovering and selecting AI models.

---

## 1. Features of Supporting Modules

This section outlines the key features provided by each supporting module.

### `Unity.AI.Generators.Asset`

This module provides a standardized way to interact with Unity assets.

-   **`AssetReference`**: A serializable `record` that uniquely identifies a project asset by its GUID. This is the primary way assets are referenced throughout the AI Toolkit's state management system, ensuring stability even if assets are moved or renamed.
-   **Asset Path Management**: Provides helper functions to consistently resolve paths, such as the path to an asset's dedicated "GeneratedAssets" subfolder.
-   **Asset Lifecycle Integration**: Hooks into `AssetModificationProcessor` to manage the "GeneratedAssets" folder when a source asset is deleted, preventing orphaned files.
-   **Labeling & Metadata**: Includes utilities for applying the `"UnityAIGenerated"` label to assets created by the toolkit, which is important for compliance and searchability.
-   **Specialized Asset Handling**: Contains wrappers (`BrushAssetWrapper`) to interact with internal Unity types like Terrain Brushes, ensuring that changes to generated textures are correctly propagated.

### `Unity.AI.Generators.Contexts`

This module implements a simple, lightweight dependency injection system for `UIToolkit`.

-   **Hierarchical Context**: Allows context values (like a Redux `Store` or an `AssetReference`) to be "provided" at a certain point in the UI hierarchy. Any descendant element can then "get" that context.
-   **`UseContext` Hook**: A reactive hook that allows a UI element to subscribe to changes in a context value, automatically re-rendering or executing a callback when the value changes. This is fundamental to the reactive nature of the generator UIs.

### `Unity.AI.Generators.Redux`

This is the foundational, in-house implementation of a Redux-like state management pattern. It is the architectural backbone of all AI generator modules.

-   **`Store`**: The central object that holds the entire application state.
-   **Actions & Reducers**: Provides the building blocks for defining state changes (`StandardAction`) and the pure functions (`Reducer`) that handle them.
-   **Slices**: A mechanism (`CreateSlice`) for organizing the state and its related logic into modular, self-contained units (e.g., a "generationSettings" slice, a "generationResults" slice).
-   **Middleware Pipeline**: An extensible middleware system for handling side effects.
-   **Asynchronous Thunks**: A powerful abstraction (`AsyncThunkCreator`) for managing asynchronous operations like API calls. It automatically handles dispatching `pending`, `fulfilled`, and `rejected` actions, simplifying async state management significantly. This pattern is used for all backend communication in the toolkit.
-   **RTK Query-inspired API**: Includes a high-level API (`EndpointBuilder`) for defining data-fetching and caching logic. It automates caching, invalidation, and re-fetching, reducing boilerplate for common API interactions.

### `Unity.AI.Generators.Sdk`

This module is the bridge between the Unity editor tools and the backend AI services.

-   **Typed SDK**: Provides a strongly-typed C# interface for all available backend endpoints, generated from the service's OpenAPI specification. This ensures that requests and responses are correctly structured.
-   **Authentication**: Implements an `IUnityAuthenticationTokenProvider` that integrates with the Unity Hub and `UnityConnect` to automatically handle user authentication and access token refreshing.
-   **HTTP Client Management**: A `HttpClientManager` singleton manages a shared `HttpClient` instance, optimizing performance by reusing connections and handling its lifecycle correctly within the editor.
-   **Resilient Communication**: All backend calls are wrapped with retry logic and standardized error handling.
-   **Tracing & Logging**: Integrates with Unity's analytics to provide trace IDs for backend requests, aiding in debugging. It also includes a configurable logging system for SDK-level messages.

### `Unity.AI.Generators.IO`

This module provides common, non-asset-specific I/O utilities that are consumed by the generator modules. Key responsibilities include:
- Temporary asset lifecycle management for creating and importing files into the project's Assets folder (used by drag-and-drop and formats Unity can't load directly).
- Resilient file operations (retries, exponential backoff) and Windows long-path handling.
- Advanced file-type detection by reading header bytes.
- An image format registry to support encoding/decoding multiple image formats (PNG, JPG, GIF, etc.) and integrating with native utilities where necessary.

(See `Modules/Unity.AI.Generators.IO/README.md` for full implementation details.)

### `Unity.AI.ModelSelector`

This module provides a shared, reusable UI and service for browsing, filtering, and selecting AI models.

-   **Centralized Model Store**: Manages a single, cached list of all available AI models for all modalities (Image, Sound, etc.).
-   **Model Discovery**: Handles the API calls to fetch the list of available models and their capabilities (e.g., which operations they support, like "TextPrompt" or "Upscale").
-   **Modal Selector Window**: A dedicated `EditorWindow` that can be opened by any generator module. It displays the list of models and allows the user to filter by modality, provider, tags, and other criteria.
-   **Favorites System**: Allows users to mark models as favorites for quick access. This state is persisted across editor sessions.
-   **Automatic Model Assignment**: Includes logic to automatically select the most appropriate model if the user hasn't made a choice, simplifying the user experience.

---

## 2. For Onboarding Developers: Architecture Overview

This section details how the supporting modules fit together to form the foundation of the AI Toolkit.

### The Architectural Stack

The modules are organized in a clear dependency hierarchy:

1.  **`Unity.AI.Generators.Redux` (Bottom Layer)**: The absolute foundation. It knows nothing about UI, assets, or the backend. It is a pure C# state management library.

2.  **`Unity.AI.Generators.Contexts` & `Unity.AI.Generators.UI`**: These build on Redux to connect it to `UIToolkit`. `Contexts` provides the dependency injection mechanism, while `UI` provides the reactive hooks (`Use`, `UseSelector`) and common UI components that all generator windows use.

3.  **`Unity.AI.Generators.Asset` & `Unity.AI.Generators.Sdk`**: These modules provide essential services. `Asset` is the standardized interface to the Unity `AssetDatabase`, and `Sdk` is the standardized interface to the backend. They are used by the main generator modules to perform their specific tasks.

4.  **`Unity.AI.ModelSelector`**: A specialized service module that uses Redux, the SDK, and the UI modules to provide a shared piece of functionality (the model selection window) to all other generators.

5.  **Main Generator Modules (Top Layer)**: `Unity.AI.Image`, `Unity.AI.Pbr`, `Unity.AI.Sound`, and `Unity.AI.Animate` sit at the top. They consume all the foundational modules to build their specific features. For example, the `Image` module uses:
    -   `Redux` for its state.
    -   `Contexts` and `UI` to build its UI panels.
    -   `Asset` to reference the `Texture2D` or `Cubemap` it's working on.
    -   `Sdk` to make the "generate image" API call.
    -   `ModelSelector` to allow the user to choose an image generation model.

### Key Concepts & Data Flow

-   **Everything is Driven by a Redux-like State**: The entire system is reactive. A user interaction (e.g., typing in a prompt) dispatches a Redux-like action. This action is processed by a reducer, which produces a new state. Selectors observing that part of the state then trigger UI updates.

-   **Asset Identification via `AssetReference`**: Throughout the Redux-like state, assets are **never** stored as direct `UnityEngine.Object` references. They are always stored as `AssetReference` records (containing the asset's GUID). This is critical for robust serialization and for preventing issues during domain reloads. A UI element uses a selector to get the `AssetReference` from the state, then uses `AssetDatabase.GUIDToAssetPath` to get the current path and load the object for display.

-   **Asynchronous Operations with Thunks**: All interactions with the outside world (file system, backend APIs) are handled by **`AsyncThunk`s**. A thunk is dispatched like a regular action, but the `ThunkMiddleware` intercepts it. The thunk can then perform its async work (e.g., `await httpClient.GetAsync(...)`) and dispatch `pending`, `fulfilled`, or `rejected` actions as it progresses. This keeps the reducers pure and the state changes predictable.

-   **Shared UI and Logic**: The `ModelSelector` is the prime example of a shared component. Instead of each generator module implementing its own model selection UI, they all open the same `ModelSelectorWindow`. The window gets its `Store` instance from the calling module via the `Contexts` system, allowing it to read and write to the shared part of the state tree where model information is stored. This architecture promotes code reuse and ensures a consistent experience across the entire toolkit.
