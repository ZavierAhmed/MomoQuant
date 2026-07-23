from __future__ import annotations

import json
import math
from dataclasses import dataclass
from enum import Enum
from functools import lru_cache
from pathlib import Path

from app.models.common import ConfidenceClassification
from app.models.confidence import ConfidenceScoreRequest, ConfidenceScoreResponse


class EvaluationStatus(str, Enum):
    EVALUATED = "Evaluated"
    UNSUPPORTED_STRATEGY = "UnsupportedStrategy"
    INSUFFICIENT_INPUTS = "InsufficientInputs"
    INVALID_INPUTS = "InvalidInputs"
    NOT_EVALUATED = "NotEvaluated"


ADVISORY_RULES_VERSION = "AdvisoryRules/v1"

_REGISTRY_PATH = Path(__file__).with_name("advisory_rules_v1.json")


@dataclass(frozen=True)
class StrategyAdvisorySpec:
    supported_regimes: frozenset[str]
    supported_inputs: tuple[str, ...]
    required_inputs: tuple[str, ...]


@lru_cache(maxsize=1)
def load_advisory_registry() -> tuple[str, dict[str, StrategyAdvisorySpec]]:
    payload = json.loads(_REGISTRY_PATH.read_text(encoding="utf-8"))
    version = str(payload.get("advisoryRulesVersion") or ADVISORY_RULES_VERSION)
    strategies: dict[str, StrategyAdvisorySpec] = {}
    raw_strategies = payload.get("strategies") or {}
    for code, spec in raw_strategies.items():
        strategies[str(code).upper()] = StrategyAdvisorySpec(
            supported_regimes=frozenset(spec.get("supportedRegimes") or []),
            supported_inputs=tuple(spec.get("supportedInputs") or []),
            required_inputs=tuple(spec.get("requiredInputs") or []),
        )
    return version, strategies


