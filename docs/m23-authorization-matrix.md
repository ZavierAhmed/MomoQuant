# Milestone 23.0 — Authorization Matrix (Research)

Policies:
- **ResearchRead**: Admin, Trader, Viewer
- **ResearchExecute**: Admin, Trader
- **Administration**: Admin only

| Controller | Action | Method | Route | Policy | Allowed roles |
|---|---|---|---|---|---|
| ValidationLabController | * (class) | GET | api/v1/validation-lab/** | ResearchRead | Admin, Trader, Viewer |
| ValidationLabController | CreateExperiment | POST | experiments | ResearchExecute | Admin, Trader |
| ValidationLabController | UpdateExperiment | PUT | experiments/{id} | ResearchExecute | Admin, Trader |
| ValidationLabController | PrepareData | POST | experiments/{id}/prepare-data | ResearchExecute | Admin, Trader |
| ValidationLabController | RunTraining | POST | experiments/{id}/run-training | ResearchExecute | Admin, Trader |
| ValidationLabController | ResumeTraining | POST | experiments/{id}/resume-training | ResearchExecute | Admin, Trader |
| ValidationLabController | RecoverTrials | POST | experiments/{id}/recover-trials | ResearchExecute | Admin, Trader |
| ValidationLabController | Freeze | POST | experiments/{id}/freeze | ResearchExecute | Admin, Trader |
| ValidationLabController | RunValidation | POST | experiments/{id}/run-validation | ResearchExecute | Admin, Trader |
| ValidationLabController | RecalculateMetrics | POST | experiments/{id}/recalculate-metrics | ResearchExecute | Admin, Trader |
| ValidationLabController | RecalculateVerdict | POST | experiments/{id}/recalculate-verdict | ResearchExecute | Admin, Trader |
| ValidationLabController | Clone | POST | experiments/{id}/clone | ResearchExecute | Admin, Trader |
| ValidationLabController | RerunExactly | POST | experiments/{id}/rerun-exactly | ResearchExecute | Admin, Trader |
| ValidationLabController | Closeout | POST | closeout/milestone-223 | ResearchExecute | Admin, Trader |
| StrategyLabController | * (class) | GET | api/v1/strategy-lab/** | ResearchRead | Admin, Trader, Viewer |
| StrategyLabController | CreateRun | POST | runs | ResearchExecute | Admin, Trader |
| StrategyLabController | Rerun | POST | runs/{id}/rerun | ResearchExecute | Admin, Trader |
| StrategyLabController | SyntheticTests | POST | strategies/{code}/synthetic-tests | ResearchExecute | Admin, Trader |
| BacktestsController | mutations | POST | api/v1/backtests/** | AdminOrTrader | Admin, Trader |
| PaperTradingController | mutations | POST | api/v1/paper-trading/** | AdminOrTrader | Admin, Trader |
| ReplayController | mutations | POST | api/v1/replay/** | AdminOrTrader | Admin, Trader |
| StrategyBenchmarksController | mutations | POST | api/v1/strategy-benchmarks/** | AdminOrTrader | Admin, Trader |
| ExportsController | mutations | POST | api/v1/exports/** | ResearchExecute (preferred) / AdminOrTrader | Admin, Trader |

Viewer must receive **403** on every mutating research endpoint. Anonymous receives **401**.
