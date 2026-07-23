from pydantic import BaseModel, Field


class ConfidenceScoreRequest(BaseModel):
    symbol: str
    timeframe: str
    strategy_code: str = Field(alias="strategyCode")
    signal_direction: str = Field(alias="signalDirection")
    market_regime: str = Field(alias="marketRegime")
    strategy_strength: float = Field(alias="strategyStrength", ge=0, le=100)
    ema_alignment_score: float | None = Field(default=None, alias="emaAlignmentScore")
    volume_confirmation: bool | None = Field(default=None, alias="volumeConfirmation")
    rsi14: float | None = None
    atr_percent: float | None = Field(default=None, alias="atrPercent")
    reward_risk_ratio: float | None = Field(default=None, alias="rewardRiskRatio")
    spread_percent: float | None = Field(default=None, alias="spreadPercent")
    recent_win_rate: float | None = Field(default=None, alias="recentWinRate")
    volume: float | None = None

    model_config = {"populate_by_name": True}


class ConfidenceScoreResponse(BaseModel):
    advisory_rules_version: str = Field(alias="advisoryRulesVersion")
    evaluation_status: str = Field(alias="evaluationStatus")
    is_strategy_supported: bool = Field(alias="isStrategySupported")
    supported_inputs: list[str] = Field(default_factory=list, alias="supportedInputs")
    missing_inputs: list[str] = Field(default_factory=list, alias="missingInputs")
    advisory_score: int | None = Field(default=None, alias="advisoryScore", ge=0, le=100)
    advisory_classification: str = Field(alias="advisoryClassification")
    # Backward-compatible mirrors of advisory fields
    confidence_score: int = Field(alias="confidenceScore", ge=0, le=100)
    classification: str
    reasons: list[str]
    warnings: list[str]
    advisory_eligible: bool = Field(alias="advisoryEligible")
    # Temporary compat alias — does NOT authorize trades.
    trade_allowed: bool = Field(alias="tradeAllowed")

    model_config = {"populate_by_name": True, "by_alias": True}
