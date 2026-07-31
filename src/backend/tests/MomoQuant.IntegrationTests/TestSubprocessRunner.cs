using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace MomoQuant.IntegrationTests;

internal sealed record TestSubprocessRequest(
    string Phase,
    string CrashPoint,
    Guid FixtureId,
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

internal sealed record TestSubprocessResult(
    int ProcessId,
    int ExitCode,
    string StdOut,
    string StdErr,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    TimeSpan Elapsed);

internal sealed class TestSubprocessTimeoutException : TimeoutException
{
    public TestSubprocessTimeoutException(
        string diagnostic,
        int processId,
        bool cleanupSucceeded,
        string stdOut,
        string stdErr)
        : base($"Test-owned subprocess timed out. {diagnostic}{Environment.NewLine}" +
               $"stdout={stdOut}{Environment.NewLine}stderr={stdErr}")
    {
        Diagnostic = diagnostic;
        ProcessId = processId;
        CleanupSucceeded = cleanupSucceeded;
        StdOut = stdOut;
        StdErr = stdErr;
    }

    public string Diagnostic { get; }
    public int ProcessId { get; }
    public bool CleanupSucceeded { get; }
    public string StdOut { get; }
    public string StdErr { get; }
}

internal sealed partial class TestSubprocessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan DefaultCleanupAllowance = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    public TestSubprocessRunner(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task<TestSubprocessResult> RunAsync(
        TestSubprocessRequest request,
        TimeSpan? timeout = null,
        TimeSpan? cleanupAllowance = null)
    {
        var phaseTimeout = timeout ?? DefaultTimeout;
        var cleanupTimeout = cleanupAllowance ?? DefaultCleanupAllowance;
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.EnvironmentVariables)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start test-owned subprocess for phase '{request.Phase}'.");
        }

        var processId = process.Id;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        _output.WriteLine(FormatDiagnostic(
            request,
            processId,
            startedAtUtc,
            completionUtc: null,
            stopwatch.Elapsed,
            exitCode: null,
            timedOut: false,
            cleanupResult: "not-required"));

        using var timeoutCancellation = new CancellationTokenSource(phaseTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            var cleanupSucceeded = false;
            using var cleanupCancellation = new CancellationTokenSource(cleanupTimeout);
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cleanupCancellation.Token);
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(cleanupCancellation.Token);
                cleanupSucceeded = process.HasExited;
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
            {
                cleanupSucceeded = process.HasExited
                    && stdoutTask.IsCompleted
                    && stderrTask.IsCompleted;
            }

            stopwatch.Stop();
            var completedAtUtc = DateTime.UtcNow;
            var stdout = Redact(await CompletedTextAsync(stdoutTask), request.EnvironmentVariables.Values);
            var stderr = Redact(await CompletedTextAsync(stderrTask), request.EnvironmentVariables.Values);
            var diagnostic = FormatDiagnostic(
                request,
                processId,
                startedAtUtc,
                completedAtUtc,
                stopwatch.Elapsed,
                exitCode: process.HasExited ? process.ExitCode : null,
                timedOut: true,
                cleanupResult: cleanupSucceeded ? "complete" : "incomplete");
            _output.WriteLine(diagnostic);

            throw new TestSubprocessTimeoutException(
                diagnostic,
                processId,
                cleanupSucceeded,
                stdout,
                stderr);
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        stopwatch.Stop();
        var completionUtc = DateTime.UtcNow;
        var redactedStdOut = Redact(stdoutTask.Result, request.EnvironmentVariables.Values);
        var redactedStdErr = Redact(stderrTask.Result, request.EnvironmentVariables.Values);
        _output.WriteLine(FormatDiagnostic(
            request,
            processId,
            startedAtUtc,
            completionUtc,
            stopwatch.Elapsed,
            process.ExitCode,
            timedOut: false,
            cleanupResult: "not-required"));

        return new TestSubprocessResult(
            processId,
            process.ExitCode,
            redactedStdOut,
            redactedStdErr,
            startedAtUtc,
            completionUtc,
            stopwatch.Elapsed);
    }

    private static async Task<string> CompletedTextAsync(Task<string> streamTask)
    {
        if (!streamTask.IsCompleted)
        {
            return "<stream did not complete within cleanup allowance>";
        }

        return await streamTask;
    }

    private static string FormatDiagnostic(
        TestSubprocessRequest request,
        int processId,
        DateTime startedAtUtc,
        DateTime? completionUtc,
        TimeSpan elapsed,
        int? exitCode,
        bool timedOut,
        string cleanupResult) =>
        $"phase={request.Phase}; crashPoint={request.CrashPoint}; fixtureId={request.FixtureId:D}; " +
        $"childPid={processId}; startUtc={startedAtUtc:O}; " +
        $"completionUtc={(completionUtc.HasValue ? completionUtc.Value.ToString("O") : "pending")}; " +
        $"elapsed={elapsed.TotalSeconds:F3}s; exitCode={(exitCode.HasValue ? exitCode.Value.ToString() : "pending")}; " +
        $"timeout={timedOut.ToString().ToLowerInvariant()}; processTreeCleanup={cleanupResult}";

    private static string Redact(string text, IEnumerable<string> sensitiveValues)
    {
        var redacted = text;
        foreach (var value in sensitiveValues.Where(value => !string.IsNullOrEmpty(value)))
        {
            redacted = redacted.Replace(value, "<redacted>", StringComparison.Ordinal);
        }

        return ConnectionCredentialPattern().Replace(redacted, "$1<redacted>");
    }

    [GeneratedRegex("(?i)((?:password|pwd)\\s*=\\s*)[^;\\r\\n]*")]
    private static partial Regex ConnectionCredentialPattern();
}

internal static class AuditRestartHarnessOutput
{
    private static readonly string[] RequiredFiles =
    [
        "MomoQuant.AuditRestartHarness.dll",
        "MomoQuant.AuditRestartHarness.deps.json",
        "MomoQuant.AuditRestartHarness.runtimeconfig.json"
    ];

    public static string ResolveAssemblyPath()
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "AuditRestartHarness");
        var missing = RequiredFiles
            .Where(file => !File.Exists(Path.Combine(outputDirectory, file)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                $"Compiled audit restart harness output is incomplete at '{outputDirectory}'. " +
                $"Missing: {string.Join(", ", missing)}. Build MomoQuant.IntegrationTests before running restart tests.");
        }

        return Path.GetFullPath(Path.Combine(outputDirectory, RequiredFiles[0]));
    }
}
