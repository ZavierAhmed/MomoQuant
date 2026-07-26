namespace MomoQuant.IntegrationTests;

public sealed class IntegrationDatabaseSafetyTests
{
    private const string Password = "DoNotExposeThisPassword";

    [Fact]
    public void DatabaseName_EndingWithTest_IsAccepted()
    {
        var target = IntegrationDatabaseSafetyGuard.Validate(
            Connection("MOMO_QUANT_TEST"),
            "unit-test");

        Assert.Equal("MOMO_QUANT_TEST", target.NormalizedDatabaseName);
        Assert.Equal("unit-test", target.ConnectionSource);
        Assert.Equal("localhost", target.Server);
    }

    [Fact]
    public void DatabaseName_MomoQuant_IsRejected()
    {
        var exception = AssertUnsafe(Connection("momo_quant"));
        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, exception.SafeErrorCode);
    }

    [Fact]
    public void DatabaseName_TestBackup_IsRejected()
    {
        var exception = AssertUnsafe(Connection("momo_quant_test_backup"));
        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, exception.SafeErrorCode);
    }

    [Fact]
    public void DatabaseName_Testing_IsRejected()
    {
        var exception = AssertUnsafe(Connection("momo_quant_testing"));
        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, exception.SafeErrorCode);
    }

    [Fact]
    public void DatabaseName_Missing_IsRejected()
    {
        var exception = AssertUnsafe(
            $"Server=localhost;User=tester;Password={Password};");
        Assert.Equal(IntegrationDatabaseErrorCodes.DatabaseNameMissing, exception.SafeErrorCode);
    }

    [Fact]
    public void InvalidConnectionString_IsRejectedSafely()
    {
        var exception = AssertUnsafe(
            $"not-a-valid-connection-string;Password={Password}");
        Assert.Equal(IntegrationDatabaseErrorCodes.ConnectionInvalid, exception.SafeErrorCode);
        AssertSafe(exception);
    }

    [Fact]
    public void MissingIntegrationConnection_FailsWithoutFallback()
    {
        var observer = new RecordingObserver();

        var exception = Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseInitialization.ResolveTarget(
                observer,
                environmentValue: null,
                localFileReader: () => null,
                useProcessConfiguration: false));

        Assert.Equal(IntegrationDatabaseErrorCodes.ConnectionNotConfigured, exception.SafeErrorCode);
        Assert.Empty(observer.Calls);
    }

    [Fact]
    public void EnvironmentVariable_TakesPrecedenceOverLocalEnv()
    {
        var target = IntegrationDatabaseConnectionResolver.Resolve(
            Connection("environment_test"),
            () => Connection("file_test"));

        Assert.Equal("environment_test", target.NormalizedDatabaseName);
        Assert.Equal("MOMO_INTEGRATION_MYSQL", target.ConnectionSource);
    }

    [Fact]
    public void ConnectionResolver_DoesNotReturnApplicationDefaultConnection()
    {
        var applicationDefault = Connection("momo_quant");

        var exception = Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseConnectionResolver.Resolve(null, () => applicationDefault));

        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, exception.SafeErrorCode);
        Assert.Equal("integration.local.env", exception.ConnectionSource);
    }

    [Fact]
    public void SafeDiagnostics_DoNotContainPassword()
    {
        var exception = AssertUnsafe(Connection("unsafe_database"));

        AssertSafe(exception);
        Assert.Equal("unsafe_database", exception.ResolvedDatabaseName);
        Assert.Equal(exception.Message, exception.SafeMessage);
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("information_schema")]
    [InlineData("performance_schema")]
    [InlineData("sys")]
    public void UnknownDatabaseTarget_FailsClosed(string databaseName)
    {
        var exception = AssertUnsafe(Connection(databaseName));
        Assert.Equal(IntegrationDatabaseErrorCodes.ReservedName, exception.SafeErrorCode);
    }

    [Fact]
    public void Redaction_ReplacesPasswordAndPwdSegments()
    {
        var redacted = ConnectionStringRedaction.Redact(
            $"Server=localhost;Password={Password};Database=momo_quant_test;Pwd=another-secret;");

        Assert.Contains("Password=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pwd=***", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_NullInput_ReturnsNull()
    {
        Assert.Null(ConnectionStringRedaction.Redact(null));
    }

    [Fact]
    public void GuardPass_InvokesFactoryObserverAfterValidation()
    {
        var observer = new RecordingObserver();

        var target = IntegrationDatabaseInitialization.ResolveTarget(
            observer,
            Connection("observer_test"),
            () => null,
            useProcessConfiguration: false);

        Assert.Equal("observer_test", target.NormalizedDatabaseName);
        Assert.Equal(["OnDbContextCreating"], observer.Calls);
    }

    private static string Connection(string databaseName) =>
        $"Server=localhost;Port=3306;Database={databaseName};User=tester;Password={Password};";

    private static UnsafeIntegrationDatabaseTargetException AssertUnsafe(string connectionString)
    {
        return Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseSafetyGuard.Validate(connectionString, "unit-test"));
    }

    private static void AssertSafe(UnsafeIntegrationDatabaseTargetException exception)
    {
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.SafeMessage, StringComparison.Ordinal);
    }

    private sealed class RecordingObserver : IIntegrationDatabaseInitializationObserver
    {
        public List<string> Calls { get; } = [];

        public void OnDbContextCreating() => Calls.Add(nameof(OnDbContextCreating));

        public void OnMigrating() => Calls.Add(nameof(OnMigrating));

        public void OnSeeding() => Calls.Add(nameof(OnSeeding));
    }
}
