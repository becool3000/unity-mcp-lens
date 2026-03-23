# `Unity.AI.Generators.IO` Module

This document provides an overview of the `Unity.AI.Generators.IO` module. This assembly contains shared, non-asset-specific logic and utilities that support all AI generator modules within the Unity AI Toolkit, such as `Unity.AI.Image`, `Unity.AI.Pbr`, `Unity.AI.Sound`, and `Unity.AI.Animate`.

It serves as a foundational layer for asset handling.

## 1. Features

This module provides a common set of features and utilities leveraged by all other AI generator modules.

#### Asset & File Utilities
- **Temporary Asset Management**: A robust system (`TemporaryAssetUtilities`) for creating, importing, and managing temporary assets in a dedicated project folder. This is crucial for handling files that need to be imported into the `AssetDatabase` before they can be used (e.g., for drag-and-drop or for formats like `.gif` that Unity can't load at runtime).
- **Long Path Support (Windows)**: Wraps all `System.IO` file operations to automatically handle Windows long path limitations (`\\?\` prefix), preventing errors with deeply nested project structures.
- **Resilient File Operations**: Implements retry logic with exponential backoff for file copy operations to handle transient file locks or sharing violations, which are common in an editor environment.
- **Advanced File Type Detection**: Provides utilities to reliably detect the type of a file (PNG, JPG, WAV, FBX, etc.) by reading its header bytes, rather than relying on its extension.
- **Image Format Registry**: A centralized registry (`ImageFileTypeSupport`) for handling various image formats. It supports detecting format type, getting dimensions, and encoding `Texture2D` objects into different formats (e.g., PNG, JPG, GIF) by leveraging the native library bundled with the Unity Editor.

---

## 2. For Onboarding Developers: Architecture Overview

This section details the internal architecture of the `Unity.AI.Generators.IO` module. This assembly is designed to be a dependency for all other generator modules, providing them with a common toolkit of reusable components.

### Relationship to Other Generator Modules

This module is the "common ground" for `Unity.AI.Image`, `Unity.AI.Pbr`, `Unity.AI.Sound`, and `Unity.AI.Animate`. It contains logic that is not specific to any single asset type. For example, the process of showing a progress bar, handling a download failure, or managing a drag-and-drop operation is the same regardless of whether the asset is a `Texture2D` or an `AudioClip`. By centralizing this logic, we avoid code duplication and ensure a consistent user experience.

### Key Namespaces and Responsibilities

#### `Unity.AI.Generators.IO.Utilities`
This is a collection of essential helper classes that provide foundational services.
- **`TemporaryAssetUtilities`**: Manages the lifecycle of temporary assets. When a file from an external path (like the `Library` folder where downloads are saved) needs to be used as a Unity asset, this utility copies it into a temporary folder within `Assets`, imports it, and returns a `TemporaryAsset` object that implements `IDisposable`. When the `TemporaryAsset.Scope` is disposed, the temporary file and folder are automatically cleaned up. This is the backbone of the drag-and-drop system.
- **`FileIO`**: A static wrapper around `System.IO` file operations. Its primary purpose is to transparently handle **Windows long path limitations** by automatically adding the `\\?\` prefix to paths when necessary. This prevents `PathTooLongException` errors in deeply nested projects. It also includes resilient async copy methods with retry logic.
- **`ImageFileTypeSupport` & `ImageFileUtilities`**: A comprehensive system for handling different image formats. Because Unity's runtime `Texture2D.LoadImage` only supports a limited set of formats (and can't handle things like EXIF orientation in JPEGs), this system provides a more robust solution.
  - `ImageFileTypeSupport` is a registry that maps file extensions to a set of functions for detecting the format, getting dimensions, and encoding textures. For formats not natively supported by Unity (like GIF), it uses P/Invoke to call the library that is bundled with the Unity Editor.
  - `ImageFileUtilities` provides higher-level functions that use this registry.
