using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C1 — true separate-process crash/recovery harness tests.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C1ProcessRestartTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone230E2C1ProcessRestartTests(MomoQuantWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CrashBeforeFirstFlush_RestartCannotTreatZeroRowsAsComplete()
    {
        var result = await RunCrashRecoverAsync(
            "AfterAuditExecutionCreatedBeforeFirstFlush");

        Assert.False(result.GetProperty("CompletenessIsComplete").GetBoolean());
        Assert.NotEqual("Complete", result.GetProperty("CompletenessCode").GetString());
        Assert.Equal(0, result.GetProperty("EventCount").GetInt32());
        Assert.NotEqual("Complete", result.GetProperty("OldExecutionStatus").GetString());
        Assert.NotEqual("NoRecoveryNeeded", result.GetProperty("RecoveryDecision").GetString());
        Assert.True(result.GetProperty("MustRerunTrial").GetBoolean());

        var oldStatus = result.GetProperty("OldExecutionStatus").GetString();
        Assert.True(
            oldStatus is "Superseded" or "RecoveryRequired",
            $"Expected Superseded or RecoveryRequired, got {oldStatus}");

        var fixtureId = result.GetProperty("FixtureId").GetGuid();
        var newAuditId = result.GetProperty("NewAuditExecutionId").GetGuid();
        var oldScopeId = result.GetProperty("OldScopeExecutionId").GetGuid();
        var newScopeId = result.GetProperty("NewScopeExecutionId").GetGuid();

        Assert.NotEqual(fixtureId, newAuditId);
        Assert.NotEqual(oldScopeId, newScopeId);
        Assert.NotEqual(
            result.GetProperty("OldExecutionToken").GetString(),
            result.GetProperty("NewExecutionToken").GetString());
        Assert.Equal(2, result.GetProperty("NewAttemptNumber").GetInt32());
        Assert.Equal(1L, result.GetProperty("ReplacementFirstAccessSequence").GetInt64());
        Assert.True(result.GetProperty("OldRowsCannotSatisfyReplacementCompleteness").GetBoolean());
        Assert.True(result.GetProperty("InMemoryAccessCountBeforeCrash").GetInt32() >= 1);
        Assert.False(result.GetProperty("BeforeRecoveryCompletenessIsComplete").GetBoolean());
    }

    [Fact]
    public async Task CrashAfterEventCommitBeforeCursorAdvance_RestartRecoversConfirmedSequence()
    {
        var result = await RunCrashRecoverAsync(
            "AfterEventCommitBeforeCursorAdvance");

        Assert.True(result.GetProperty("RecoveredLastConfirmedSequence").GetInt64() >= 1
                    || result.GetProperty("LastConfirmedSequence").GetInt64() >= 1);
        Assert.False(result.GetProperty("MustRerunTrial").GetBoolean());
    }

    [Fact]
    public async Task CrashAfterAllEventsConfirmedBeforeCompletionMarker_RemainsIncompleteUntilRecovered()
    {
        var result = await RunCrashRecoverAsync(
            "AfterEventsConfirmedBeforeExecutionCompleted");

        Assert.NotEqual("Completed", result.GetProperty("BeforeRecoveryExecutionStatus").GetString());
        Assert.False(result.GetProperty("BeforeRecoveryCompletenessIsComplete").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            result.GetProperty("BeforeRecoveryFinalExpectedSequence").ValueKind);
        Assert.True(result.GetProperty("FinalizerInvoked").GetBoolean());
        Assert.True(result.GetProperty("FinalizerIsComplete").GetBoolean());
        Assert.True(result.GetProperty("CompletenessIsComplete").GetBoolean());
        Assert.Equal("Complete", result.GetProperty("CompletenessCode").GetString());
        Assert.Equal("Complete", result.GetProperty("TrialAuditCompletionStatus").GetString());
    }

    private async Task<JsonElement> RunCrashRecoverAsync(string crashPoint)
    {
        var connection = ResolveConnection();
        IntegrationDatabaseSafety.AssertDisposableTestDatabase(connection);

        var fixtureId = Guid.NewGuid();
        var resultPath = Path.Combine(Path.GetTempPath(), $"e2c1-{fixtureId:N}.json");
        var project = FindHarnessProject();

        // Ensure schema exists via factory host (migrates on startup).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        }

        var write = await RunHarnessAsync(
            project,
            $"--phase write --crash-point {crashPoint} --fixture-id {fixtureId:D} --connection \"{connection}\"");
        Assert.True(
            write.ExitCode == 42,
            $"Write phase expected exit 42, got {write.ExitCode}. stdout={write.StdOut} stderr={write.StdErr}");

        var recover = await RunHarnessAsync(
            project,
            $"--phase recover --crash-point {crashPoint} --fixture-id {fixtureId:D} --connection \"{connection}\" --result-path \"{resultPath}\"");
        Assert.True(
            recover.ExitCode == 0,
            $"Recover phase expected exit 0, got {recover.ExitCode}. stdout={recover.StdOut} stderr={recover.StdErr}");

        Assert.True(File.Exists(resultPath), $"Result file missing. stdout={recover.StdOut} stderr={recover.StdErr}");
        var json = await File.ReadAllTextAsync(resultPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string ResolveConnection()
    {
        var target = IntegrationDatabaseConnectionResolver.Resolve();
        return target.ConnectionString;
    }

    private static string FindHarnessProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "backend", "tests", "MomoQuant.AuditRestartHarness",
                "MomoQuant.AuditRestartHarness.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // From bin/Debug/net8.0 of IntegrationTests
            candidate = Path.Combine(
                dir.FullName,
                "..", "..", "..", "..",
                "MomoQuant.AuditRestartHarness",
                "MomoQuant.AuditRestartHarness.csproj");
            candidate = Path.GetFullPath(candidate);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("MomoQuant.AuditRestartHarness.csproj not found.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunHarnessAsync(
        string projectPath,
        string args)
    {
        // Always build so process tests pick up harness changes.
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -- {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await StartProcessAsync(psi);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> StartProcessAsync(ProcessStartInfo psi)
    {
        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        Assert.True(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
