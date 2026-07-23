from fastapi import FastAPI

from app.api.routes import router as ai_router
from app.core.config import settings

app = FastAPI(
    title="MOMO Quant AI Service",
    version=settings.service_version,
    description="Advisory AI service for MOMO Quant. Does not place orders or approve risk.",
)

app.include_router(ai_router)


@app.get("/health")
def health() -> dict[str, str]:
    return {
        "status": "healthy",
        "service": settings.service_name,
        "version": settings.service_version,
    }
