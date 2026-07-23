from enum import Enum


class MarketRegime(str, Enum):
    TRENDING = "Trending"
    RANGING = "Ranging"
    BREAKOUT = "Breakout"
    REVERSAL = "Reversal"
    HIGH_VOLATILITY = "HighVolatility"
    LOW_VOLATILITY = "LowVolatility"
    CHOPPY = "Choppy"
    ABNORMAL = "Abnormal"
    UNKNOWN = "Unknown"


class ConfidenceClassification(str, Enum):
    VERY_LOW = "VeryLow"
    LOW = "Low"
    MEDIUM = "Medium"
    HIGH = "High"
    VERY_HIGH = "VeryHigh"


class AnomalySeverity(str, Enum):
    NONE = "None"
    LOW = "Low"
    MEDIUM = "Medium"
    HIGH = "High"
    CRITICAL = "Critical"


class SignalDirection(str, Enum):
    LONG = "Long"
    SHORT = "Short"
    NONE = "None"
