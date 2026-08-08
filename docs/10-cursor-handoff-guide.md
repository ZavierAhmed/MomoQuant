# MOMO Quant — Agent handoff guide

## Purpose

This guide applies to Codex, Cursor, or another coding agent. Work milestone-by-milestone; do not rebuild the application from historical drafts.

## Required handoff sequence

1. Verify local branch, HEAD, worktree, local tracking ref, and authoritative remote `main`.
2. Inspect relevant production code, route attributes, DTOs, policies, frontend clients, Python routes, and tests.
3. Read the repository-aligned documents: `04-api-specification.md`, `04-api-route-inventory.md`, `04-api-gap-register.md`, then the relevant design notes.
4. Identify conflicts and record them; executable implementation and accepted tests win.
5. Stop on material scope ambiguity or a missing authoritative milestone definition.
6. Implement only the approved milestone and preserve accepted transaction/audit/qualification/runtime boundaries.
7. Run focused and full required verification, inspect the complete diff, and commit only authorized files.

## Authority rule

The latest accepted repository implementation and tests are authoritative for current behavior. DOC1 produced an initial inventory that failed independent accuracy review; DOC1C1 is the corrective documentation pass. Documentation is normative for future work only within the audited source snapshot and explicit future-scope classifications after DOC1C1 is approved. Historical greenfield documents are design context, not permission to invent routes or modules.

## Non-negotiable boundaries

- Do not implement live trading, real-order placement, or an API-key vault without an explicit approved milestone.
- Keep the canonical active portfolio to the three MOMO strategies; legacy records remain isolated.
- SK System is diagnostic, not a trading strategy.
- Preserve fail-closed required audit evidence and deployment qualification.
- Do not silently change routes, DTOs, response envelopes, parameters, thresholds, or trading behavior.
- Do not start multiple milestones together or infer B1C6D2/B1C6D3/B1C7 content.

## Handoff record

Record the inspected SHA, branch, remote SHA, changed files, focused/full test counts, known warnings, blockers, and the exact next approved milestone. State whether anything was pushed. Keep temporary extraction logs outside the repository.

## Useful references

- `docs/04-api-specification.md` — current API contract.
- `docs/04-api-route-inventory.md` — every implemented REST route, SignalR event, and Python route.
- `docs/04-api-gap-register.md` — explicit gaps, mismatches, decisions, and dependencies.
- `docs/08-cursor-implementation-plan.md` — current continuation rules.
