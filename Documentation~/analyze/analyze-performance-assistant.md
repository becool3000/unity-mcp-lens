---
uid: analyze-performance-assistant
---

# Analyze profiler data in Assistant

Use Assistant to analyze profiling data and investigate performance issues in your project.

Assistant works with profiling sessions that already exist in your project. If no profiling data is available, Assistant guides you to record a session with the Profiler.

To analyze performance with Assistant, follow these steps:

1. Open the **Assistant** window in the Unity Editor.
1. Submit a performance-related question, for example:

   - `Why is my game running slowly?`
   - `What is causing high CPU usage in my project?`

   Assistant checks for the available profiling data.
2. When prompted, review the options that Assistant presents:

   - **Open Profiler**
   - **Analyze** (for available profiling sessions)

3. Continue based on the profiling data available in your project:

   - If saved profiling sessions are available: Assistant lists the saved profiling sessions. Select **Analyze** next to the session you want to review. Assistant analyzes the session and provides a summary that highlights timing patterns, memory behavior, and potential performance concerns.

   - If only an active profiling session is available: If no saved sessions exist but an active profiling session is open in the Profiler, Assistant automatically uses the active session and displays the analysis results.

   - If both saved and active sessions are available: Assistant lists both the options. To analyze the active session, select **Analyze** next to it. You don't need to save the session first.

   - If no profiling data is available: Assistant indicates that it needs profiling data and displays the **Open Profiler** option. Select it, record a profiling session in the Profiler, then rerun your query. Unity saves the profiling session as `.data` files in your project.

4. Review the analysis results in the **Assistant** window and use the guidance provided to investigate the performance issues in your project.

## Additional resources

* [Analyze a Unity Profiler capture](xref:use-profiler)
* [Open and use Assistant](xref:get-started)