using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CESDK;
using CESDK.Classes;

namespace CESDK.LiveTestPlugin;

public sealed class CesdkLiveTestPlugin : CheatEnginePlugin
{
    private const string DefaultResultFileName = "cesdk-live-tests-result.json";

    public override string Name => "CESDK Live Tests";

    protected override void OnEnable()
    {
        string resultPath = GetResultPath();
        PrintSeparator();
        LuaLogger.TryPrint("CESDK LIVE TESTS STARTED");
        LuaLogger.TryPrint($"JSON result file: {resultPath}");
        PrintSeparator();

        LiveTestReport report = new()
        {
            Plugin = Name,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Tests = []
        };

        Run(report, "lua-native-do-string", LuaNativeDoStringLeavesExpectedValues);
        Run(report, "lua-native-register-function", LuaNativeRegisterFunctionCallsManagedCode);
        Run(report, "lua-native-register-ce-function", LuaNativeRegisterCeFunctionCallsManagedCode);
        Run(report, "lua-executor-multiple-results", LuaExecutorReadsMultipleResults);
        Run(report, "lua-executor-table-results", LuaExecutorReadsTables);
        Run(report, "converter-string-md5", ConverterReturnsExpectedMd5);
        Run(report, "cesdk-synchronize", SynchronizeExecutesCallback);

        report.FinishedAtUtc = DateTimeOffset.UtcNow;
        report.Success = report.Tests.All(test => test.Success);

        WriteReport(report, resultPath);
        PrintCompletion(report, resultPath);
    }

    private static void LuaNativeDoStringLeavesExpectedValues()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();