class ConfidenceScorer:
    def __init__(self, config=None) -> None:
        from app.core.config import Settings, settings

        self._config: Settings = config or settings
        self._rules_version, self._registry = load_advisory_registry()

    def score(self, request: ConfidenceScoreRequest) -> ConfidenceScoreResponse:
        validation_errors = self._validate_input_domain(request)
        strategy_code = (request.strategy_code or "").strip().upper()
        spec = self._registry.get(strategy_code) if strategy_code else None

        if not strategy_code:
            return self._terminal_response(
                evaluation_status=EvaluationStatus.NOT_EVALUATED,
                is_strategy_supported=False,
                supported_inputs=[],
                missing_inputs=["strategyCode"],
                reasons=["Strategy code was not provided."],
                warnings=["Advisory rules were not evaluated."],
            )

        if validation_errors:
            return self._terminal_response(
                evaluation_status=EvaluationStatus.INVALID_INPUTS,
                is_strategy_supported=spec is not None,
                supported_inputs=list(spec.supported_inputs) if spec else [],
                missing_inputs=[],
                reasons=[],
                warnings=validation_errors,
            )

        if spec is None:
            return self._terminal_response(
                evaluation_status=EvaluationStatus.UNSUPPORTED_STRATEGY,
                is_strategy_supported=False,
                supported_inputs=[],
                missing_inputs=[],
                reasons=[f"Strategy '{strategy_code}' is not registered in {self._rules_version}."],
                warnings=["Advisory rules did not evaluate this strategy; no regime penalty applied."],
            )

        missing = self._missing_required_inputs(request, spec)
        if missing:
            return self._terminal_response(
                evaluation_status=EvaluationStatus.INSUFFICIENT_INPUTS,
                is_strategy_supported=True,
                supported_inputs=list(spec.supported_inputs),
                missing_inputs=missing,
                reasons=["Required advisory inputs are missing."],
                warnings=["Advisory score was not computed because required inputs are missing."],
            )

        score = float(request.strategy_strength)
        reasons: list[str] = []
        warnings: list[str] = []

        if request.market_regime in spec.supported_regimes:
            score += 8
            reasons.append("Strategy matches market regime.")
        else:
            score -= 12
            warnings.append("Strategy does not match the current market regime.")

        if request.ema_alignment_score is not None:
            if request.ema_alignment_score >= 70:
                score += 6
                reasons.append("EMA alignment supports direction.")
            elif request.ema_alignment_score < 40:
                score -= 8
                warnings.append("EMA alignment is weak for the signal direction.")

        if request.volume_confirmation is True:
            score += 5
            reasons.append("Volume confirms move.")
        elif request.volume_confirmation is False:
            score -= 4
            warnings.append("Volume confirmation is missing.")

        if request.reward_risk_ratio is not None:
            if request.reward_risk_ratio >= 1.5:
                score += 6
                reasons.append("Reward-risk ratio is acceptable.")
            elif request.reward_risk_ratio < 1.0:
                score -= 10
                warnings.append("Reward-risk ratio is weak.")

        if request.spread_percent is not None:
            if request.spread_percent <= self._config.max_safe_spread_percent / 2:
                score += 3
                reasons.append("Spread is low.")
            elif request.spread_percent > self._config.max_safe_spread_percent:
                score -= 10
                warnings.append("Spread is too high.")

        if request.atr_percent is not None:
            if request.atr_percent <= self._config.high_atr_threshold:
                score += 3
                reasons.append("ATR is normal.")
            elif request.atr_percent >= self._config.abnormal_atr_threshold:
                score -= 12
                warnings.append("ATR is too high.")
            elif request.atr_percent > self._config.high_atr_threshold:
                score -= 6
                warnings.append("Volatility is elevated.")

        if request.rsi14 is not None:
            if self._rsi_contradicts_direction(request.signal_direction, request.rsi14):
                score -= 8
                warnings.append("RSI contradicts signal direction.")

        if request.recent_win_rate is not None and request.recent_win_rate < 40:
            score -= 4
            warnings.append("Recent win rate is weak.")

        final_score = int(max(0, min(100, round(score))))
        classification = self._classify(final_score)
        advisory_eligible = final_score >= 80

        if not reasons:
            reasons.append("Confidence score derived from available strategy and market inputs.")

        return ConfidenceScoreResponse(
            advisory_rules_version=self._rules_version,
            evaluation_status=EvaluationStatus.EVALUATED.value,
            is_strategy_supported=True,
            supported_inputs=list(spec.supported_inputs),
            missing_inputs=[],
            advisory_score=final_score,
            advisory_classification=classification.value,
            confidence_score=final_score,
            classification=classification.value,
            reasons=reasons,
            warnings=warnings,
            advisory_eligible=advisory_eligible,
            trade_allowed=advisory_eligible,
        )

    def _terminal_response(
        self,
        *,
        evaluation_status: EvaluationStatus,
        is_strategy_supported: bool,
        supported_inputs: list[str],
        missing_inputs: list[str],
        reasons: list[str],
        warnings: list[str],
    ) -> ConfidenceScoreResponse:
        return ConfidenceScoreResponse(
            advisory_rules_version=self._rules_version,
            evaluation_status=evaluation_status.value,
            is_strategy_supported=is_strategy_supported,
            supported_inputs=supported_inputs,
            missing_inputs=missing_inputs,
            advisory_score=None,
            advisory_classification=ConfidenceClassification.VERY_LOW.value,
            confidence_score=0,
            classification=ConfidenceClassification.VERY_LOW.value,
            reasons=reasons,
            warnings=warnings,
            advisory_eligible=False,
            trade_allowed=False,
        )

    def _missing_required_inputs(
        self,
        request: ConfidenceScoreRequest,
        spec: StrategyAdvisorySpec,
    ) -> list[str]:
        provided = {
            "strategyStrength": request.strategy_strength,
            "marketRegime": request.market_regime,
            "signalDirection": request.signal_direction,
            "emaAlignmentScore": request.ema_alignment_score,
            "volumeConfirmation": request.volume_confirmation,
            "rsi14": request.rsi14,
            "atrPercent": request.atr_percent,
            "rewardRiskRatio": request.reward_risk_ratio,
            "spreadPercent": request.spread_percent,
            "recentWinRate": request.recent_win_rate,
        }
        missing: list[str] = []
        for key in spec.required_inputs:
            value = provided.get(key)
            if value is None:
                missing.append(key)
                continue
            if isinstance(value, str) and not value.strip():
                missing.append(key)
        return missing

    def _validate_input_domain(self, request: ConfidenceScoreRequest) -> list[str]:
        errors: list[str] = []

        def reject_non_finite(name: str, value: float | None) -> None:
            if value is None:
                return
            if isinstance(value, bool) or not isinstance(value, (int, float)):
                errors.append(f"{name} must be a finite number.")
                return
            if math.isnan(value) or math.isinf(value):
                errors.append(f"{name} must be a finite number (NaN/Inf rejected).")

        reject_non_finite("strategyStrength", request.strategy_strength)
        reject_non_finite("emaAlignmentScore", request.ema_alignment_score)
        reject_non_finite("rsi14", request.rsi14)
        reject_non_finite("atrPercent", request.atr_percent)
        reject_non_finite("rewardRiskRatio", request.reward_risk_ratio)
        reject_non_finite("spreadPercent", request.spread_percent)
        reject_non_finite("recentWinRate", request.recent_win_rate)
        reject_non_finite("volume", request.volume)

        if request.atr_percent is not None and request.atr_percent < 0:
            errors.append("atrPercent must not be negative.")
        if request.spread_percent is not None and request.spread_percent < 0:
            errors.append("spreadPercent must not be negative.")
        if request.volume is not None and request.volume < 0:
            errors.append("volume must not be negative.")
        if request.reward_risk_ratio is not None and request.reward_risk_ratio <= 0:
            errors.append("rewardRiskRatio must be greater than zero.")
        if request.rsi14 is not None and not (0 <= request.rsi14 <= 100):
            errors.append("rsi14 must be between 0 and 100 inclusive.")
        if request.recent_win_rate is not None and not (0 <= request.recent_win_rate <= 100):
            errors.append("recentWinRate must be between 0 and 100 inclusive.")
        if request.ema_alignment_score is not None and not (0 <= request.ema_alignment_score <= 100):
            errors.append("emaAlignmentScore must be between 0 and 100 inclusive.")

        return errors

    def _rsi_contradicts_direction(self, direction: str, rsi: float) -> bool:
        normalized = direction.strip().lower()
        if normalized == "long" and rsi >= 75:
            return True
        if normalized == "short" and rsi <= 25:
            return True
        return False

    def _classify(self, score: int) -> ConfidenceClassification:
        if score >= 90:
            return ConfidenceClassification.VERY_HIGH
        if score >= 75:
            return ConfidenceClassification.HIGH
        if score >= 60:
            return ConfidenceClassification.MEDIUM
        if score >= 40:
            return ConfidenceClassification.LOW
        return ConfidenceClassification.VERY_LOW
