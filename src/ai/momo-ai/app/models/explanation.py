from pydantic import BaseModel, Field


class TradeExplainRequest(BaseModel):
    symbol: str
    timeframe: str
    strategy_code: str = Field(alias="strategyCode")
    signal_direction: str = Field(alias="signalDirection")
    market_regime: str = Field(alias="marketRegime")
    confidence_score: float = Field(alias="confidenceScore", ge=0, le=100)
    risk_decision: str = Field(alias="riskDecision")
    risk_reason: str = Field(alias="riskReason")
    strategy_reason: str = Field(alias="strategyReason")
    warnings: list[str] = Field(default_factory=list)

    model_config = {"populate_by_name": True}


class TradeExplainResponse(BaseModel):
    summary: str
    details: list[str]
    caution: str