        try
        {
            lua.DoString("return 'cesdk', 42, true");

            AssertEqual(initialTop + 3, lua.GetTop(), "DoString should leave three return values on the stack.");
            AssertEqual("cesdk", lua.ToString(-3), "First return value should be a string.");
            AssertEqual(42, lua.ToInteger(-2), "Second return value should be an integer.");
            AssertTrue(lua.ToBoolean(-1), "Third return value should be true.");
        }
        finally
        {
            lua.SetTop(initialTop);
        }
    }

    private static void LuaNativeRegisterFunctionCallsManagedCode()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();
        int calls = 0;

        try
        {
            lua.RegisterFunction("__cesdk_live_managed_callback", () => calls++);
            lua.DoString("__cesdk_live_managed_callback()");

            AssertEqual(1, calls, "Registered Lua function should call managed code exactly once.");
        }
        finally
        {
            lua.SetTop(initialTop);
            lua.DoString("__cesdk_live_managed_callback = nil");
            lua.SetTop(initialTop);
        }
    }

    private static void LuaNativeRegisterCeFunctionCallsManagedCode()
    {
        var lua = PluginContext.Lua;
        int initialTop = lua.GetTop();
        int calls = 0;

        try
        {
            lua.RegisterCEFunction("__cesdk_live_ce_callback", _ =>
            {
                calls++;
                lua.PushString("ok");
                return 1;
            });

            lua.DoString("return __cesdk_live_ce_callback()");

            AssertEqual(1, calls, "Registered CE Lua function should call managed code exactly once.");
            AssertEqual(initialTop + 1, lua.GetTop(), "CE callback should leave one return value on the stack.");
            AssertEqual("ok", lua.ToString(-1), "CE callback should return its pushed value.");
        }
        finally
        {
            lua.SetTop(initialTop);
            lua.DoString("__cesdk_live_ce_callback = nil");
            lua.SetTop(initialTop);
        }
    }

    private static void LuaExecutorReadsMultipleResults()
    {
        LuaResult result = LuaExecutor.Execute("return 12, 'ok', false");
        List<object?> values = AssertIs<List<object?>>(result.Values, "Multiple return values should be present.");

        AssertEqual(3, result.ReturnCount, "LuaExecutor should report three return values.");
        AssertEqual(12, AssertIs<int>(values[0], "First return value should be an integer."), "First return value should match.");
        AssertEqual("ok", AssertIs<string>(values[1], "Second return value should be a string."), "Second return value should match.");
        AssertFalse(AssertIs<bool>(values[2], "Third return value should be false."), "Third return value should be false.");
    }

    private static void LuaExecutorReadsTables()
    {
        LuaResult arrayResult = LuaExecutor.Execute("return { 5, 'six', true }");
        List<object?> array = AssertIs<List<object?>>(arrayResult.Value, "Sequential table should become a list.");

        AssertEqual(3, array.Count, "Sequential table should contain three entries.");
        AssertEqual(5, AssertIs<int>(array[0], "First array entry should be an integer."), "First array entry should match.");
        AssertEqual("six", AssertIs<string>(array[1], "Second array entry should be a string."), "Second array entry should match.");
        AssertTrue(AssertIs<bool>(array[2], "Third array entry should be true."), "Third array entry should be true.");

        LuaResult dictResult = LuaExecutor.Execute("return { name = 'cesdk', count = 3, nested = { ok = true } }");
        Dictionary<string, object?> dict = AssertIs<Dictionary<string, object?>>(dictResult.Value, "Record table should become a dictionary.");
        Dictionary<string, object?> nested = AssertIs<Dictionary<string, object?>>(dict["nested"], "Nested table should become a dictionary.");

        AssertEqual("cesdk", AssertIs<string>(dict["name"], "Dictionary name should be a string."), "Dictionary name should match.");
        AssertEqual(3, AssertIs<int>(dict["count"], "Dictionary count should be an integer."), "Dictionary count should match.");
        AssertTrue(AssertIs<bool>(nested["ok"], "Nested ok value should be true."), "Nested ok value should be true.");
    }

    private static void ConverterReturnsExpectedMd5()
    {
        string md5 = Converter.StringToMD5("abc");
        AssertEqual("900150983cd24fb0d6963f7d28e17f72", md5.ToLowerInvariant(), "CE MD5 helper should match the known digest for abc.");
    }

    private static void SynchronizeExecutesCallback()
    {
        bool called = false;

        global::CESDK.CESDK.Synchronize(() => called = true);

        AssertTrue(called, "Synchronize should execute the managed callback.");
    }

    private static void Run(LiveTestReport report, string name, Action test)
    {
        LuaLogger.TryPrint($"RUNNING: {name}");

        Stopwatch stopwatch = Stopwatch.StartNew();
        LiveTestCase result = new()
        {
            Name = name
        };

        try
        {
            test();
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.ToString();
        }
        finally
        {
            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;
            report.Tests.Add(result);
        }

        string status = result.Success ? "PASSED" : "FAILED";
        LuaLogger.TryPrint($"{status}: {name} ({result.DurationMs} ms)");
    }

    private static void WriteReport(LiveTestReport report, string resultPath)
    {
        string? directory = Path.GetDirectoryName(resultPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resultPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void PrintCompletion(LiveTestReport report, string resultPath)
    {
        int passed = report.Tests.Count(test => test.Success);
        int failed = report.Tests.Count - passed;

        PrintSeparator();
        LuaLogger.TryPrint("ALL CESDK LIVE TESTS ARE DONE");
        LuaLogger.TryPrint(report.Success ? "FINAL RESULT: ALL TESTS PASSED" : "FINAL RESULT: SOME TESTS FAILED");
        LuaLogger.TryPrint($"SUMMARY: {passed}/{report.Tests.Count} passed, {failed} failed");
        LuaLogger.TryPrint($"JSON result file: {resultPath}");
        PrintSeparator();
    }

    private static void PrintSeparator() =>
        LuaLogger.TryPrint("============================================================");

    private static string GetResultPath() =>
        Environment.GetEnvironmentVariable("CESDK_LIVE_RESULT")
        ?? Path.Combine(Path.GetTempPath(), DefaultResultFileName);

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static T AssertIs<T>(object? value, string message)
    {
        if (value is T typedValue)
            return typedValue;

        string actualType = value?.GetType().FullName ?? "<null>";
        throw new InvalidOperationException($"{message} Actual type was {actualType}.");
    }

    private sealed class LiveTestReport
    {
        public string Plugin { get; set; } = "";
        public bool Success { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset FinishedAtUtc { get; set; }
        public List<LiveTestCase> Tests { get; set; } = [];
    }

    private sealed class LiveTestCase
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }
    }
}
