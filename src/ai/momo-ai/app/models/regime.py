from pydantic import BaseModel, Field


class RegimeDetectRequest(BaseModel):
    symbol: str
    timeframe: str
    ema20: float | None = None
    ema50: float | None = None
    ema200: float | None = None
    close: float | None = None
    atr_percent: float | None = Field(default=None, alias="atrPercent")
    rsi14: float | None = None
    volume: float | None = None
    volume_sma20: float | None = Field(default=None, alias="volumeSma20")
    swing_high_rising: bool | None = Field(default=None, alias="swingHighRising")
    swing_low_rising: bool | None = Field(default=None, alias="swingLowRising")
    recent_range_percent: float | None = Field(default=None, alias="recentRangePercent")

    model_config = {"populate_by_name": True}


class RegimeDetectResponse(BaseModel):
    regime: str
    confidence: int = Field(ge=0, le=100)
    reasons: list[str]
