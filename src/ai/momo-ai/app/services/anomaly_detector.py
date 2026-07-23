from app.core.config import Settings, settings
from app.models.anomaly import AnomalyDetectRequest, AnomalyDetectResponse
from app.models.common import AnomalySeverity


class AnomalyDetector:
    def __init__(self, config: Settings | None = None) -> None:
        self._config = config or settings

    def detect(self, request: AnomalyDetectRequest) -> AnomalyDetectResponse:
        reasons: list[str] = []
        severity_score = 0

        atr = request.atr_percent
        if atr is not None:
            if atr >= self._config.abnormal_atr_threshold:
                reasons.append("ATR percent is unusually high.")
                severity_score += 4
            elif atr >= self._config.high_atr_threshold:
                reasons.append("ATR percent is elevated.")
                severity_score += 1

        volume_ratio = self._volume_ratio(request)
        if volume_ratio >= self._config.extreme_volume_spike_threshold:
            reasons.append(f"Volume is more than {self._config.extreme_volume_spike_threshold:.0f}x average.")
            severity_score += 3
        elif volume_ratio >= self._config.volume_spike_threshold:
            reasons.append("Volume spike exceeds normal threshold.")
            severity_score += 2

        spread = request.spread_percent
        if spread is not None and spread > self._config.max_safe_spread_percent:
            reasons.append("Spread is above safe threshold.")
            severity_score += 2

        candle_range = request.candle_range_percent
        if candle_range is not None and candle_range >= 3.0:
            reasons.append("Current candle range is unusually wide.")
            severity_score += 1

        price_gap = request.price_gap_percent
        if price_gap is not None and price_gap >= 1.0:
            reasons.append("Price gap exceeds normal threshold.")
            severity_score += 1

        if not reasons:
            return AnomalyDetectResponse(
                is_anomalous=False,
                severity=AnomalySeverity.NONE.value,
                reasons=["No abnormal market conditions detected."],
            )

        severity = self._map_severity(severity_score)
        return AnomalyDetectResponse(
            is_anomalous=True,
            severity=severity.value,
            reasons=reasons,
        )

    def _volume_ratio(self, request: AnomalyDetectRequest) -> float:
        if request.volume is None or request.volume_sma20 in (None, 0):
            return 0.0
        return request.volume / request.volume_sma20

    def _map_severity(self, score: int) -> AnomalySeverity:
        if score >= 6:
            return AnomalySeverity.CRITICAL
        if score >= 4:
            return AnomalySeverity.HIGH
        if score >= 2:
            return AnomalySeverity.MEDIUM
        return AnomalySeverity.LOW
