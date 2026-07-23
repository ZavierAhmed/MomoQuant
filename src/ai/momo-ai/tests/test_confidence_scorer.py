from math import inf, nan

from app.models.confidence import ConfidenceScoreRequest
from app.services.confidence_scorer import ADVISORY_RULES_VERSION, ConfidenceScorer, EvaluationStatus


def _scorer() -> ConfidenceScorer:
    return ConfidenceScorer()


def _base(**overrides):
    payload = dict(
        symbol="BTCUSDT",
        timeframe="3m",
        strategyCode="EMA_PULLBACK",
        signalDirection="Long",
        marketRegime="Trending",
        strategyStrength=78,
        emaAlignmentScore=90,
        volumeConfirmation=True,
        rsi14=55,
        atrPercent=1.2,
        rewardRiskRatio=1.8,
        spreadPercent=0.01,
    )
    payload.update(overrides)
    return ConfidenceScoreRequest(**payload)


def test_supported_strategy_evaluates_with_rules_version() -> None:
    result = _scorer().score(_base())

    assert result.evaluation_status == EvaluationStatus.EVALUATED.value
    assert result.is_strategy_supported is True
    assert result.advisory_rules_version == ADVISORY_RULES_VERSION
    assert result.advisory_score is not None and result.advisory_score >= 75
    assert result.confidence_score == result.advisory_score
    assert result.classification == result.advisory_classification
    assert result.warnings == []
    assert result.advisory_eligible == (result.advisory_score >= 80)
    assert result.trade_allowed == result.advisory_eligible


def test_unsupported_strategy_does_not_apply_regime_penalty() -> None:
    result = _scorer().score(
        _base(strategyCode="PSBR_V1_UNKNOWN", strategyStrength=90, marketRegime="Ranging")
    )

    assert result.evaluation_status == EvaluationStatus.UNSUPPORTED_STRATEGY.value
    assert result.is_strategy_supported is False
    assert result.advisory_score is None
    assert result.advisory_eligible is False
    assert result.trade_allowed is False
    assert any("no regime penalty" in w.lower() for w in result.warnings)
    assert result.confidence_score == 0


def test_invalid_inputs_reject_negative_and_nan() -> None:
    negative = _scorer().score(_base(atrPercent=-1.0))
    assert negative.evaluation_status == EvaluationStatus.INVALID_INPUTS.value
    assert any("atr" in w.lower() for w in negative.warnings)

    bad_rr = _scorer().score(_base(rewardRiskRatio=0))
    assert bad_rr.evaluation_status == EvaluationStatus.INVALID_INPUTS.value

    bad_rsi = _scorer().score(_base(rsi14=150))
    assert bad_rsi.evaluation_status == EvaluationStatus.INVALID_INPUTS.value

    bad_volume = _scorer().score(_base(volume=-5))
    assert bad_volume.evaluation_status == EvaluationStatus.INVALID_INPUTS.value

    nan_spread = _scorer().score(
        ConfidenceScoreRequest.model_construct(
            symbol="BTCUSDT",
            timeframe="3m",
            strategy_code="EMA_PULLBACK",
            signal_direction="Long",
            market_regime="Trending",
            strategy_strength=70,
            spread_percent=nan,
        )
    )
    assert nan_spread.evaluation_status == EvaluationStatus.INVALID_INPUTS.value
    assert any("nan" in w.lower() or "finite" in w.lower() for w in nan_spread.warnings)

    inf_spread = _scorer().score(
        ConfidenceScoreRequest.model_construct(
            symbol="BTCUSDT",
            timeframe="3m",
            strategy_code="EMA_PULLBACK",
            signal_direction="Long",
            market_regime="Trending",
            strategy_strength=70,
            spread_percent=inf,
        )
    )
    assert inf_spread.evaluation_status == EvaluationStatus.INVALID_INPUTS.value


def test_insufficient_inputs_when_required_missing() -> None:
    result = _scorer().score(
        ConfidenceScoreRequest.model_construct(
            symbol="BTCUSDT",
            timeframe="3m",
            strategy_code="EMA_PULLBACK",
            signal_direction="Long",
            market_regime="",
            strategy_strength=70,
        )
    )

    assert result.evaluation_status == EvaluationStatus.INSUFFICIENT_INPUTS.value
    assert result.is_strategy_supported is True
    assert "marketRegime" in result.missing_inputs
    assert result.advisory_eligible is False


def test_not_evaluated_when_strategy_code_blank() -> None:
    result = _scorer().score(
        ConfidenceScoreRequest.model_construct(
            symbol="BTCUSDT",
            timeframe="3m",
            strategy_code="   ",
            signal_direction="Long",
            market_regime="Trending",
            strategy_strength=70,
        )
    )

    assert result.evaluation_status == EvaluationStatus.NOT_EVALUATED.value
    assert result.advisory_eligible is False
    assert result.advisory_score is None


def test_deterministic_for_identical_inputs() -> None:
    a = _scorer().score(_base())
    b = _scorer().score(_base())

    assert a.model_dump() == b.model_dump()


def test_penalizes_unsupported_regime_for_supported_strategy() -> None:
    result = _scorer().score(
        _base(
            marketRegime="Ranging",
            emaAlignmentScore=None,
            volumeConfirmation=None,
            rewardRiskRatio=None,
            atrPercent=None,
            spreadPercent=None,
            rsi14=None,
        )
    )

    assert result.evaluation_status == EvaluationStatus.EVALUATED.value
    assert result.advisory_score is not None and result.advisory_score < 78
    assert any("regime" in warning.lower() for warning in result.warnings)


def test_penalizes_high_spread() -> None:
    result = _scorer().score(_base(strategyStrength=80, spreadPercent=0.15, emaAlignmentScore=None, volumeConfirmation=None, rewardRiskRatio=None, atrPercent=None, rsi14=None))

    assert any("spread" in warning.lower() for warning in result.warnings)


def test_penalizes_high_atr() -> None:
    result = _scorer().score(_base(strategyStrength=80, atrPercent=4.5, emaAlignmentScore=None, volumeConfirmation=None, rewardRiskRatio=None, spreadPercent=None, rsi14=None))

    assert any("atr" in warning.lower() for warning in result.warnings)


def test_clamps_score_to_valid_range() -> None:
    high = _scorer().score(
        _base(
            strategyStrength=99,
            emaAlignmentScore=99,
            volumeConfirmation=True,
            rewardRiskRatio=3.0,
            spreadPercent=0.001,
            atrPercent=1.0,
        )
    )
    low = _scorer().score(
        _base(
            marketRegime="Ranging",
            strategyStrength=5,
            spreadPercent=0.2,
            atrPercent=5.0,
            rsi14=80,
            emaAlignmentScore=None,
            volumeConfirmation=None,
            rewardRiskRatio=None,
        )
    )

    assert high.advisory_score is not None and 0 <= high.advisory_score <= 100
    assert low.advisory_score is not None and 0 <= low.advisory_score <= 100


def test_advisory_eligible_cannot_authorize_trade() -> None:
    """AdvisoryEligible / tradeAllowed are advisory-only; they must never imply order authority."""
    result = _scorer().score(_base(strategyStrength=95))

    assert result.evaluation_status == EvaluationStatus.EVALUATED.value
    assert result.advisory_eligible is True
    assert result.trade_allowed is True
    # Compat alias only — AI must not place orders; callers must still require risk engine approval.
    assert "authorize" not in " ".join(result.reasons).lower()
    assert "place order" not in " ".join(result.reasons).lower()
    assert result.advisory_rules_version == "AdvisoryRules/v1"
