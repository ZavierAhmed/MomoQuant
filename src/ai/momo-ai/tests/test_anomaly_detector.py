from app.models.anomaly import AnomalyDetectRequest
from app.services.anomaly_detector import AnomalyDetector


def _detector() -> AnomalyDetector:
    return AnomalyDetector()


def test_detects_extreme_atr() -> None:
    result = _detector().detect(
        AnomalyDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            atrPercent=4.5,
        )
    )

    assert result.is_anomalous is True
    assert result.severity in {"High", "Critical"}
    assert any("atr" in reason.lower() for reason in result.reasons)


def test_detects_extreme_volume_spike() -> None:
    result = _detector().detect(
        AnomalyDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            volume=5000,
            volumeSma20=1000,
        )
    )

    assert result.is_anomalous is True
    assert any("volume" in reason.lower() for reason in result.reasons)


def test_detects_high_spread() -> None:
    result = _detector().detect(
        AnomalyDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            spreadPercent=0.12,
        )
    )

    assert result.is_anomalous is True
    assert any("spread" in reason.lower() for reason in result.reasons)


def test_returns_non_anomalous_for_normal_inputs() -> None:
    result = _detector().detect(
        AnomalyDetectRequest(
            symbol="BTCUSDT",
            timeframe="3m",
            atrPercent=1.2,
            volume=1100,
            volumeSma20=1000,
            spreadPercent=0.01,
            candleRangePercent=0.8,
            priceGapPercent=0.1,
        )
    )

    assert result.is_anomalous is False
    assert result.severity == "None"
