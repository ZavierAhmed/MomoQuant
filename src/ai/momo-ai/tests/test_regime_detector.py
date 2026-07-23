from app.models.regime import RegimeDetectRequest
from app.services.regime_detector import RegimeDetector


def _detector() -> RegimeDetector:
    return RegimeDetector()


def test_returns_trending_for_bullish_ema_alignment() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            ema20=65000,
            ema50=64500,
            ema200=63000,
            close=65100,
            atrPercent=1.2,
            rsi14=58,
            swingHighRising=True,
            swingLowRising=True,
            recentRangePercent=1.5,
        )
    )

    assert result.regime == "Trending"
    assert result.confidence >= 70
    assert any("bullish" in reason.lower() for reason in result.reasons)


def test_returns_trending_for_bearish_ema_alignment() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            ema20=64000,
            ema50=64500,
            ema200=65000,
            close=63900,
            atrPercent=1.2,
            swingHighRising=False,
            swingLowRising=False,
            recentRangePercent=1.5,
        )
    )

    assert result.regime == "Trending"
    assert any("bearish" in reason.lower() for reason in result.reasons)


def test_returns_ranging_for_tight_range_and_neutral_rsi() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            ema20=65000,
            ema50=64980,
            close=65010,
            atrPercent=1.0,
            rsi14=50,
            recentRangePercent=1.2,
        )
    )

    assert result.regime == "Ranging"
    assert any("narrow" in reason.lower() for reason in result.reasons)


def test_returns_high_volatility_for_high_atr() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            ema20=65000,
            ema50=64500,
            close=65100,
            atrPercent=3.0,
        )
    )

    assert result.regime == "HighVolatility"


def test_returns_abnormal_for_extreme_atr_and_volume() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            ema20=65000,
            ema50=64500,
            close=65100,
            atrPercent=4.5,
            volume=6000,
            volumeSma20=1000,
        )
    )

    assert result.regime == "Abnormal"
    assert result.confidence >= 70


def test_returns_unknown_for_insufficient_input() -> None:
    result = _detector().detect(
        RegimeDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            close=None,
            ema20=None,
            ema50=64500,
        )
    )

    assert result.regime == "Unknown"
    assert result.confidence == 0
