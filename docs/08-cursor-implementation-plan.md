# MOMO Quant — Current implementation and continuation plan

**Historical note:** the original milestones 1–21 were a greenfield implementation plan and are not pending work. The repository at `c8ba9e87a83b5c19ad574ef7f98f3e5340bd56a2` is the production/API baseline. DOC1 was the first documentation reconciliation pass and failed independent accuracy review; DOC1C1 corrects its inventory without changing production behavior.

## Current rules

1. Inspect authoritative remote `main` and local Git state before every milestone.
2. Compare the requested milestone with the latest diff and accepted checkpoint; never trust a completion summary without source/test evidence.
3. Start one narrowly scoped milestone at a time.
4. Preserve accepted transaction, audit, qualification, and runtime-ordering boundaries.
5. Live trading, real-order placement, and API-key-vault implementation remain blocked.
6. Only `MOMO_ADAPTIVE_MTF_TREND_BREAKOUT`, `PRICE_STRUCTURE_BREAKOUT_RETEST`, and `MOMO_VOLATILITY_RANGE_REVERSION` are active for new work; legacy strategies remain isolated.
7. SK System is a diagnostic system, not a trading strategy.
8. Missing milestone definitions must not be invented. A gap-register row is not authorization to implement it.
9. Implementation requires a narrow approved prompt tied to `docs/04-api-gap-register.md`.

## Evidence order

Production code and route attributes → DTOs/shared contracts → authorization and tests → frontend clients/SignalR → Python service → domain/database capabilities → reconciled docs → historical drafts.

## Safe milestone loop

```text
verify remote and worktree
inspect accepted diff and relevant production/tests
read reconciled docs and gap register
define one approved change
implement only that change
run focused and required regression gates
inspect diff and commit narrowly
```

## Continuation boundary

The API contract reconciliation is considered current only after DOC1C1’s exact-contract and static gates pass at the audited SHA. Future work must name the exact gap, authorized files, acceptance gates, and no-push/commit boundary. Do not begin B1C6D2, B1C6D3, B1C7, or live trading without an authoritative prompt.

## Historical appendix

The early greenfield plan remains in Git history for context. Its initial-implementation assumptions do not override current executable behavior, the canonical portfolio, or the reconciled API inventory.
