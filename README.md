# MOMO Quant

AI-assisted quantitative crypto futures trading platform.

## Repository Structure

```text
docs/                 Project documentation (source of truth)
src/
  backend/            .NET 8 modular monolith
  frontend/           React + Vite + Tailwind dashboard
  ai/                 Python FastAPI AI service
deploy/               Docker Compose deployment files
scripts/              Backup, restore, deploy, and log scripts
```

## Prerequisites

- .NET 8 SDK
- Node.js LTS
- Python 3.11+
- Docker Desktop

## Backend

```bash
cd src/backend
dotnet build MomoQuant.sln
dotnet test MomoQuant.sln
```

Run the API:

```bash
dotnet run --project src/MomoQuant.Api
```

## Frontend

```bash
cd src/frontend/momo-dashboard
npm install
npm run dev
```

## AI Service

```bash
cd src/ai/momo-ai
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
```

## Local Infrastructure

Copy `deploy/.env.example` to `deploy/.env`, update values, then:

```bash
docker compose -f deploy/docker-compose.yml up -d
```

## Documentation

Read `/docs` before implementing features. Build milestone by milestone per `docs/08-cursor-implementation-plan.md`.

## Safety Rules

- No live trading in MVP
- AI and strategies never place orders
- Risk engine approves every trade before execution
- All timestamps use UTC
- Financial values use `decimal`
