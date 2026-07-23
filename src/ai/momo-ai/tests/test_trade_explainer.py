from app.models.explanation import TradeExplainRequest
from app.services.trade_explainer import TradeExplainer, CAUTION_TEXT


def _explainer() -> TradeExplainer:
    return TradeExplainer()


def test_returns_clear_summary() -> None:
    result = _explainer().explain(
        TradeExplainRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            strategyCode="EMA_PULLBACK",
            signalDirection="Long",
            marketRegime="Trending",
            confidenceScore=84,
            riskDecision="Approved",
            riskReason="All risk rules passed",
            strategyReason="Bullish EMA alignment with pullback near EMA20",
        )
    )

    assert "BTCUSDT" in result.summary
    assert "EMA Pullback" in result.summary
    assert "Trending" in result.summary


def test_includes_caution_text() -> None:
    result = _explainer().explain(
        TradeExplainRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            strategyCode="EMA_PULLBACK",
            signalDirection="Long",
            marketRegime="Trending",
            confidenceScore=84,
            riskDecision="Approved",
            riskReason="All risk rules passed",
            strategyReason="Bullish EMA alignment with pullback near EMA20",
        )
    )

    assert result.caution == CAUTION_TEXT


def test_does_not_guarantee_trade() -> None:
    result = _explainer().explain(
        TradeExplainRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            strategyCode="EMA_PULLBACK",
            signalDirection="Long",
            marketRegime="Trending",
            confidenceScore=84,
            riskDecision="Approved",
            riskReason="All risk rules passed",
            strategyReason="Bullish EMA alignment with pullback near EMA20",
        )
    )

    assert any("does not guarantee" in detail.lower() for detail in result.details)


def test_does_not_claim_risk_approval_when_rejected() -> None:
    result = _explainer().explain(
        TradeExplainRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            strategyCode="EMA_PULLBACK",
            signalDirection="Long",
            marketRegime="Trending",
            confidenceScore=50,
            riskDecision="Rejected",
            riskReason="Confidence below minimum",
            strategyReason="Pullback detected",
        )
    )

    assert any("rejected" in detail.lower() for detail in result.details)
    assert not any("approved the setup" in detail.lower() for detail in result.details)
