from app.core.config import Settings, settings
from app.models.common import MarketRegime
from app.models.regime import RegimeDetectRequest, RegimeDetectResponse


class RegimeDetector:
    def __init__(self, config: Settings | None = None) -> None:
        self._config = config or settings

    def detect(self, request: RegimeDetectRequest) -> RegimeDetectResponse:
        if not self._has_minimum_data(request):
            return RegimeDetectResponse(
                regime=MarketRegime.UNKNOWN.value,
                confidence=0,
                reasons=["Insufficient indicator data was provided for regime detection."],
            )

        abnormal = self._detect_abnormal(request)
        if abnormal is not None:
            return abnormal

        high_vol = self._detect_high_volatility(request)
        if high_vol is not None:
            return high_vol

        low_vol = self._detect_low_volatility(request)
        if low_vol is not None:
            return low_vol

        breakout = self._detect_breakout(request)
        if breakout is not None:
            return breakout

        reversal = self._detect_reversal(request)
        if reversal is not None:
            return reversal

        trending = self._detect_trending(request)
        if trending is not None:
            return trending

        ranging = self._detect_ranging(request)
        if ranging is not None:
            return ranging

        return RegimeDetectResponse(
            regime=MarketRegime.CHOPPY.value,
            confidence=45,
            reasons=[
                "EMA alignment is unclear.",
                "Market structure does not strongly match a single regime.",
            ],
        )

    def _has_minimum_data(self, request: RegimeDetectRequest) -> bool:
        return request.close is not None and request.ema20 is not None and request.ema50 is not None

    def _detect_abnormal(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        reasons: list[str] = []
        atr = request.atr_percent or 0.0
        volume_ratio = self._volume_ratio(request)

        if atr >= self._config.abnormal_atr_threshold:
            reasons.append("ATR percent is extremely high.")

        if volume_ratio >= self._config.extreme_volume_spike_threshold:
            reasons.append("Volume spike is extreme compared to average volume.")

        if atr >= self._config.abnormal_atr_threshold and volume_ratio >= self._config.volume_spike_threshold:
            reasons.append("Combined volatility and volume indicate unstable conditions.")

        if not reasons:
            return None

        return RegimeDetectResponse(
            regime=MarketRegime.ABNORMAL.value,
            confidence=min(95, 70 + len(reasons) * 8),
            reasons=reasons,
        )

    def _detect_high_volatility(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        atr = request.atr_percent
        if atr is None or atr < self._config.high_atr_threshold:
            return None

        return RegimeDetectResponse(
            regime=MarketRegime.HIGH_VOLATILITY.value,
            confidence=min(90, int(60 + atr * 8)),
            reasons=[
                f"ATR percent {atr:.2f} is above the high-volatility threshold.",
            ],
        )

    def _detect_low_volatility(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        atr = request.atr_percent
        if atr is None or atr > self._config.low_atr_threshold:
            return None

        return RegimeDetectResponse(
            regime=MarketRegime.LOW_VOLATILITY.value,
            confidence=72,
            reasons=[
                f"ATR percent {atr:.2f} is below the low-volatility threshold.",
            ],
        )

    def _detect_breakout(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        volume_ratio = self._volume_ratio(request)
        range_percent = request.recent_range_percent or 0.0
        atr = request.atr_percent or 0.0

        volume_confirmed = volume_ratio >= 1.0
        range_extended = range_percent >= self._config.breakout_range_percent
        atr_elevated = self._config.low_atr_threshold < atr < self._config.abnormal_atr_threshold

        if not (volume_confirmed and range_extended):
            return None

        reasons = [
            "Volume is above the 20-period average.",
            "Price is extended from the recent range.",
        ]
        if atr_elevated:
            reasons.append("ATR is rising but remains below abnormal levels.")

        return RegimeDetectResponse(
            regime=MarketRegime.BREAKOUT.value,
            confidence=min(88, 65 + int(volume_ratio * 5)),
            reasons=reasons,
        )

    def _detect_reversal(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        rsi = request.rsi14
        if rsi is None:
            return None

        if rsi <= 30:
            return RegimeDetectResponse(
                regime=MarketRegime.REVERSAL.value,
                confidence=78,
                reasons=[
                    "RSI is oversold.",
                    "Price may be rejecting lower levels.",
                ],
            )

        if rsi >= 70:
            return RegimeDetectResponse(
                regime=MarketRegime.REVERSAL.value,
                confidence=78,
                reasons=[
                    "RSI is overbought.",
                    "Price may be rejecting higher levels.",
                ],
            )

        return None

    def _detect_trending(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        ema20 = request.ema20
        ema50 = request.ema50
        ema200 = request.ema200
        if ema20 is None or ema50 is None:
            return None

        ema_gap_percent = abs(ema20 - ema50) / ema50 * 100 if ema50 else 0.0
        if ema_gap_percent <= self._config.ema_cluster_percent:
            return None

        bullish = ema20 > ema50 and (ema200 is None or ema50 > ema200)
        bearish = ema20 < ema50 and (ema200 is None or ema50 < ema200)

        if not bullish and not bearish:
            return None

        reasons: list[str] = []
        confidence = 70

        if bullish:
            reasons.append("EMA alignment is bullish.")
            if request.swing_high_rising and request.swing_low_rising:
                reasons.append("Swing highs and swing lows are rising.")
                confidence += 12
        else:
            reasons.append("EMA alignment is bearish.")
            if request.swing_high_rising is False and request.swing_low_rising is False:
                reasons.append("Swing highs and swing lows are falling.")
                confidence += 12

        atr = request.atr_percent
        if atr is not None and atr < self._config.high_atr_threshold:
            reasons.append("ATR is within normal range.")
            confidence += 5

        return RegimeDetectResponse(
            regime=MarketRegime.TRENDING.value,
            confidence=min(95, confidence),
            reasons=reasons,
        )

    def _detect_ranging(self, request: RegimeDetectRequest) -> RegimeDetectResponse | None:
        ema20 = request.ema20
        ema50 = request.ema50
        rsi = request.rsi14
        range_percent = request.recent_range_percent

        if ema20 is None or ema50 is None or rsi is None or range_percent is None:
            return None

        ema_gap_percent = abs(ema20 - ema50) / ema50 * 100 if ema50 else 0.0
        if range_percent > self._config.ranging_max_range_percent:
            return None
        if ema_gap_percent > self._config.ema_cluster_percent:
            return None
        if not (40 <= rsi <= 60):
            return None

        return RegimeDetectResponse(
            regime=MarketRegime.RANGING.value,
            confidence=76,
            reasons=[
                "Recent range is narrow.",
                "EMA values are close together.",
                "RSI is neutral between 40 and 60.",
            ],
        )

    def _volume_ratio(self, request: RegimeDetectRequest) -> float:
        if request.volume is None or request.volume_sma20 in (None, 0):
            return 0.0
        return request.volume / request.volume_sma20
