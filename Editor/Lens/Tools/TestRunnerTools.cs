#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tools
{
    enum UnityTestsRunMode
    {
        EditMode,
        PlayMode
    }

    sealed class UnityTestsRunParams
    {
        [McpDescription("Unity Test Runner mode: EditMode or PlayMode.", Required = false, Default = "EditMode", EnumType = typeof(UnityTestsRunMode))]
        public string Mode { get; set; } = "EditMode";

        [McpDescription("Optional test assembly name to include, for example Assembly-CSharp-Editor.", Required = false)]
        public string Assembly { get; set; }

        [McpDescription("Optional test assembly names to include.", Required = false)]
        public string[] Assemblies { get; set; } = Array.Empty<string>();

        [McpDescription("Optional test name or full-name filter. Use TestNames for multiple explicit filters.", Required = false)]
        public string Filter { get; set; }

        [McpDescription("Optional test names or full names to include.", Required = false)]
        public string[] TestNames { get; set; } = Array.Empty<string>();

        [McpDescription("Optional test category to include.", Required = false)]
        public string Category { get; set; }

        [McpDescription("Optional test categories to include.", Required = false)]
        public string[] Categories { get; set; } = Array.Empty<string>();

        [McpDescription("Maximum wall-clock wait in milliseconds.", Required = false, Default = 120000)]
        public int TimeoutMs { get; set; } = 120000;

        [McpDescription("Optional timeout alias in seconds. When greater than zero, this overrides TimeoutMs.", Required = false, Default = 0)]
        public int TimeoutSeconds { get; set; } = 0;

        [McpDescription("Maximum failed test rows to return inline.", Required = false, Default = 20)]
        public int MaxFailedTests { get; set; } = 20;

        [McpDescription("Maximum first assertion messages to return inline.", Required = false, Default = 5)]
        public int MaxAssertionMessages { get; set; } = 5;

        [McpDescription("Capture Unity console entries emitted during the test run.", Required = false, Default = true)]
        public bool CaptureConsoleDelta { get; set; } = true;
    }

    static class TestRunnerTools
    {
        const string ToolName = "Unity.Tests.Run";

        const string Description = @"Runs Unity Test Runner tests and returns compact counts, failures, assertion messages, and console delta.

Args:
    Mode: EditMode or PlayMode. Defaults to EditMode.
    Assembly/Assemblies: Optional test assembly filters.
    Filter/TestNames: Optional test name or full-name filters.
    Category/Categories: Optional test category filters.
    TimeoutMs/TimeoutSeconds: Wall-clock timeout.
    CaptureConsoleDelta: Include Unity console entries emitted during the run.

Returns:
    Dictionary with success/message/data. Data contains pass/fail/skip/inconclusive counts, failed test names, first assertion messages, runner availability, timeout/cancel status, and consoleDelta.";

        [McpTool(ToolName, Description, "Run Unity Tests", Groups = new[] { "project", "validation" }, EnabledByDefault = true)]
        public static async Task<object> Run(UnityTestsRunParams parameters)
        {
            parameters ??= new UnityTestsRunParams();
            var timing = new ToolOperationTiming(ToolName, "run_unity_tests", PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(parameters, Formatting.None)));
            var stopwatch = Stopwatch.StartNew();
            object response;
            bool success = false;
            string errorKind = null;

            try
            {
                var request = Normalize(parameters);
                if (!TryNormalizeMode(request.Mode, out string mode, out string modeError))
                {
                    errorKind = "invalid_test_mode";
                    response = Response.Error("INVALID_TEST_MODE", new
                    {
                        errorKind,
                        error = modeError,
                        allowedModes = new[] { "EditMode", "PlayMode" },
                        elapsedMs = stopwatch.ElapsedMilliseconds
                    });
                }
                else if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    errorKind = "editor_busy";
                    response = Response.Error("UNITY_TESTS_EDITOR_BUSY", new
                    {
                        errorKind,
                        message = "Unity tests were not started because the editor is compiling, updating, building, or changing play mode.",
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        editorState = EditorToolStateHelpers.BuildEditorState()
                    });
                }
                else
                {
                    var runResult = await ExecuteTestRunAsync(request, mode, stopwatch);
                    success = runResult.Success;
                    errorKind = runResult.ErrorKind;
                    response = success
                        ? Response.Success("Unity tests passed.", runResult.Data)
                        : Response.Error(runResult.ErrorCode, runResult.Data);
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                response = Response.Error("UNITY_TESTS_RUN_FAILED", new
                {
                    errorKind,
                    error = ex.Message,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    editorState = EditorToolStateHelpers.BuildEditorState()
                });
            }

            using (timing.Measure("result_shaping"))
            {
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static UnityTestsRunParams Normalize(UnityTestsRunParams parameters)
        {
            parameters.Mode = string.IsNullOrWhiteSpace(parameters.Mode) ? "EditMode" : parameters.Mode.Trim();
            parameters.Assemblies = NormalizeValues(parameters.Assembly, parameters.Assemblies);
            parameters.TestNames = NormalizeValues(parameters.Filter, parameters.TestNames);
            parameters.Categories = NormalizeValues(parameters.Category, parameters.Categories);
            parameters.TimeoutMs = NormalizeTimeoutMs(parameters.TimeoutMs, parameters.TimeoutSeconds);
            parameters.MaxFailedTests = Math.Clamp(parameters.MaxFailedTests, 0, 200);
            parameters.MaxAssertionMessages = Math.Clamp(parameters.MaxAssertionMessages, 0, 50);
            return parameters;
        }

        static async Task<TestRunToolResult> ExecuteTestRunAsync(UnityTestsRunParams parameters, string mode, Stopwatch stopwatch)
        {
            ConsoleCursorSnapshot consoleBefore = parameters.CaptureConsoleDelta ? ConsoleCursorDelta.Capture() : null;
            var types = TestRunnerReflection.TryResolve();
            if (!types.Available)
            {
                var unavailableData = BuildBaseData(parameters, mode, stopwatch, false, timedOut: false, cancellationRequested: false, null, Array.Empty<TestFailureRow>(), Array.Empty<string>(), Array.Empty<object>(), types.Error, consoleBefore);
                return TestRunToolResult.CreateFailure("UNITY_TEST_FRAMEWORK_UNAVAILABLE", "test_framework_unavailable", unavailableData);
            }

            var callbackErrors = new List<string>();
            var callbackFailures = new List<TestFailureRow>();
            int testFinishedCount = 0;
            object finalResult = null;
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            object callbacks = null;
            object api = null;
            object executeReturn = null;
            bool registered = false;

            try
            {
                api = Activator.CreateInstance(types.ApiType);
                object settings = BuildExecutionSettings(types, parameters, mode);
                callbacks = CreateCallbackProxy(types.CallbacksInterfaceType, (methodName, args) =>
                {
                    try
                    {
                        if (string.Equals(methodName, "TestFinished", StringComparison.OrdinalIgnoreCase) && args.Length > 0)
                        {
                            testFinishedCount++;
                            var result = args[0];
                            if (IsFailedResult(result))
                            {
                                var failure = BuildFailureRow(result);
                                if (callbackFailures.Count < parameters.MaxFailedTests)
                                    callbackFailures.Add(failure);
                            }
                        }
                        else if (string.Equals(methodName, "RunFinished", StringComparison.OrdinalIgnoreCase))
                        {
                            finalResult = args.Length > 0 ? args[0] : null;
                            completion.TrySetResult(finalResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        callbackErrors.Add($"{methodName}: {ex.GetType().Name}: {ex.Message}");
                    }
                });

                TestRunnerReflection.InvokeRegisterCallbacks(api, types, callbacks);
                registered = true;
                executeReturn = TestRunnerReflection.InvokeExecute(api, types, settings);

                bool timedOut = await WaitForCompletionAsync(completion.Task, parameters.TimeoutMs);
                bool cancellationRequested = false;
                if (timedOut)
                    cancellationRequested = TestRunnerReflection.TryCancelRun(api, out _);

                if (finalResult == null && completion.Task.IsCompletedSuccessfully)
                    finalResult = completion.Task.Result;

                var failureRows = CollectFailureRows(finalResult, parameters.MaxFailedTests);
                if (failureRows.Count == 0)
                    failureRows = callbackFailures;

                var counts = BuildCounts(finalResult, failureRows.Count, testFinishedCount);
                var failedTestNames = failureRows
                    .Select(row => string.IsNullOrWhiteSpace(row.fullName) ? row.name : row.fullName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .Take(parameters.MaxFailedTests)
                    .ToArray();
                var firstAssertionMessages = failureRows
                    .Where(row => !string.IsNullOrWhiteSpace(row.message))
                    .Take(parameters.MaxAssertionMessages)
                    .Select(row => new
                    {
                        name = string.IsNullOrWhiteSpace(row.fullName) ? row.name : row.fullName,
                        message = row.message,
                        stackTraceFirstLine = row.stackTraceFirstLine
                    })
                    .ToArray();

                object data = BuildBaseData(
                    parameters,
                    mode,
                    stopwatch,
                    true,
                    timedOut,
                    cancellationRequested,
                    finalResult,
                    failureRows.ToArray(),
                    callbackErrors.ToArray(),
                    firstAssertionMessages,
                    null,
                    consoleBefore,
                    counts,
                    failedTestNames,
                    executeReturn);

                if (timedOut)
                    return TestRunToolResult.CreateFailure("UNITY_TESTS_TIMEOUT", "test_timeout", data);

                if (callbackErrors.Count > 0)
                    return TestRunToolResult.CreateFailure("UNITY_TESTS_CALLBACK_ERROR", "callback_error", data);

                if (counts.failCount > 0)
                    return TestRunToolResult.CreateFailure("UNITY_TESTS_FAILED", "tests_failed", data);

                return TestRunToolResult.CreateSuccess(data);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                object data = BuildBaseData(parameters, mode, stopwatch, true, timedOut: false, cancellationRequested: false, finalResult, callbackFailures.ToArray(), callbackErrors.ToArray(), Array.Empty<object>(), ex.InnerException.Message, consoleBefore);
                return TestRunToolResult.CreateFailure("UNITY_TESTS_RUN_FAILED", ex.InnerException.GetType().Name, data);
            }
            catch (Exception ex)
            {
                object data = BuildBaseData(parameters, mode, stopwatch, true, timedOut: false, cancellationRequested: false, finalResult, callbackFailures.ToArray(), callbackErrors.ToArray(), Array.Empty<object>(), ex.Message, consoleBefore);
                return TestRunToolResult.CreateFailure("UNITY_TESTS_RUN_FAILED", ex.GetType().Name, data);
            }
            finally
            {
                if (registered && api != null && callbacks != null)
                    TestRunnerReflection.TryUnregisterCallbacks(api, types, callbacks, out _);
            }
        }

        static bool TryNormalizeMode(string input, out string normalized, out string error)
        {
            normalized = string.IsNullOrWhiteSpace(input) ? "EditMode" : input.Trim();
            error = null;

            if (string.Equals(normalized, "Edit", StringComparison.OrdinalIgnoreCase))
                normalized = "EditMode";
            else if (string.Equals(normalized, "Play", StringComparison.OrdinalIgnoreCase))
                normalized = "PlayMode";

            if (string.Equals(normalized, "EditMode", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "EditMode";
                return true;
            }

            if (string.Equals(normalized, "PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "PlayMode";
                return true;
            }

            error = $"Unsupported test mode '{input}'. Use EditMode or PlayMode.";
            return false;
        }

        static int NormalizeTimeoutMs(int timeoutMs, int timeoutSeconds)
        {
            int resolved = timeoutSeconds > 0
                ? Math.Min(timeoutSeconds, 30 * 60) * 1000
                : timeoutMs;
            return Math.Clamp(resolved <= 0 ? 120000 : resolved, 1000, 30 * 60 * 1000);
        }

        static string[] NormalizeValues(string single, string[] values)
        {
            var normalized = new List<string>();
            AddTokens(normalized, single);
            if (values != null)
            {
                foreach (string value in values)
                    AddTokens(normalized, value);
            }

            return normalized
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        static void AddTokens(List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (string token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    values.Add(trimmed);
            }
        }

        static object BuildExecutionSettings(TestRunnerReflection types, UnityTestsRunParams parameters, string mode)
        {
            object filter = Activator.CreateInstance(types.FilterType);
            object testMode = Enum.Parse(types.TestModeType, mode);
            TrySetMember(filter, "testMode", testMode);
            TrySetMember(filter, "assemblyNames", parameters.Assemblies);
            TrySetMember(filter, "testNames", parameters.TestNames);
            TrySetMember(filter, "categoryNames", parameters.Categories);

            Array filters = Array.CreateInstance(types.FilterType, 1);
            filters.SetValue(filter, 0);

            var settingsCtor = types.ExecutionSettingsType.GetConstructor(new[] { types.FilterType.MakeArrayType() });
            if (settingsCtor != null)
                return settingsCtor.Invoke(new object[] { filters });

            object settings = Activator.CreateInstance(types.ExecutionSettingsType);
            TrySetMember(settings, "filters", filters);
            return settings;
        }

        static object CreateCallbackProxy(Type callbacksInterface, Action<string, object[]> onInvoke)
        {
            var createMethod = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => method.Name == "Create" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);
            object proxy = createMethod
                .MakeGenericMethod(callbacksInterface, typeof(TestRunnerCallbackProxy))
                .Invoke(null, null);
            ((TestRunnerCallbackProxy)proxy).OnInvoke = onInvoke;
            return proxy;
        }

        static async Task<bool> WaitForCompletionAsync(Task completionTask, int timeoutMs)
        {
            Task winner = await Task.WhenAny(completionTask, Task.Delay(timeoutMs));
            return winner != completionTask;
        }

        static TestRunCounts BuildCounts(object finalResult, int failureRowsCount, int testFinishedCount)
        {
            int passCount = GetInt(finalResult, "PassCount", "passCount");
            int failCount = Math.Max(GetInt(finalResult, "FailCount", "failCount"), failureRowsCount);
            int skipCount = GetInt(finalResult, "SkipCount", "skipCount");
            int inconclusiveCount = GetInt(finalResult, "InconclusiveCount", "inconclusiveCount");
            int testCount = GetInt(finalResult, "TestCaseCount", "TestCount", "TotalCount", "testCaseCount", "testCount", "totalCount");
            int counted = passCount + failCount + skipCount + inconclusiveCount;
            if (testCount <= 0)
                testCount = counted > 0 ? counted : testFinishedCount;

            return new TestRunCounts
            {
                testCount = testCount,
                passCount = passCount,
                failCount = failCount,
                skipCount = skipCount,
                inconclusiveCount = inconclusiveCount
            };
        }

        static object BuildBaseData(
            UnityTestsRunParams parameters,
            string mode,
            Stopwatch stopwatch,
            bool runnerAvailable,
            bool timedOut,
            bool cancellationRequested,
            object finalResult,
            IReadOnlyList<TestFailureRow> failedTests,
            IReadOnlyList<string> callbackErrors,
            IReadOnlyList<object> firstAssertionMessages,
            string runnerError,
            ConsoleCursorSnapshot consoleBefore,
            TestRunCounts counts = null,
            string[] failedTestNames = null,
            object executeReturn = null)
        {
            counts ??= BuildCounts(finalResult, failedTests?.Count ?? 0, 0);
            object consoleDelta = ConsoleCursorDelta.BuildDelta(
                parameters.CaptureConsoleDelta,
                consoleBefore,
                ToolName,
                new { kind = "unity_tests_run_console_delta", mode, timedOut });

            return new
            {
                mode,
                requested = new
                {
                    assemblies = parameters.Assemblies ?? Array.Empty<string>(),
                    filter = parameters.Filter,
                    testNames = parameters.TestNames ?? Array.Empty<string>(),
                    category = parameters.Category,
                    categories = parameters.Categories ?? Array.Empty<string>(),
                    timeoutMs = parameters.TimeoutMs
                },
                runnerAvailable,
                runnerError,
                timedOut,
                cancellationRequested,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                testCount = counts.testCount,
                passCount = counts.passCount,
                failCount = counts.failCount,
                skipCount = counts.skipCount,
                inconclusiveCount = counts.inconclusiveCount,
                runStatus = GetString(finalResult, "TestStatus", "Status"),
                runResultState = GetString(finalResult, "ResultState", "State"),
                failedTestNames = failedTestNames ?? Array.Empty<string>(),
                failedTests = failedTests ?? Array.Empty<TestFailureRow>(),
                firstAssertionMessages = firstAssertionMessages ?? Array.Empty<object>(),
                callbackErrors = callbackErrors ?? Array.Empty<string>(),
                executeReturn,
                consoleDelta,
                editorState = EditorToolStateHelpers.BuildEditorState()
            };
        }

        static List<TestFailureRow> CollectFailureRows(object result, int maxRows)
        {
            var rows = new List<TestFailureRow>();
            if (result == null || maxRows <= 0)
                return rows;

            CollectFailureRows(result, rows, maxRows, new HashSet<object>());
            return rows;
        }

        static void CollectFailureRows(object result, List<TestFailureRow> rows, int maxRows, HashSet<object> visited)
        {
            if (result == null || rows.Count >= maxRows || visited.Contains(result))
                return;

            visited.Add(result);
            var children = GetEnumerable(result, "Children", "children")?.Cast<object>().Where(item => item != null).ToArray();
            if (children != null && children.Length > 0)
            {
                foreach (object child in children)
                {
                    CollectFailureRows(child, rows, maxRows, visited);
                    if (rows.Count >= maxRows)
                        return;
                }

                return;
            }

            if (IsFailedResult(result))
                rows.Add(BuildFailureRow(result));
        }

        static bool IsFailedResult(object result)
        {
            if (result == null)
                return false;

            if (GetInt(result, "FailCount", "failCount") > 0 && GetEnumerable(result, "Children", "children") == null)
                return true;

            string status = GetString(result, "TestStatus", "Status");
            string state = GetString(result, "ResultState", "State");
            string label = GetString(result, "Label");
            return ContainsFailureSignal(status) || ContainsFailureSignal(state) || ContainsFailureSignal(label);
        }

        static bool ContainsFailureSignal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static TestFailureRow BuildFailureRow(object result)
        {
            string stackTrace = GetString(result, "StackTrace", "stackTrace");
            return new TestFailureRow
            {
                name = GetString(result, "Name", "name"),
                fullName = GetString(result, "FullName", "fullName"),
                status = GetString(result, "TestStatus", "Status"),
                resultState = GetString(result, "ResultState", "State"),
                label = GetString(result, "Label"),
                message = GetString(result, "Message", "message"),
                stackTraceFirstLine = FirstLine(stackTrace),
                duration = GetDouble(result, "Duration", "duration")
            };
        }

        static string FirstLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').FirstOrDefault();
        }

        static object GetMemberValue(object target, params string[] names)
        {
            if (target == null || names == null)
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            Type type = target.GetType();
            foreach (string name in names)
            {
                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return property.GetValue(target);
                    }
                    catch
                    {
                    }
                }

                var field = type.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(target);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        static bool TrySetMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
                return false;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            Type type = target.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }

            return false;
        }

        static string GetString(object target, params string[] names)
        {
            object value = GetMemberValue(target, names);
            return value?.ToString();
        }

        static int GetInt(object target, params string[] names)
        {
            object value = GetMemberValue(target, names);
            if (value == null)
                return 0;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        static double GetDouble(object target, params string[] names)
        {
            object value = GetMemberValue(target, names);
            if (value == null)
                return 0;

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return 0;
            }
        }

        static IEnumerable GetEnumerable(object target, params string[] names)
        {
            object value = GetMemberValue(target, names);
            return value is string ? null : value as IEnumerable;
        }

        sealed class TestRunnerCallbackProxy : DispatchProxy
        {
            public Action<string, object[]> OnInvoke { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                OnInvoke?.Invoke(targetMethod?.Name ?? string.Empty, args ?? Array.Empty<object>());
                return null;
            }
        }

        sealed class TestRunToolResult
        {
            public bool Success { get; private set; }
            public string ErrorCode { get; private set; }
            public string ErrorKind { get; private set; }
            public object Data { get; private set; }

            public static TestRunToolResult CreateSuccess(object data) => new()
            {
                Success = true,
                ErrorCode = null,
                ErrorKind = null,
                Data = data
            };

            public static TestRunToolResult CreateFailure(string errorCode, string errorKind, object data) => new()
            {
                Success = false,
                ErrorCode = errorCode,
                ErrorKind = errorKind,
                Data = data
            };
        }

        sealed class TestRunCounts
        {
            public int testCount { get; set; }
            public int passCount { get; set; }
            public int failCount { get; set; }
            public int skipCount { get; set; }
            public int inconclusiveCount { get; set; }
        }

        sealed class TestFailureRow
        {
            public string name { get; set; }
            public string fullName { get; set; }
            public string status { get; set; }
            public string resultState { get; set; }
            public string label { get; set; }
            public string message { get; set; }
            public string stackTraceFirstLine { get; set; }
            public double duration { get; set; }
        }

        sealed class TestRunnerReflection
        {
            public bool Available { get; private set; }
            public string Error { get; private set; }
            public Type ApiType { get; private set; }
            public Type ExecutionSettingsType { get; private set; }
            public Type FilterType { get; private set; }
            public Type TestModeType { get; private set; }
            public Type CallbacksInterfaceType { get; private set; }

            public static TestRunnerReflection TryResolve()
            {
                var resolved = new TestRunnerReflection
                {
                    ApiType = FindType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi"),
                    ExecutionSettingsType = FindType("UnityEditor.TestTools.TestRunner.Api.ExecutionSettings"),
                    FilterType = FindType("UnityEditor.TestTools.TestRunner.Api.Filter"),
                    TestModeType = FindType("UnityEditor.TestTools.TestRunner.Api.TestMode"),
                    CallbacksInterfaceType = FindType("UnityEditor.TestTools.TestRunner.Api.ICallbacks")
                };

                var missing = new List<string>();
                if (resolved.ApiType == null) missing.Add("TestRunnerApi");
                if (resolved.ExecutionSettingsType == null) missing.Add("ExecutionSettings");
                if (resolved.FilterType == null) missing.Add("Filter");
                if (resolved.TestModeType == null) missing.Add("TestMode");
                if (resolved.CallbacksInterfaceType == null) missing.Add("ICallbacks");

                resolved.Available = missing.Count == 0;
                resolved.Error = resolved.Available
                    ? null
                    : $"Unity Test Framework API types are unavailable: {string.Join(", ", missing)}.";
                return resolved;
            }

            public static void InvokeRegisterCallbacks(object api, TestRunnerReflection types, object callbacks)
            {
                var methods = types.ApiType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => method.Name == "RegisterCallbacks")
                    .OrderBy(method => method.GetParameters().Length);

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(callbacks))
                    {
                        method.Invoke(api, new[] { callbacks });
                        return;
                    }

                    if (parameters.Length == 2 && parameters[0].ParameterType.IsInstanceOfType(callbacks))
                    {
                        method.Invoke(api, new[] { callbacks, (object)0 });
                        return;
                    }
                }

                throw new MissingMethodException(types.ApiType.FullName, "RegisterCallbacks");
            }

            public static bool TryUnregisterCallbacks(object api, TestRunnerReflection types, object callbacks, out string error)
            {
                error = null;
                try
                {
                    var method = types.ApiType
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(candidate =>
                        {
                            if (candidate.Name != "UnregisterCallbacks")
                                return false;

                            var parameters = candidate.GetParameters();
                            return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(callbacks);
                        });
                    if (method == null)
                    {
                        error = "UnregisterCallbacks was not found.";
                        return false;
                    }

                    method.Invoke(api, new[] { callbacks });
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            public static object InvokeExecute(object api, TestRunnerReflection types, object settings)
            {
                var method = types.ApiType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                    {
                        if (candidate.Name != "Execute")
                            return false;

                        var parameters = candidate.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(settings);
                    });

                if (method == null)
                    throw new MissingMethodException(types.ApiType.FullName, "Execute");

                return method.Invoke(api, new[] { settings });
            }

            public static bool TryCancelRun(object api, out string error)
            {
                error = null;
                try
                {
                    var method = api.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(candidate => candidate.Name == "CancelTestRun" && candidate.GetParameters().Length == 0);
                    if (method == null)
                    {
                        error = "CancelTestRun was not found.";
                        return false;
                    }

                    method.Invoke(api, null);
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            static Type FindType(string fullName)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }

                return null;
            }
        }
    }
}
