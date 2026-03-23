# `Unity.AI.Sound` Module

This document provides an overview of the `Unity.AI.Sound` module, which contains the foundational logic for the AI-powered audio generation features within the Unity AI Toolkit.

This assembly is responsible for state management, backend communication, audio data processing, and editor integration for generating and editing `AudioClip` assets.

## 1. Features

This module provides a comprehensive set of features for generating and managing AI-driven audio:

#### Generation Capabilities
- **Text-to-Sound**: Generate sound effects, ambient noise, and other audio directly from a descriptive text prompt.
- **Sound-to-Sound**: Generate variations of an existing sound by providing a reference `AudioClip`.
- **Microphone Input**: Use a live microphone recording as a reference for sound-to-sound generation.

#### Audio Editing & Post-Processing
- **Destructive Trimming & Enveloping**: A dedicated "Trim" window allows users to visually specify start/end points and apply a volume envelope. The editing process is interactive and non-destructive, but the changes are permanently applied to the asset file upon saving.
- **Volume Envelope Editing**: Visually edit the volume of an audio clip over its duration using a multi-point envelope editor, allowing for custom fades and ADSR-like shaping.
- **Automatic Post-Processing**: Intelligently auto-trims leading and trailing silence from generated audio clips to create tight, game-ready sound effects.
- **WAV Encoding**: Provides robust utilities for encoding raw audio sample data into the `.wav` format.

#### Editor Integration & Workflow
- **Dedicated "Generate" Window**: A primary `EditorWindow` serves as the main user interface for interacting with the generation service. (Note: UI elements are defined in a separate assembly).
- **Dedicated "Trim" Window**: A secondary `EditorWindow` provides a focused interface for trimming and applying volume envelopes.
- **Inspector Integration**: Adds "Generate" and "Trim" buttons directly to the `AudioClip` inspector header and context menus for a seamless workflow.
- **Asset Creation**: Supports creating new, blank `AudioClip` assets that are immediately ready for generation.
- **Generation History**: Tracks and displays previously generated audio clips for a given asset.

#### UI & Visualization
- **Interactive Oscillogram**: Features a high-performance, interactive waveform (oscillogram) display with pan and zoom capabilities.
- **Visual Envelope & Trim Markers**: Renders the trim markers and volume envelope control points directly onto the oscillogram for intuitive editing.

#### Backend & State Management
- **Resilient Backend Communication**: Manages all communication with the backend AI service for quoting and generation, including robust error handling and retry logic.
- **Download Recovery**: Features a download recovery system that can resume interrupted generations when the project is reopened.
- **Redux Architecture**: Built on a predictable and maintainable Redux state management pattern.

#### Performance & Caching
- **Asynchronous Pre-caching**: Intelligently pre-caches audio clips in the background to ensure a smooth user experience when browsing generation history.
- **Efficient Oscillogram Rendering**: The waveform visualization is highly optimized, generating and caching textures from audio samples on-demand.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Sound` module. Understanding these concepts is crucial for any developer working on this system.

### Foundational Architecture: Redux

The entire module is built around a **Redux** architecture. This provides a single, predictable source of truth for the application's state.

-   **Single Source of Truth**: All application state (UI state, user settings, generation results, etc.) is stored in a single object tree within the `SharedStore`.
-   **State is Read-Only**: The only way to modify the state is to dispatch an **Action**, which is an object describing what happened.
-   **Changes are Pure**: State mutations are handled by pure functions called **Reducers**. A reducer takes the previous state and an action, and returns the *next* state.

The typical data flow is unidirectional:

`UI Interaction` -> `Dispatch(Action)` -> `Middleware (Thunk)` -> `Reducer` -> `New State` -> `UI Update (via Selectors)`

### Key Namespaces and Responsibilities

#### `Unity.AI.Sound.Services.Stores`
This is the heart of the Redux implementation.
-   **`Slices`**: Each slice (e.g., `GenerationSettingsSlice`, `GenerationResultsSlice`) defines the initial state, actions, and reducers for a specific part of the application state. This keeps the logic organized and modular.
-   **`Actions`**: Defines all possible state change requests. Asynchronous operations, like API calls, are handled by **`AsyncThunk`s**. These thunks can dispatch multiple actions and perform side effects. The `Backend` sub-namespace contains the thunks that communicate with the AI service SDK.
-   **`Selectors`**: Contains pure functions that extract and compute derived data from the state tree. The UI and other logic should **always** use selectors to read data, which allows for memoization and performance optimization.
-   **`States`**: Defines the serializable data structures (records and classes) that make up the application state.

#### `Unity.AI.Sound.Services.Utilities`
A collection of essential helper classes.
-   **`AudioClipExtensions`**: A critical utility class containing methods for:
    -   Playing audio clips in the editor via a temporary `AudioSource`.
    -   Encoding raw `float[]` sample data into a `.wav` file stream.
    -   Applying volume envelopes and trim markers to audio data for in-memory playback previews and for destructively saving the modified audio to a `.wav` file.
    -   Analyzing audio data to find silence or peak amplitude.
-   **`AudioClipOscillogramUtils` & `...RenderingUtils`**: This pair of classes manages the waveform visualization. `Utils` generates a `Texture2D` from audio samples (with caching), while `RenderingUtils` uses a custom shader to draw that texture into the UI.
-   **`AudioClipMarkerRenderingUtils`**: Uses a custom shader to render the trim markers and envelope control points on top of the oscillogram texture.
-   **`GenerationRecovery`**: Manages the JSON file that tracks interrupted downloads, allowing them to be resumed.
-   **`AudioClipCache`**: An in-memory cache for `AudioClip` objects loaded from disk or web URLs. It's persisted across domain reloads via a `ScriptableSingleton`.

#### `Unity.AI.Sound.Services.SessionPersistence`
This handles the saving and loading of application state and user settings to survive editor restarts and domain reloads.
-   **`SoundGeneratorSettings`**: A `ScriptableSingleton` that persists session data (like the last used model and microphone) to the project's `UserSettings` folder.
-   **`SharedStore`**: Provides the singleton instance of the Redux store. It applies middleware to automatically persist parts of the state when actions are dispatched.
-   **`ObjectPersistence` / `MemoryPersistence`**: A system that serializes the Redux state to another `ScriptableSingleton` to survive domain reloads within a single editor session.

#### `Unity.AI.Sound.Windows`
Contains the `EditorWindow` classes that serve as the entry points for the user interface.
-   **`SoundGeneratorWindow`**: The main window for generation settings and viewing results.
-   **`SoundEnvelopeWindow`**: The dedicated window for trimming and editing the volume envelope.
-   **`SoundGeneratorInspectorButton`**: The static class that injects "Generate" and "Trim" buttons into the `AudioClip` inspector and project context menus.

#### `Unity.AI.Sound.Services.Undo`
-   **`SoundEnvelopeUndoManager`**: A `ScriptableObject` that tracks user changes within the Trim window (moving markers, adding/deleting envelope points) to support Unity's native Undo/Redo system.
