using System.Reflection;
using Microsoft.Extensions.Logging;

namespace MomoQuant.Application.Common;

/// <summary>
/// Shared transient-database retry: MaxAttempts is total attempts (1..MaxAttempts), not retries+1.
/// </summary>
public static class TransientDatabaseRetryPolicy
{
    public const int DefaultMaxAttempts = 3;

    private static readonly HashSet<int> TransientMySqlErrorNumbers =
    [
        1040, // Too many connections
        1205, // Lock wait timeout
        1213, // Deadlock
        1614, // Transaction branch was rolled back
        2002, // Can't connect (socket)
        2003, // Can't connect (server)
        2006, // Server has gone away
        2013  // Lost connection during query
    ];

    public static async Task ExecuteAsync(
        Func<Task> action,
        int maxAttempts = DefaultMaxAttempts,
        string operationName = "database",
        string? correlationId = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be >= 1.");
        }

        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                last = ex;
                logger?.LogWarning(
                    ex,
                    "Transient database failure on {OperationName} attempt {Attempt}/{MaxAttempts}. CorrelationId={CorrelationId}",
                    operationName,
                    attempt,
                    maxAttempts,
                    correlationId ?? "-");

                await ComputeBackoffAsync(attempt, cancellationToken);
            }
        }

        // Exhausted MaxAttempts with transient failures: rethrow the last original exception.
        throw last ?? new InvalidOperationException("TransientDatabaseRetryPolicy exhausted without exception.");
    }

    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        int maxAttempts = DefaultMaxAttempts,
        string operationName = "database",
        string? correlationId = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        T? result = default;
        await ExecuteAsync(
            async () => { result = await action(); },
            maxAttempts,
            operationName,
            correlationId,
            logger,
            cancellationToken);
        return result!;
    }

    public static bool IsTransient(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (TryGetMySqlErrorNumber(current, out var number) && TransientMySqlErrorNumbers.Contains(number))
            {
                return true;
            }

            var typeName = current.GetType().FullName ?? string.Empty;
            if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("TimeoutException", StringComparison.OrdinalIgnoreCase))
            {
                var message = current.Message;
                if (message.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("transient failure", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Lock wait timeout", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Server has gone away", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Too many connections", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var msg = current.Message;
            if (msg.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("transient failure", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMySqlErrorNumber(Exception ex, out int number)
    {
        number = 0;
        var type = ex.GetType();
        var prop = type.GetProperty("Number", BindingFlags.Public | BindingFlags.Instance)
                   ?? type.GetProperty("ErrorCode", BindingFlags.Public | BindingFlags.Instance);
        if (prop is null)
        {
            return false;
        }

        var value = prop.GetValue(ex);
        switch (value)
        {
            case int i:
                number = i;
                return true;
            case Enum e:
                number = Convert.ToInt32(e);
                return true;
            default:
                return false;
        }
    }

    private static async Task ComputeBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        // Exponential base with bounded jitter: 250ms * 2^(attempt-1) + up to 50ms, cap 2s.
        var baseMs = Math.Min(2000, 250 * (1 << (attempt - 1)));
        var jitter = Random.Shared.Next(0, 51);
        await Task.Delay(TimeSpan.FromMilliseconds(baseMs + jitter), cancellationToken);
    }
}
