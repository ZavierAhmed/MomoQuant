from pydantic import BaseModel, Field


class AnomalyDetectRequest(BaseModel):
    symbol: str
    timeframe: str
    atr_percent: float | None = Field(default=None, alias="atrPercent")
    volume: float | None = None
    volume_sma20: float | None = Field(default=None, alias="volumeSma20")
    spread_percent: float | None = Field(default=None, alias="spreadPercent")
    candle_range_percent: float | None = Field(default=None, alias="candleRangePercent")
    price_gap_percent: float | None = Field(default=None, alias="priceGapPercent")

    model_config = {"populate_by_name": True}


class AnomalyDetectResponse(BaseModel):
    is_anomalous: bool = Field(alias="isAnomalous")
    severity: str
    reasons: list[str]

    model_config = {"populate_by_name": True, "by_alias": True}
