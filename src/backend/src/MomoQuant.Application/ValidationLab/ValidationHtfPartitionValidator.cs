using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Fail-closed HTF partition validation for factory bootstrap loads (Milestone 23.1B1B).
/// Validates raw repository results in returned order — no sort, no silent filter.
/// </summary>
internal static class ValidationHtfPartitionValidator
{
    internal sealed class ValidationOutcome
    {
        public bool Succeeded { get; init; }
        public string? DenialCode { get; init; }
        public string? DenialReason { get; init; }
        public IReadOnlyList<Candle> Authorized { get; init; } = Array.Empty<Candle>();
    }

    internal static ValidationOutcome ValidateRawHtfPartitionFailClosed(
        IReadOnlyList<Candle> raw,
        long expectedSymbolId,
        long expectedExchangeId,
        Timeframe mappedHtf,
        DateTime loadEndExclusive,
        DateTime boundary)
    {
        if (raw.Count == 0)
        {
            return Fail(
                ValidationCandlePartitionDenialCodes.MissingPartitionHtf,
                $"Adaptive validation HTF '{TimeframeParser.ToApiString(mappedHtf)}' is missing from authorized unscoped load. " +
                "Coverage/import/repos must not repair validation HTF after scope construction.");
        }

        DateTime? previousOpen = null;
        DateTime? previousClose = null;
        var seenOpens = new HashSet<DateTime>();

        for (var i = 0; i < raw.Count; i++)
        {
            var c = raw[i];
            var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
            var close = DateTime.SpecifyKind(c.CloseTimeUtc, DateTimeKind.Utc);

            if (c.SymbolId != expectedSymbolId)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfWrongSymbol,
                    $"Higher-timeframe candle at index {i} SymbolId {c.SymbolId} does not match expected symbol {expectedSymbolId}.");
            }

            if (c.ExchangeId != expectedExchangeId)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfWrongExchange,
                    $"Higher-timeframe candle at index {i} ExchangeId {c.ExchangeId} does not match expected exchange {expectedExchangeId}.");
            }

            if (c.Timeframe != mappedHtf)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfWrongTimeframe,
                    $"Higher-timeframe candle at index {i} timeframe '{TimeframeParser.ToApiString(c.Timeframe)}' does not match mapped HTF '{TimeframeParser.ToApiString(mappedHtf)}'.");
            }

            if (!c.IsClosed)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfOpenCandle,
                    $"Higher-timeframe candle at index {i} OpenTimeUtc={open:O} is not closed (IsClosed=false).");
            }

            if (!seenOpens.Add(open))
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfDuplicate,
                    $"Higher-timeframe candles contain duplicate OpenTimeUtc {open:O} at index {i}.");
            }

            if (previousOpen.HasValue && open <= previousOpen.Value)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfUnordered,
                    $"Higher-timeframe candles are not strictly ascending by OpenTimeUtc at index {i} (got {open:O} after {previousOpen.Value:O}).");
            }

            if (previousClose.HasValue && close < previousClose.Value)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfUnordered,
                    $"Higher-timeframe candles are not ascending by CloseTimeUtc at index {i} (got {close:O} after {previousClose.Value:O}).");
            }

            if (close > loadEndExclusive || close > boundary)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary,
                    $"Higher-timeframe candle at index {i} CloseTimeUtc={close:O} extends beyond load end/boundary ({loadEndExclusive:O}).");
            }

            if (open >= loadEndExclusive)
            {
                return Fail(
                    ValidationCandlePartitionDenialCodes.HtfCloseBeyondBoundary,
                    $"Higher-timeframe candle at index {i} OpenTimeUtc={open:O} is at or after load end exclusive ({loadEndExclusive:O}).");
            }

            previousOpen = open;
            previousClose = close;
        }

        return new ValidationOutcome { Succeeded = true, Authorized = raw };
    }

    private static ValidationOutcome Fail(string denialCode, string denialReason) =>
        new()
        {
            Succeeded = false,
            DenialCode = denialCode,
            DenialReason = denialReason
        };
}
