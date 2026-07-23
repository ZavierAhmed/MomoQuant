using MomoQuant.Application.Common;

namespace MomoQuant.UnitTests.Common;

public class TransientDatabaseRetryPolicyTests
{
    [Fact]
    public async Task Succeeds_OnAttempt1()
    {
        var attempts = 0;
        await TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.CompletedTask;
        }, maxAttempts: 3);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Succeeds_OnAttempt2()
    {
        var attempts = 0;
        await TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw CreateTransient();
            }

            await Task.CompletedTask;
        }, maxAttempts: 3);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Succeeds_OnAttempt3()
    {
        var attempts = 0;
        await TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw CreateTransient();
            }

            await Task.CompletedTask;
        }, maxAttempts: 3);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Fails_AfterExactly3Attempts()
    {
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
            {
                attempts++;
                await Task.CompletedTask;
                throw CreateTransient();
            }, maxAttempts: 3));

        Assert.Equal(3, attempts);
        Assert.Contains("Unable to connect", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonTransient_IsNotRetried()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
            {
                attempts++;
                await Task.CompletedTask;
                throw new InvalidOperationException("permanent");
            }, maxAttempts: 3));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Cancellation_InterruptsDelay()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        var task = TransientDatabaseRetryPolicy.ExecuteAsync(async () =>
        {
            attempts++;
            throw CreateTransient();
        }, maxAttempts: 3, cancellationToken: cts.Token);

        // Cancel during first backoff after attempt 1.
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(attempts >= 1 && attempts <= 3);
    }

    private static Exception CreateTransient() =>
        new InvalidOperationException("Unable to connect to any of the specified MySQL hosts.")
        {
            // Named to look MySQL-ish for type heuristics when message alone is used.
            Source = "MySqlConnector"
        };
}
