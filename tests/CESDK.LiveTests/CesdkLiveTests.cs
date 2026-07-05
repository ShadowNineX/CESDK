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

        CollectionAssert.Contains(testNames, "lua-native-do-string");
        CollectionAssert.Contains(testNames, "lua-native-register-function");
        CollectionAssert.Contains(testNames, "lua-native-register-ce-function");
        CollectionAssert.Contains(testNames, "lua-executor-multiple-results");
        CollectionAssert.Contains(testNames, "lua-executor-table-results");
        CollectionAssert.Contains(testNames, "converter-string-md5");
        CollectionAssert.Contains(testNames, "cesdk-synchronize");
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
            .Where(test => !test.Success)
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
