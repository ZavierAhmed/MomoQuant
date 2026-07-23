from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

    service_name: str = "momo-ai"
    service_version: str = "0.1.0"
    host: str = "127.0.0.1"
    port: int = 8001

    high_atr_threshold: float = 2.5
    low_atr_threshold: float = 0.8
    abnormal_atr_threshold: float = 4.0
    volume_spike_threshold: float = 3.0
    extreme_volume_spike_threshold: float = 5.0
    max_safe_spread_percent: float = 0.08
    ranging_max_range_percent: float = 2.0
    ema_cluster_percent: float = 0.5
    breakout_range_percent: float = 2.0


settings = Settings()
