# `Unity.AI.Image` Module

This document provides an overview of the `Unity.AI.Image` module, which contains the foundational logic for the AI-powered 2D image and sprite generation features within the Unity AI Toolkit.

This assembly is responsible for state management, backend communication, image data processing, and editor integration for generating and editing `Texture2D`, `Sprite`, and `Cubemap` assets. The module generates equirectangular images for cubemaps, which are then imported as `Cubemap` assets.

## 1. Features

This module provides a comprehensive set of features for generating and manipulating 2D images:

#### Generation & Refinement Modes
- **Text-to-Image**: Generate 2D images, sprites, and skybox cubemaps (as equirectangular images) from a descriptive text prompt.
- **Image-to-Image**: Guide the generation process using a reference image. This module supports multiple types of image guidance:
  - **Prompt Image**: A general reference for content and style.
  - **Style Image**: Transfers the artistic style of a reference image.
  - **Composition Image**: Uses the layout and composition of a reference.
  - **Pose Image**: Guides generation based on a character or object pose.
  - **Depth & Line Art**: Uses depth maps or line art as structural guides.
- **In-painting**: Edit specific regions of an image by providing a mask and a text prompt describing the desired change.
- **AI Upscaling**: Increase the resolution of an image using AI to enhance detail.
- **Background Removal**: Automatically remove the background from an image, leaving a transparent subject.
- **Recolor**: Change the color palette of an image based on a reference palette image.
- **Pixelation**: Apply a pixel art effect to an image with configurable block size and sampling modes.

#### Editor Integration & Workflow
- **Dedicated "Generate" Window**: A primary `EditorWindow` serves as the main user interface for interacting with the various generation and refinement modes.
- **Integrated Doodle Pad**: A simple, built-in drawing tool allows users to create masks and reference images (like line art or composition guides) directly within the editor, without needing an external application.
- **Inspector Integration**: Adds a "Generate" button directly to the `Texture2D`, `Sprite`, and `Cubemap` inspector headers and context menus for a seamless workflow.
- **Asset Creation**: Supports creating new, blank `Texture2D`, `Sprite`, or `Cubemap` assets that are immediately ready for generation.
- **Generation History**: Tracks and displays previously generated images for a given asset, allowing users to easily switch between variations.
- **Extensibility API**: Exposes a public API (`Unity.AI.Image.Interfaces`) allowing other tools and packages to integrate with and extend the image generation UI and functionality.

#### Backend & State Management
- **Resilient Backend Communication**: Manages all communication with the backend AI service, handling different endpoints for generation and transformation tasks. It includes robust error handling and retry logic, consistent with the other generator modules (`Sound`, `Material`, `Animate`).
- **Download Recovery**: Features a download recovery system that can resume interrupted generations when the project is reopened.
- **Redux Architecture**: Built on a predictable and maintainable Redux state management pattern, sharing its foundation with the other generator modules.

#### Performance & Caching
- **Asynchronous Pre-caching**: Intelligently pre-caches generated textures in the background to ensure a smooth user experience when browsing generation history. This leverages the shared caching system from `Unity.AI.Generators.UI`.
- **Efficient Image Handling**: Utilizes shared utilities for handling various image formats, resizing, and processing, ensuring consistency with the `Material` module.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Image` module. Understanding these concepts is crucial for any developer working on this system.

### Relationship to Other Generator Modules

The `Unity.AI.Image` module is a sophisticated implementation that builds upon the shared foundation provided by `Unity.AI.Generators.UI`. It shares many architectural patterns with the `Material`, `Sound`, and `Animate` modules, especially regarding its Redux state management, backend communication, and asset handling.

Its closest relative is the `Material` module, as both deal with `Texture2D` assets. However, the `Image` module is more complex due to its wide array of **refinement modes** (In-painting, Upscaling, etc.) and its extensive use of **image references** (Style, Pose, Composition, etc.).

### Foundational Architecture: Redux

The entire module is built around a **Redux** architecture, providing a single, predictable source of truth.

-   **Single Source of Truth**: All application state is stored in a single object tree within the `SharedStore`.
-   **State is Read-Only**: The only way to modify the state is to dispatch an **Action**.
-   **Changes are Pure**: State mutations are handled by pure functions called **Reducers**.

The data flow is unidirectional: `UI Interaction` -> `Dispatch(Action)` -> `Middleware (Thunk)` -> `Reducer` -> `New State` -> `UI Update (via Selectors)`.

### Key Namespaces and Responsibilities

#### `Unity.AI.Image.Services.Stores`
This is the heart of the Redux implementation.
-   **`Slices`**: Defines the state, actions, and reducers for each part of the application. The most complex is `GenerationSettingsSlice`, which manages the state for all the different refinement modes and their unique parameters (e.g., `pixelateSettings`, `upscaleFactor`, and the array of `imageReferences`).
-   **`Actions`**: Defines all state change requests. `AsyncThunk`s in the `Backend` sub-namespace handle the complex logic of preparing and sending requests to the correct AI service endpoints based on the currently selected `RefinementMode`.
-   **`Selectors`**: Contains pure functions for reading data from the state. A key selector is `SelectImageReferencesByRefinement`, which filters the many possible image reference types down to only those relevant for the current mode.
-   **`States`**: Defines the serializable data structures.
    -   **`RefinementMode`**: An enum that is central to the module's logic, dictating which UI is shown and which backend API is called.
    -   **`ImageReferenceSettings`**: A record that holds the state for a single image reference control (e.g., Style Image), including its asset, doodle data, strength, and active state. The main `GenerationSetting` state contains an array of these.

#### `Unity.AI.Image.Services.Utilities`
A collection of essential helper classes.
-   **`ImageReferenceTypeExtensions`**: A critical utility that uses custom attributes on the `ImageReferenceType` enum to provide metadata, such as the display name, the refinement modes it applies to, and which Redux selectors to use for its data. This allows for a data-driven approach to building the UI and managing state for the various image reference controls.
-   **`TextureUtils`**: Contains specialized image processing logic, such as the K-Means clustering algorithm used to generate a color palette for the **Recolor** feature.
-   **`GenerationRecovery`**: The implementation of the download recovery system, tailored to handle single-image results. It shares its base logic with the other generator modules via `Unity.AI.Generators.UI`.

#### `Unity.AI.Image.Windows` & `Unity.AI.Image.Panel`
-   **`TextureGeneratorWindow`**: The main `EditorWindow` that hosts the UI.
-   **`DoodleWindow`**: A separate `EditorWindow` for the Doodle Pad feature. It has its own minimal Redux state for managing layers, tools, and brush sizes, and communicates back to the main generator window when the user saves their changes.
-   **`AIPanel` and sub-panels**: The root UI component (`AIPanel`) acts as a router, showing and hiding different sub-panels (e.g., `InpaintingPanel`, `UpscalePanel`) based on the `RefinementMode` selected in the Redux state.

#### `Unity.AI.Image.Interfaces`
This namespace exposes a controlled public API for external integration. It allows other tools (like a potential 2D character editor) to embed and interact with the image generation panel. It provides a simplified, imperative layer over the declarative Redux architecture, with methods like `SetAIMode` and `AddGenerationSelectionHandler` that dispatch the appropriate Redux actions under the hood.
