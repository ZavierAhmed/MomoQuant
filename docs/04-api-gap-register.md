# MOMO Quant — API Gap and Continuation Register

- Repository: `ZavierAhmed/MomoQuant`
- Inspected SHA: `c8ba9e87a83b5c19ad574ef7f98f3e5340bd56a2`
- Scope: DOC1C1 documentation-only correction. DOC1 produced the initial inventory but failed independent accuracy review; this pass corrects exact request/response contracts without changing production code. No gap item is implemented by this document.

Statuses are restricted to: **Implemented**, **Implemented alias**, **Partially implemented**, **Missing candidate**, **Deferred**, **Internal-only**, **Historical**, **Not applicable/rejected**, and **Decision required**.

| Area/proposal | Draft expectation | Repository reality | Status | Evidence | Risk/dependency | Recommended disposition | Earliest allowed milestone |
|---|---|---|---|---|---|---|---|
| Auth | Nested user login | Flat JWT login DTO; me/logout exist | Implemented | AuthController.cs; LoginResponse.cs | JWT lifecycle remains client-side | Keep current contract | None |
| Users | User administration | Admin-only CRUD/disable | Implemented | UsersController.cs | Admin policy | Keep current contract | None |
| Exchanges | Exchange CRUD/test | Implemented under /api/v1/exchanges | Implemented | ExchangesController.cs | Credential handling | Keep current contract | None |
| API-key vault | Public vault API | No public API-key vault controller | Deferred | No controller/route | Live trading/security boundary | Do not implement here | Future approved milestone |
| Symbols | Symbol list/sync/status | Implemented and paged list | Implemented | SymbolsController.cs | Exchange freshness | Keep current contract | None |
| Market data | Candles/imports/settings/quality | Implemented under market-data | Implemented | MarketDataController.cs | Coverage | Keep current contract | None |
| Indicators | Snapshot/recalculation | Implemented under indicators | Implemented | IndicatorsController.cs | Candle prerequisites | Keep current contract | None |
| Strategies | Catalog/parameters/evaluation | Implemented; canonical portfolio has three codes | Implemented | StrategiesController.cs; CanonicalStrategyPortfolio.cs | Archived isolation | Keep current contract | None |
| Generic trading sessions | Top-level trading-session resource | No public generic resource; nested simulation resources exist | Decision required | No TradingSessionsController.cs | Model overlap | Resolve ownership first | Future approved milestone |
| Bot controls | /bot control API | No bot controller or hub | Missing candidate | No matching route | Live/safety scope | Do not infer from services | Future approved milestone |
| Backtests | Run/result families | Implemented under backtests | Implemented | BacktestsController.cs | Long-running jobs | Keep current contract | None |
| Replay | Session controls | Implemented under replay/sessions with step-forward/backward/speed | Implemented | ReplayController.cs | Separate control semantics | Keep current contract | None |
| Paper trading | Accounts/sessions | Resource routes with qualification binding | Implemented | PaperTradingController.cs; PaperSessionService.cs | Start/resume revalidation | Preserve fail-closed behavior | None |
| Live trading | Real order execution | Disabled/deferred; no public route | Deferred | No live trading controller | Credentials and safety | Remain blocked | B1C7 or approved scope |
| Signals | Top-level /signals | No top-level route; nested signal families exist | Missing candidate | Controller inventory | Duplicate resource model | Decision before implementation | Future approved milestone |
| AI | Advisory .NET/Python calls | Four internal Python paths plus .NET endpoints | Implemented | AiController.cs; AiServiceClient.cs; app/api/routes.py | No parameter optimize route | Keep current contract | None |
| Risk | Profiles/rules/decisions | Implemented | Implemented | RiskController.cs | Strategy integration | Keep current contract | None |
| Orders | Top-level /orders | No top-level route; nested families exist | Missing candidate | No OrdersController.cs | Ownership unresolved | Decision required | Future approved milestone |
| Trades | Top-level /trades | No top-level route; nested families exist | Missing candidate | No TradesController.cs | Ownership unresolved | Decision required | Future approved milestone |
| Positions | Top-level /positions | No top-level route; paper nested positions exists | Missing candidate | PaperTradingController.cs | Avoid duplicate route | Decision required | Future approved milestone |
| Reports | Overview and nested reports | Implemented under reports/overview and families | Implemented | ReportsController.cs | Query shapes vary | Keep current contract | None |
| Monitoring | Health/status/diagnostics | Implemented | Implemented | MonitoringController.cs | Some bounded lists | Keep current contract | None |
| Audit | Admin audit query | Implemented under /api/v1/audit/* | Implemented | AuditController.cs | Required evidence separate from telemetry | B1C6D3 catalog later | B1C6D3 |
| Settings | Trading settings | Read/update/reset implemented | Implemented | TradingSettingsController.cs | Admin reset | Keep current contract | None |
| Notifications | Notification API | No public route | Deferred | No NotificationsController.cs | Product scope absent | Do not invent | Future approved milestone |
| SignalR | Multiple hubs | One live-market hub and seven events | Implemented | Program.cs; LiveMarketSignalREventPublisher.cs | Dashboard does not consume hub | Keep limitation explicit | None |
| Internal Python APIs | Public .NET-style service | FastAPI health plus four advisory routes | Internal-only | app/main.py; app/api/routes.py | Separate envelope | Keep separate | None |
| Authorization | Simplified role summary | Four named policies plus ordinary Authorize | Implemented | AuthorizationPolicies.cs; controller attrs | Action-level detail | Keep current contract | None |
| Response envelope | Universal code/trace envelope | ApiResponse<T> success/message/data/errors | Implemented | MomoQuant.Shared/Contracts/ApiResponse.cs | Health/download exceptions | Keep exceptions explicit | None |
| Pagination | All lists paged | PagedRequest/PagedResult only on selected routes; limits/unpaged elsewhere | Partially implemented | PagedRequest.cs; PagedResult.cs | Do not universalize | Document per route | None |
| Correlation IDs | Backend trace contract | Frontend generates X-Correlation-Id; shared backend echo absent | Partially implemented | frontend apiClient.ts | Observability consistency | Decide before standardization | Future approved milestone |
| OpenAPI/Swagger | Committed contract | Runtime Swagger with JWT definition/UI | Partially implemented | Program.cs | Generated doc can drift | Inventory is current evidence | None |
| SK System | Trading strategy API | Diagnostic system with /sk and /sk-system aliases | Implemented alias | TradingSystemsController.cs | Not a strategy | Keep aliases | None |
| SK LivePaper | Live execution | Simulation/diagnostic module only | Implemented | SkLivePaperController.cs | Not live trading | Keep blocked boundary | None |
| Validation Lab | Experiments/training/holdout | Broad research API implemented | Implemented | ValidationLabController.cs | Research policy split | Keep current contract | None |
| Strategy Lab | Runs/candidates/risk/gates | Implemented; canonical three-run restriction | Implemented | StrategyLabController.cs | Portfolio restriction | Keep current contract | None |
| Benchmarks | Benchmark lifecycle | Implemented | Implemented | StrategyBenchmarksController.cs | Long-running lifecycle | Keep current contract | None |
| Research/optimization | Validation/optimization/approvals | Implemented | Implemented | StrategyResearchController.cs | Not deployment qualification alone | Keep separated | None |
| Exports | Scopes/jobs/download | Implemented; download is raw file | Implemented | ExportsController.cs | File response exception | Keep current contract | None |
| Admin cleanup | Fake data/clean baseline | Admin-only preview/execute | Implemented | AdminDataCleanupController.cs; AdminSystemCleanupController.cs | Destructive operations | Keep explicit gate | None |
| Simulation summaries | Summary by source/id | Implemented | Implemented | SimulationSummariesController.cs | Source-specific data | Keep current contract | None |
| Frontend/backend: SignalR | Dashboard realtime consumption | No verified frontend hub connection | Partially implemented | LiveMarketHub.cs; frontend modules | Realtime UI absent | Do not claim consumed | Decision required |
| Frontend/backend: reports | Full report UI | Client exposes subset; backend exposes more | Partially implemented | reportsApi.ts; ReportsController.cs | UI coverage gap | Track client work separately | Future approved milestone |
| Frontend/backend: exports | Envelope download | Client builds raw URL; backend returns file | Implemented | exportsApi.ts; ExportsController.cs | Non-envelope response | Keep exception | None |
| Legacy examples | Greenfield generic routes/old strategy names | Not implemented; historical/deferred only | Historical | Existing drafts and this register | Future agents may misread | Do not advertise | None |

## Priority and continuation

1. Preserve the implemented transactional audit and deployment-qualification boundaries before adding safety-critical state changes.
2. Resolve product ownership for generic trading sessions/orders/trades/positions rather than duplicating nested resources.
3. Keep live trading, API-key vault, bot controls, notifications, and extra hubs deferred until an authoritative milestone exists.
4. Treat frontend coverage and correlation/SignalR limitations as documented mismatches; do not reclassify them as implemented.
5. No authoritative specifications for B1C6D2, B1C6D3, or B1C7 are present at this SHA; a future prompt is required.

## Stale draft strings

Legacy strings such as `/api/v1/audit-logs`, `/api/v1/paper/start`, `/api/v1/paper/stop`, `/api/v1/replay/{id}/step`, `/api/v1/reports/dashboard-summary`, `/api/v1/bot`, `/hubs/bot`, `/hubs/market`, `/hubs/trading`, `/hubs/replay`, `/hubs/monitoring`, `EMA_PULLBACK`, `LIQUIDITY_SWEEP`, `totalItems`, and `/ai/parameters/optimize` are not current implemented contracts. They appear only in this explicitly historical/deferred register.
