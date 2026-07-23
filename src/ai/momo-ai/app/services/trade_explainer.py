from app.models.explanation import TradeExplainRequest, TradeExplainResponse

CAUTION_TEXT = (
    "This explanation is informational only. Final execution must still follow risk and execution rules."
)


class TradeExplainer:
    def explain(self, request: TradeExplainRequest) -> TradeExplainResponse:
        confidence_label = self._confidence_label(request.confidence_score)
        summary = (
            f"{request.symbol} {request.timeframe} produced a {request.signal_direction} "
            f"{self._format_strategy_name(request.strategy_code)} setup in a "
            f"{request.market_regime} regime with {confidence_label} confidence."
        )

        details = [
            f"Strategy reason: {request.strategy_reason}",
        ]

        if request.risk_decision.lower() == "approved":
            details.append("Risk engine approved the setup.")
            details.append(f"Risk reason: {request.risk_reason}")
        else:
            details.append(f"Risk engine decision: {request.risk_decision}.")
            details.append(f"Risk reason: {request.risk_reason}")

        if request.warnings:
            details.append(f"Warnings: {'; '.join(request.warnings)}")
        else:
            details.append("No major warnings were detected.")

        details.append(
            "This explanation does not guarantee a profitable trade or future performance."
        )

        return TradeExplainResponse(
            summary=summary,
            details=details,
            caution=CAUTION_TEXT,
        )

    def _confidence_label(self, score: float) -> str:
        if score >= 90:
            return "very high"
        if score >= 75:
            return "high"
        if score >= 60:
            return "medium"
        if score >= 40:
            return "low"
        return "very low"

    def _format_strategy_name(self, strategy_code: str) -> str:
        mapping = {
            "EMA_PULLBACK": "EMA Pullback",
            "VWAP_MEAN_REVERSION": "VWAP Mean Reversion",
            "LIQUIDITY_SWEEP": "Liquidity Sweep",
        }
        return mapping.get(strategy_code.upper(), strategy_code.replace("_", " ").title())
