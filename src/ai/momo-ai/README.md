# MOMO Quant AI Service

Standalone Python FastAPI service for advisory AI features: market regime detection, confidence scoring, anomaly detection, and trade explanations.

This service is **advisory only**. It does not place orders, approve risk, call exchange APIs, or use external LLM APIs.

## Requirements

- Python 3.11+ recommended
- pip

## Setup

From `src/ai/momo-ai`:

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

## Run Tests

```powershell
python -m pytest
```

## Compile Check

```powershell
python -m compileall app
```

## Run Locally

```powershell
uvicorn app.main:app --reload --host 127.0.0.1 --port 8001
```

Health check:

```powershell
curl http://127.0.0.1:8001/health
```

Interactive docs: http://127.0.0.1:8001/docs

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health` | Service health |
| POST | `/api/v1/ai/regime/detect` | Market regime detection |
| POST | `/api/v1/ai/confidence/score` | Confidence scoring |
| POST | `/api/v1/ai/anomaly/detect` | Anomaly detection |
| POST | `/api/v1/ai/explain/trade` | Trade explanation |

## Configuration

Environment variables (optional):

- `SERVICE_NAME` (default: `momo-ai`)
- `SERVICE_VERSION` (default: `0.1.0`)
- `HIGH_ATR_THRESHOLD` (default: `2.5`)
- `LOW_ATR_THRESHOLD` (default: `0.8`)
- `ABNORMAL_ATR_THRESHOLD` (default: `4.0`)
- `VOLUME_SPIKE_THRESHOLD` (default: `3.0`)
- `MAX_SAFE_SPREAD_PERCENT` (default: `0.08`)

## Boundaries

The AI service may detect regimes, score confidence, detect anomalies, and explain setups.

It must **not** place orders, create trades, approve risk, override risk rejection, or control live trading.
