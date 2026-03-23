---
uid: troubleshoot-dll-conflicts
---

# Troubleshooting DLL compilation conflicts

Solve an issue with script compilation errors caused by dynamic link library (DLL) dependency conflicts in Assistant.

Occasionally, projects that include their own third-party dependencies can encounter script compilation errors when those dependencies conflict with DLLs included in the Assistant package. These conflicts can prevent scripts from compiling or stop the Unity Editor from entering Play mode.

You can resolve these conflicts by excluding specific Assistant DLLs from compilation using scripting define symbols. This approach lets you control which DLLs are active in your project when dependency collisions occur.

## Symptoms

You might encounter one or more of the following issues:

- Script compilation errors after you install Assistant.
- Assembly or namespace conflict errors, involving third-party libraries.
- The Unity Editor fails to compile scripts or enter Play mode.

Error messages typically reference duplicate types, assemblies, or incompatible versions of the same dependency.

## Cause

Assistant includes several third-party DLLs to support its features. In some projects, these DLLs can conflict with versions of the same libraries already included in the project and cause compilation errors.

## Resolution

You can temporarily exclude a conflicting Assistant DLL by using a scripting define symbol.

1. In the Unity Editor, go to the **Project** window.
1. In the **Packages** section, expand the **Assistant** package.
1. Open the **Plugins** folder and identify the DLL causing the compilation conflict.
1. Select the DLL and, in the **Inspector**, locate **Define Constraints**.
1. Identify the constraint that starts with an exclamation mark (`!`). This scripting define symbol disables the DLL when present.
1. Go to **Edit** > **Project Settings** > **Player** > **Other Settings** > **Script Compilation** > **Scripting Define Symbols**.
1. Add the same define from step 5, but without the leading exclamation mark.
1. Select **Apply**.

   Unity recompiles the project and excludes the targeted DLL from compilation.

9. Repeat these steps for each conflicting DLL as needed.

## Additional resources

- [Manage Assistant](xref:manage-assistant)
- [Configure Assistant permissions and preferences](xref:preferences)