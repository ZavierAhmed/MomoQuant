namespace MomoQuant.IntegrationTests;

public sealed class IntegrationDatabaseSafetyTests
{
    [Fact]
    public void AssertDisposableTestDatabase_AcceptsMomoQuantTest()
    {
        var ex = Record.Exception(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase(
                "Server=localhost;Port=3306;Database=momo_quant_test;User=momo_user;Password=secret"));

        Assert.Null(ex);
    }

    [Fact]
    public void AssertDisposableTestDatabase_RejectsMomoQuantWithoutTestSuffix()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase(
                "Server=localhost;Port=3306;Database=momo_quant;User=momo_user;Password=secret"));

        Assert.Contains("_test", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertDisposableTestDatabase_RejectsMissingDatabase()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase(
                "Server=localhost;Port=3306;User=momo_user;Password=secret"));

        Assert.Contains("Database", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertDisposableTestDatabase_RejectsNullOrEmpty()
    {
        Assert.Throws<InvalidOperationException>(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase(null));
        Assert.Throws<InvalidOperationException>(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase("   "));
    }

    [Fact]
    public void AssertDisposableTestDatabase_AcceptsCaseInsensitiveTestSuffix()
    {
        var ex = Record.Exception(() =>
            IntegrationDatabaseSafety.AssertDisposableTestDatabase(
                "Server=localhost;Database=MOMO_QUANT_TEST;User=u;Password=p"));

        Assert.Null(ex);
    }
}
