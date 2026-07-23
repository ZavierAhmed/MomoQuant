namespace MomoQuant.Domain.Audit;

public static class AuditActions
{
    public const string UserLoggedIn = "UserLoggedIn";
    public const string UserLoginFailed = "UserLoginFailed";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDisabled = "UserDisabled";
    public const string ExchangeCreated = "ExchangeCreated";
    public const string ExchangeUpdated = "ExchangeUpdated";
    public const string SymbolSynced = "SymbolSynced";
    public const string CandleImportStarted = "CandleImportStarted";
    public const string CandleImportCompleted = "CandleImportCompleted";
    public const string IndicatorRecalculated = "IndicatorRecalculated";
    public const string StrategyEnabled = "StrategyEnabled";
    public const string StrategyDisabled = "StrategyDisabled";
    public const string StrategyParametersUpdated = "StrategyParametersUpdated";
    public const string RiskProfileCreated = "RiskProfileCreated";
    public const string RiskProfileUpdated = "RiskProfileUpdated";
    public const string RiskRulesUpdated = "RiskRulesUpdated";
    public const string EmergencyStopChanged = "EmergencyStopChanged";
    public const string BacktestStarted = "BacktestStarted";
    public const string BacktestCompleted = "BacktestCompleted";
    public const string BacktestFailed = "BacktestFailed";
    public const string ReplayStarted = "ReplayStarted";
    public const string ReplayStopped = "ReplayStopped";
    public const string PaperAccountCreated = "PaperAccountCreated";
    public const string PaperAccountReset = "PaperAccountReset";
    public const string PaperSessionStarted = "PaperSessionStarted";
    public const string PaperSessionStopped = "PaperSessionStopped";
    public const string AiServiceUnavailable = "AiServiceUnavailable";
    public const string SystemHealthCheckFailed = "SystemHealthCheckFailed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UserLoggedIn, UserLoginFailed, UserCreated, UserUpdated, UserDisabled,
        ExchangeCreated, ExchangeUpdated, SymbolSynced, CandleImportStarted, CandleImportCompleted,
        IndicatorRecalculated, StrategyEnabled, StrategyDisabled, StrategyParametersUpdated,
        RiskProfileCreated, RiskProfileUpdated, RiskRulesUpdated, EmergencyStopChanged,
        BacktestStarted, BacktestCompleted, BacktestFailed, ReplayStarted, ReplayStopped,
        PaperAccountCreated, PaperAccountReset, PaperSessionStarted, PaperSessionStopped,
        AiServiceUnavailable, SystemHealthCheckFailed,
        "USER_LOGGED_IN", "USER_LOGIN_FAILED", "USER_CREATED", "USER_UPDATED", "USER_DISABLED",
        "EXCHANGE_CREATED", "EXCHANGE_UPDATED", "SYMBOL_SYNCED", "CANDLE_IMPORT_STARTED", "CANDLE_IMPORT_COMPLETED",
        "INDICATOR_RECALCULATED", "STRATEGY_ENABLED", "STRATEGY_DISABLED", "STRATEGY_PARAMETERS_UPDATED",
        "RISK_PROFILE_CREATED", "RISK_PROFILE_UPDATED", "RISK_RULES_UPDATED", "RISK_EMERGENCY_STOP_RULE_CHANGED",
        "BACKTEST_STARTED", "BACKTEST_COMPLETED", "BACKTEST_FAILED", "REPLAY_STARTED", "REPLAY_STOPPED",
        "PAPER_ACCOUNT_CREATED", "PAPER_ACCOUNT_RESET", "PAPER_SESSION_STARTED", "PAPER_SESSION_STOPPED",
        "AI_SERVICE_UNAVAILABLE", "SYSTEM_HEALTH_CHECK_FAILED"
    };

    public static readonly IReadOnlySet<string> SafetyActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EmergencyStopChanged, RiskRulesUpdated, UserLoginFailed, AiServiceUnavailable,
        SystemHealthCheckFailed, BacktestFailed, "RISK_EMERGENCY_STOP_RULE_CHANGED", "RISK_RULES_UPDATED",
        "USER_LOGIN_FAILED", "AI_SERVICE_UNAVAILABLE", "BACKTEST_FAILED", "PAPER_SESSION_FAILED",
        "SYSTEM_HEALTH_CHECK_FAILED"
    };
}
