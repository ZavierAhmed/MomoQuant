using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C3RestartHarnessSubprocessTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly ITestOutputHelper _output;

    public Milestone231B1C3RestartHarnessSubprocessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Timeout_KillsRootAndDescendantProcessTree()
    {
        var runner = new TestSubprocessRunner(_output);
        var request = new TestSubprocessRequest(
            Phase: "orchestration-probe",
            CrashPoint: "timeout-cleanup",
            FixtureId: Guid.Empty,
            FileName: "dotnet",
            Arguments:
            [
                AuditRestartHarnessOutput.ResolveAssemblyPath(),
                "--orchestration-probe"
            ],
            EnvironmentVariables: new Dictionary<string, string>());

        var timeout = await Assert.ThrowsAsync<TestSubprocessTimeoutException>(
            () => runner.RunAsync(request, ProbeTimeout));

        var pidMatch = Regex.Match(
            timeout.StdOut,
            @"ORCHESTRATION_PROBE rootPid=(?<root>\d+) descendantPid=(?<descendant>\d+)",
            RegexOptions.CultureInvariant);
        Assert.True(pidMatch.Success, $"Probe PID evidence was missing. stdout={timeout.StdOut}");

        var rootPid = int.Parse(pidMatch.Groups["root"].Value);
        var descendantPid = int.Parse(pidMatch.Groups["descendant"].Value);
        Assert.Equal(timeout.ProcessId, rootPid);
        Assert.True(timeout.CleanupSucceeded, timeout.Diagnostic);
        Assert.False(IsProcessAlive(rootPid), $"Probe root PID {rootPid} remained alive.");
        Assert.False(IsProcessAlive(descendantPid), $"Probe descendant PID {descendantPid} remained alive.");
        Assert.Contains("phase=orchestration-probe", timeout.Diagnostic, StringComparison.Ordinal);
        Assert.Contains($"childPid={rootPid}", timeout.Diagnostic, StringComparison.Ordinal);

        using var testHost = Process.GetProcessById(Environment.ProcessId);
        Assert.False(testHost.HasExited);
        _output.WriteLine(
            $"probeRootPid={rootPid}; probeDescendantPid={descendantPid}; " +
            $"rootExited=true; descendantExited=true; unrelatedTestHostPid={Environment.ProcessId}; " +
            "unrelatedTestHostAlive=true");
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
