using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Persistence;
using Xunit.Abstractions;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2C1 — true separate-process crash/recovery harness tests.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2C1ProcessRestartTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private const string ConnectionEnvironmentVariable = "MOMOQUANT_AUDIT_RESTART_CONNECTION";
    private readonly MomoQuantWebApplicationFactory _factory;
    private readonly TestSubprocessRunner _subprocessRunner;

    public Milestone230E2C1ProcessRestartTests(
        MomoQuantWebApplicationFactory factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _subprocessRunner = new TestSubprocessRunner(output);
    }

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
        var accessHintPath = Path.Combine(Path.GetTempPath(), $"e2c1-access-{fixtureId:N}.txt");
        var harnessAssembly = AuditRestartHarnessOutput.ResolveAssemblyPath();

        try
        {
            // Ensure schema exists via factory host (migrates on startup).
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                _ = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            }

            var write = await RunHarnessAsync(
                harnessAssembly,
                connection,
                phase: "write",
                crashPoint,
                fixtureId,
                resultPath: null);
            Assert.True(
                write.ExitCode == 42,
                $"Write phase expected exit 42, got {write.ExitCode}. stdout={write.StdOut} stderr={write.StdErr}");

            var recover = await RunHarnessAsync(
                harnessAssembly,
                connection,
                phase: "recover",
                crashPoint,
                fixtureId,
                resultPath);
            Assert.True(
                recover.ExitCode == 0,
                $"Recover phase expected exit 0, got {recover.ExitCode}. stdout={recover.StdOut} stderr={recover.StdErr}");

            Assert.True(File.Exists(resultPath), $"Result file missing. stdout={recover.StdOut} stderr={recover.StdErr}");
            var json = await File.ReadAllTextAsync(resultPath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        finally
        {
            DeleteTestOwnedFile(resultPath);
            DeleteTestOwnedFile(accessHintPath);
        }
    }

    private static string ResolveConnection()
    {
        var target = IntegrationDatabaseConnectionResolver.Resolve();
        return target.ConnectionString;
    }

    private Task<TestSubprocessResult> RunHarnessAsync(
        string harnessAssembly,
        string connection,
        string phase,
        string crashPoint,
        Guid fixtureId,
        string? resultPath)
    {
        var arguments = new List<string>
        {
            harnessAssembly,
            "--phase", phase,
            "--crash-point", crashPoint,
            "--fixture-id", fixtureId.ToString("D")
        };
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            arguments.Add("--result-path");
            arguments.Add(resultPath);
        }

        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var request = new TestSubprocessRequest(
            phase,
            crashPoint,
            fixtureId,
            string.IsNullOrWhiteSpace(dotnetHost) ? "dotnet" : dotnetHost,
            arguments,
            new Dictionary<string, string>
            {
                [ConnectionEnvironmentVariable] = connection
            });
        return _subprocessRunner.RunAsync(request);
    }

    private static void DeleteTestOwnedFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Test-owned diagnostics should not hide the primary restart assertion.
        }
    }
}
