# `Unity.AI.Generators.UI` Module

This document provides an overview of the `Unity.AI.Generators.UI` module. This assembly contains shared, non-asset-specific logic and utilities that support all AI generator modules within the Unity AI Toolkit, such as `Unity.AI.Image`, `Unity.AI.Pbr`, `Unity.AI.Sound`, and `Unity.AI.Animate`.

It serves as a foundational layer for common UI behaviors and backend communication patterns.

## 1. Features

This module provides a common set of features and utilities leveraged by all other AI generator modules.

#### Shared Redux Actions & Payloads
- **Common Generation Actions**: Defines a standard set of Redux actions for managing the lifecycle of a generation task (`setGenerationAllowed`, `setGenerationProgress`, `addGenerationFeedback`, etc.). This ensures a consistent state management pattern across all generator types.
- **Backend Message Dispatching**: Provides a suite of standardized helper functions (`Dispatch...Message`) for dispatching user-facing feedback based on responses from the AI service SDK. This centralizes error and status reporting for API calls, validation, and downloads.

#### Common UI Manipulators & Controls
- **File System Watcher**: A robust `FileSystemWatcher` manipulator that automatically detects changes in a generator's output directory and triggers a UI refresh. It includes a fallback polling mechanism for environments where file system events are unreliable.
- **Drag-and-Drop System**: A comprehensive system (`ExternalFileDragDrop`) for dragging generated assets from the generator UI directly into the Project window or Scene view. It handles the creation of temporary assets, caching, and final asset placement.
- **Specialized Manipulators**: Includes various `UIToolkit` manipulators for common UI needs, such as:
  - `Draggable`: A generic manipulator for creating draggable UI elements.
  - `SpinnerManipulator`: A simple animated spinner icon for indicating progress.
  - `ScaleToFit...`: Manipulators that automatically resize `Image` and `ObjectField` previews to fit their containers while preserving aspect ratio.

#### Cross-Cutting Concerns
- **Search Integration**: Implements a custom `SearchProvider` that allows users to easily find all AI-generated assets within their project using the Unity Search window. It supports searching by filename, prompt, and other metadata.
- **Undo System**: A generic `AssetUndoManager` class that provides a foundation for handling Undo/Redo operations for asset replacements in all generator windows.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Generators.UI` module. This assembly is designed to be a dependency for all other generator modules, providing them with a common toolkit of reusable components.

### Relationship to Other Generator Modules

This module is the "common ground" for `Unity.AI.Image`, `Unity.AI.Pbr`, `Unity.AI.Sound`, and `Unity.AI.Animate`. It contains logic that is not specific to any single asset type. For example, the process of showing a progress bar, handling a download failure, or managing a drag-and-drop operation is the same regardless of whether the asset is a `Texture2D` or an `AudioClip`. By centralizing this logic, we avoid code duplication and ensure a consistent user experience.

### Key Namespaces and Responsibilities

#### `Unity.AI.Generators.UI.Actions` & `...Payloads`
This namespace defines the **common language** for Redux actions that all generator modules use.
- **`GenerationActions`**: Contains the fundamental actions for tracking the state of a generation (e.g., `setGenerationProgress`). It also includes a suite of extension methods for the `IStoreApi` interface, which are used by `AsyncThunk`s in other modules to dispatch standardized, user-friendly feedback messages based on API responses. For example, `DispatchFailedBatchMessage` takes a generic SDK response and translates it into a consistent error message in the UI.
- **`Payloads`**: Defines the data structures that are sent along with the common actions. For instance, `GenerationProgressData` is a standardized way to report progress for any type of generation.

#### `Unity.AI.Generators.UI` (Root Namespace)
This namespace contains the reusable `UIToolkit` manipulators and components.
- **`GenerationFileSystemWatcher`**: A critical component for keeping the generation history view up-to-date. It listens for file system events in the output directory of a generator. When a new file is created (i.e., a download completes), it dispatches an action to rebuild the list of generated assets. It's designed to be resilient, with a debounce mechanism to prevent excessive updates and a fallback to periodic polling if the native `FileSystemWatcher` fails.
- **`DragExternalFileManipulator` & `ExternalFileDragDrop`**: These classes work together to enable dragging assets from the UI into the project.
  - `DragExternalFileManipulator` is attached to a UI element (like a generation tile). On mouse down and drag, it calls `ExternalFileDragDrop.StartDragFromExternalPath`.
  - `StartDragFromExternalPath` creates a **temporary copy** of the asset inside the project's `Assets` folder (using `TemporaryAssetUtilities`). This is necessary because Unity's drag-and-drop system can only operate on assets that are already in the `AssetDatabase`.
  - The system then tracks the drag operation. If the asset is dropped successfully into the Project window or Scene, `OnProjectWindowDragPerform` is called to move the temporary asset to its final destination. If the drag is canceled, the temporary asset is cleaned up.

#### `Unity.AI.Generators.UI.Utilities`
This is a collection of essential helper classes that provide foundational services.
- **`AssetUndoManager<T>`**: A generic base class for implementing Undo/Redo functionality. When a user applies a generated asset, an instance of this class is recorded with `Undo.RecordObject`. It saves a copy of the original asset to a temporary file. If the user performs an Undo, the `OnAfterDeserialize` callback is triggered, which restores the asset from the temporary file. Each generator module (e.g., `Unity.AI.Pbr`) provides a concrete implementation of this class.
