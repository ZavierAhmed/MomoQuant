# Milestone 22.2 Acceptance Snapshot (partial live)

Generated during manual verification. UTF-8.

## Builds / Tests
- Backend Api: build succeeded (0 errors)
- ValidationLab unit tests: 49 passed, 0 failed
- Frontend npm run build: succeeded
- Migration `20260718100000_AddValidationLab222Exclusivity`: present; EF reports DB up to date

## A3 (Experiment Id=8) — COMPLETE
- Type: ValidateExistingFrozenConfiguration, sourceStrategyLabRunId=28
- Range: 2026-07-01 → 2026-07-14, RequiredWarmupCandles=600
- ValidationMetricsVersion: ValidationMetrics/v1.2
- HoldoutExclusivityPolicyVersion: ValidationHoldoutExclusivity/v1
- Candles: total 1344, train 940, val 404
- Train RawStrategy: persisted=238, metricIncluded=238, excluded=0
- Val RawStrategy: persisted=144, metricIncluded=139, excluded=5, overlaps=5
- Metric union: 238+139=377; intersection empty
- Excluded overlap fingerprints: 077580D802819209, B3164119173CD0B2, D1C21FA248CC98FB, F764B923DCBCA215, FBA84CCE6EBF3C74
- Train gross/net expectancy R: -0.12 / -0.12 (summary-derived path for training Raw at run time)
- Val gross/net expectancy R: 0.08515939 / -48.91119449 (FromCandidates v1.2)
- Val gross/net PF: 1.36794595 / 0.76127985
- Reconciliation: ExplainedSessionBoundaryDifference (exclusivity AffectsMetrics=false)
- Leakage: Passed
- MetricConsistency: Passed
- ExportVerification: Passed
- Verdict: FailedNegativeTrainingExpectancy (not invented Passed)
- Readiness (lab): ReadyWithWarnings
- Exports: JSON (exportId 5), CSV (6), PDF (7) — CSV includes overlap-candidates.csv; PDF includes Holdout Exclusivity

## C2 status
- Exp 11: Apr–Jul range, warmup=600 — hung ~trial 21/25 (client 60m timeout / long detector); left TrainingRunning
- Exp 16 (C2b): same range, warmup=100 — interrupted when API process exited mid-train (~51%)
- Exp C2c: restarting full prepare→train→freeze→validate (see live IDs in c2c-*.json)

## Policy
EarlierOccurrenceOwnsFingerprint: training owns shared SetupFingerprint; validation occurrence is CrossSegmentOverlapExcludedFromValidation (audit-only, PortfolioMutationAllowed=false).
