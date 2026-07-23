from fastapi import APIRouter

from app.models.anomaly import AnomalyDetectRequest, AnomalyDetectResponse
from app.models.confidence import ConfidenceScoreRequest, ConfidenceScoreResponse
from app.models.explanation import TradeExplainRequest, TradeExplainResponse
from app.models.regime import RegimeDetectRequest, RegimeDetectResponse
from app.services.anomaly_detector import AnomalyDetector
from app.services.confidence_scorer import ConfidenceScorer
from app.services.regime_detector import RegimeDetector
from app.services.trade_explainer import TradeExplainer

router = APIRouter(prefix="/api/v1/ai")

_regime_detector = RegimeDetector()
_confidence_scorer = ConfidenceScorer()
_anomaly_detector = AnomalyDetector()
_trade_explainer = TradeExplainer()


@router.post("/regime/detect", response_model=RegimeDetectResponse)
def detect_regime(request: RegimeDetectRequest) -> RegimeDetectResponse:
    return _regime_detector.detect(request)


@router.post("/confidence/score", response_model=ConfidenceScoreResponse)
def score_confidence(request: ConfidenceScoreRequest) -> ConfidenceScoreResponse:
    return _confidence_scorer.score(request)


@router.post("/anomaly/detect", response_model=AnomalyDetectResponse)
def detect_anomaly(request: AnomalyDetectRequest) -> AnomalyDetectResponse:
    return _anomaly_detector.detect(request)


@router.post("/explain/trade", response_model=TradeExplainResponse)
def explain_trade(request: TradeExplainRequest) -> TradeExplainResponse:
    return _trade_explainer.explain(request)
