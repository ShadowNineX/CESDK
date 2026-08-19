using System.Text.Json;

namespace CESDK.LiveTests;

[TestClass]
public sealed class CesdkLiveTests
{
    private const string DefaultResultFileName = "cesdk-live-tests-result.json";

    private static string ResultPath =>
        Environment.GetEnvironmentVariable("CESDK_LIVE_RESULT")
        ?? Path.Combine(Path.GetTempPath(), DefaultResultFileName);

    [TestMethod]
    [TestCategory("Unit")]
    public void Harness_ResolvesResultPath()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(ResultPath));
        Assert.IsTrue(Path.IsPathFullyQualified(ResultPath), $"CESDK live result path should be absolute: {ResultPath}");
    }

    [TestMethod]
    [TestCategory("Live")]
    public async Task LiveTestPlugin_ResultFileReportsSuccess()
    {
        if (Environment.GetEnvironmentVariable("CESDK_LIVE") != "1")
        {
            Assert.Inconclusive(
                "CESDK live tests are opt-in. Build CESDK.LiveTestPlugin, load cesdk-live-tests.dll in Cheat Engine, enable it, set CESDK_LIVE=1, then run dotnet test --filter TestCategory=Live.");
        }

        LiveTestReport report = await WaitForReportAsync();
        List<string> testNames = report.Tests.Select(test => test.Name).ToList();

        Assert.IsTrue(report.Tests.Count > 0, "The CESDK live test report did not contain any test cases.");
        Assert.IsTrue(report.Success, FormatFailures(report));

        var expectedTestNames = new List<string>
        {
            "lua-native-do-string",
            "lua-native-register-function",
            "lua-native-register-ce-function",
            "lua-executor-multiple-results",
            "lua-executor-table-results",
            "converter-string-md5",
            "cesdk-synchronize",
            "plugin-context-current-plugin",
            "plugin-logger-nlog-file",
            "lua-logger-print",
            "process-list-attached-target",
            "process-control-status",
            "address-resolver-module",
            "symbol-manager-modules",
            "symbol-waiter-sections",
            "thread-list-current-process",
            "memory-regions-enumeration",
            "debugger-status-queries",
            "speedhack-current-speed",
            "dbvm-availability",
            "assembler-nop",
            "disassembler-module",
            "ce-object-wrapper-double-dispose",
        };

        if (testNames.Contains("process-open-configured-target", StringComparer.Ordinal))
        {
            expectedTestNames.Add("process-open-configured-target");
        }

        if (report.MutatingTestsEnabled)
        {
            expectedTestNames.AddRange(
            [
                "address-list-record-lifecycle",
                "structure-manager-lifecycle",
                "cheat-table-save",
                "symbol-registry-lifecycle",
                "memory-access-read-write",
                "advanced-memory-copy-compare-file",
                "pointer-chains-resolve",
                "aob-scanner-allocated-marker",
                "memscan-bounded-marker",
                "found-list-lifecycle",
                "injection-script-generation",
            ]);
        }

        if (Environment.GetEnvironmentVariable("CESDK_LIVE_MUTATING") == "1")
            Assert.IsTrue(report.MutatingTestsEnabled, "The CE plugin did not run the requested mutating coverage.");

        CollectionAssert.AreEquivalent(
            expectedTestNames,
            testNames,
            "Live report should contain every test for its selected safety mode.");

        string[] targetDependentNames =
        [
            "process-list-attached-target",
            "process-control-status",
            "address-resolver-module",
            "symbol-manager-modules",
            "thread-list-current-process",
            "memory-regions-enumeration",
            "speedhack-current-speed",
            "disassembler-module",
        ];
        bool targetConfigured = report.AttachedTargetProcessId > 0;
        foreach (string name in targetDependentNames)
        {
            LiveTestCase test = report.Tests.Single(item => item.Name == name);
            Assert.AreEqual(
                !targetConfigured,
                test.Skipped,
                $"Target-dependent test '{name}' should {(targetConfigured ? "execute" : "be skipped")}.");
            if (test.Skipped)
                Assert.IsFalse(string.IsNullOrWhiteSpace(test.SkipReason), $"Skipped test '{name}' should explain why.");
        }
    }

    private static async Task<LiveTestReport> WaitForReportAsync()
    {
        TimeSpan timeout = GetTimeout();
        TimeSpan maxAge = GetMaxAge();
        DateTime freshAfterUtc = DateTime.UtcNow.Subtract(maxAge);
        DateTime deadlineUtc = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow <= deadlineUtc)
        {
            FileInfo file = new(ResultPath);
            if (file.Exists && file.LastWriteTimeUtc >= freshAfterUtc)
                return ReadReport(file.FullName);

            await Task.Delay(250);
        }

        Assert.Fail(
            $"CESDK live test result was not written to '{ResultPath}' within {timeout.TotalSeconds:0.#} seconds, or the existing file was older than {maxAge.TotalMinutes:0.#} minutes.");
        throw new InvalidOperationException("Unreachable after Assert.Fail.");
    }

    private static LiveTestReport ReadReport(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LiveTestReport>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"CESDK live test result file was empty or invalid: {path}");
    }

    private static TimeSpan GetTimeout() =>
        ReadEnvSeconds("CESDK_LIVE_TIMEOUT_SECONDS", 10);

    private static TimeSpan GetMaxAge() =>
        ReadEnvSeconds("CESDK_LIVE_MAX_RESULT_AGE_SECONDS", 600);

    private static TimeSpan ReadEnvSeconds(string name, int defaultSeconds)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(defaultSeconds);
    }

    private static string FormatFailures(LiveTestReport report)
    {
        IEnumerable<string> failures = report.Tests
            .Where(test => !test.Success && !test.Skipped)
            .Select(test => $"{test.Name}: {test.Error}");

        string details = string.Join(Environment.NewLine, failures);
        return string.IsNullOrWhiteSpace(details)
            ? $"CESDK live test plugin reported failure in '{ResultPath}'."
            : details;
    }

    private sealed class LiveTestReport
    {
        public string Plugin { get; set; } = "";
        public bool Success { get; set; }
        public bool MutatingTestsEnabled { get; set; }
        public int? AttachedTargetProcessId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset FinishedAtUtc { get; set; }
        public List<LiveTestCase> Tests { get; set; } = [];
    }

    private sealed class LiveTestCase
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public string? SkipReason { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }
    }
}
