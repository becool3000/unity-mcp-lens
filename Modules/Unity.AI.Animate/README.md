# `Unity.AI.Animate` Module

This document provides an overview of the `Unity.AI.Animate` module, which contains the primary logic for the AI-powered animation generation features within the Unity AI Toolkit.

This assembly contains the foundational logic for state management, backend communication, motion data processing, and editor integration for generating humanoid `AnimationClip` assets.

## 1. Features

This module provides a comprehensive set of features for generating and managing AI-driven animations:

#### Generation Capabilities
- **Text-to-Motion**: Generate humanoid animations directly from a descriptive text prompt.
- **Video-to-Motion**: Generate animations by analyzing a reference video file, extracting its motion from a generated FBX file, and applying it to a humanoid rig.

#### Animation Data Processing & Conversion
- **Multi-Format Motion Pipeline**: Parses motion data from multiple sources, including:
    - A custom JSON format from the AI service containing per-frame, per-joint transform data.
    - Animation data embedded within standard `.fbx` files.
- **Humanoid `AnimationClip` Export**: Converts the raw motion data from any supported format into a standard, game-ready Unity Humanoid `AnimationClip` by mapping poses to muscle values.
- **Framerate Resampling**: Automatically resamples animation data to a consistent 30 FPS, ensuring predictable playback speed.

#### Advanced Animation Post-Processing
- **Quaternion Continuity Correction**: Automatically corrects raw quaternion data to prevent flipping and ensure smooth, natural interpolation between keyframes.
- **Root Motion Normalization**: Provides utilities to normalize an animation's starting position and orientation, making it easy to use in-game (e.g., starting at the origin, facing forward).
- **Automatic Looping Analysis**: Includes a sophisticated asynchronous algorithm to analyze a clip and find the highest-quality loop points based on pose similarity and motion coverage.
- **Animation Clip Utilities**: Provides helper functions for cropping, copying, and manipulating `AnimationClip` data.

#### Editor Integration & Workflow
- **Dedicated "Generate" Window**: A primary `EditorWindow` serves as the main user interface for interacting with the generation service. (Note: UI elements are defined in a separate assembly).
- **Inspector Integration**: Adds a "Generate" button directly to the `AnimationClip` inspector header and context menus for a seamless workflow.
- **Asset Creation**: Supports creating new, blank `AnimationClip` assets that are immediately ready for generation.
- **Generation History**: Tracks and displays previously generated animations for a given asset.

#### Backend & State Management
- **Resilient Backend Communication**: Manages all communication with the backend AI service for quoting and generation, including robust error handling.
- **Download Recovery**: Features a download recovery system that can resume interrupted generations when the project is reopened.
- **Redux-like Architecture**: Built on a predictable and maintainable Redux-like state management pattern.

#### Performance & Caching
- **Two-Tier Caching System**: Implements a highly efficient caching system for generated `AnimationClip`s:
  - An in-memory cache for the current editor session.
  - A persistent on-disk database (`ScriptableSingleton`) to preserve cached clips between editor sessions.
- **Asynchronous Pre-caching**: Intelligently pre-caches animation clips in the background to ensure a smooth user experience when browsing generation history.
- **Thumbnail Rendering**: Generates thumbnail previews for animation clips using Unity's internal avatar rendering pipeline.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Animate` module. Understanding these concepts is crucial for any developer working on this system.

### Foundational Architecture: Redux-like

The entire module is built around a **Redux**-like architecture. This provides a single, predictable source of truth for the application's state.

-   **Single Source of Truth**: All application state (UI state, user settings, generation results, etc.) is stored in a single object tree within the `SharedStore`.
-   **State is Read-Only**: The only way to modify the state is to dispatch an **Action**, which is an object describing what happened.
-   **Changes are Pure**: State mutations are handled by pure functions called **Reducers**. A reducer takes the previous state and an action, and returns the *next* state.

The typical data flow is unidirectional:

`UI Interaction` -> `Dispatch(Action)` -> `Middleware (Thunk)` -> `Reducer` -> `New State` -> `UI Update (via Selectors)`

### Key Namespaces and Responsibilities

#### `Unity.AI.Animate.Services.Stores`
This is the heart of the Redux-like implementation.
-   **`Slices`**: Each slice (e.g., `GenerationSettingsSlice`, `GenerationResultsSlice`) defines the initial state, actions, and reducers for a specific part of the application state. This keeps the logic organized and modular.
-   **`Actions`**: Defines all possible state change requests. Asynchronous operations, like API calls, are handled by **`AsyncThunk`s**. These thunks can dispatch multiple actions and perform side effects. The `Backend` sub-namespace contains the thunks that communicate with the AI service SDK.
-   **`Selectors`**: Contains pure functions that extract and compute derived data from the state tree. The UI and other logic should **always** use selectors to read data, which allows for memoization and performance optimization.
-   **`States`**: Defines the serializable data structures (records and classes) that make up the application state.

#### `Unity.AI.Animate.Motion`
This namespace contains the logic for processing the raw **JSON motion data** returned by the AI service and converting it into a usable `AnimationClip`.
-   **`MotionResponse`**: Deserializes the incoming JSON payload (containing base64-encoded positions and rotations) into C# objects.
-   **`PoseModel`**: Represents a single frame's pose as local transforms for each joint.
-   **`Timeline`**: The processing pipeline for JSON data. It takes a `MotionResponse`, bakes the data into a sequence of `PoseModel`s, handles framerate resampling, and ensures quaternion continuity.
-   **`Timeline.ExportToHumanoidClip`**: Uses `HumanPoseHandler` to convert the baked timeline into a humanoid `AnimationClip` with muscle curves.

#### `Unity.AI.Animate.Services.SessionPersistence`
This handles the saving and loading of application state and user settings to survive editor restarts and domain reloads.
-   **`AnimateGeneratorSettings`**: A `ScriptableSingleton` that persists session data (like the last used model) to the project's `UserSettings` folder.
-   **`SharedStore`**: Provides the singleton instance of the Redux-like store. It applies middleware to automatically persist parts of the state when actions are dispatched.
-   **`ObjectPersistence` / `MemoryPersistence`**: A system that serializes a Redux-like state to another `ScriptableSingleton` to survive domain reloads within a single editor session.

#### `Unity.AI.Animate.Services.Utilities`
A collection of essential helper classes.
-   **`AnimationClipUtilities` & `AnimationClipResultExtensions`**: These classes contain the crucial logic for converting different input formats into a usable `AnimationClip`. The `AnimationClipFromResultAsync` method can differentiate between the backend's JSON format, imported `.fbx` animations, and standard `.anim` files, ensuring that any valid animation source can be processed and used within the generation history and caching system.
-   **`AnimationClipCache` & `AnimationClipDatabase`**: The two-tier caching system for generated clips. The database serializes clips to disk for persistence.
-   **`AnimationClipLoopUtils`**: Contains the advanced algorithms for finding loop points and normalizing root motion.
-   **`GenerationRecovery`**: Manages the JSON file that tracks interrupted downloads, allowing them to be resumed.
-   **`AnimationClipRenderingUtils`**: Renders animation thumbnails.

#### `Unity.AI.Animate.Windows`
Contains the `EditorWindow` classes that serve as the entry points for the user interface. These windows are responsible for creating the UI (defined in a separate assembly) and providing it with the Redux-like store and asset context.
