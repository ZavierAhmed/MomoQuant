# MOMO Quant documentation

## Documentation status

| Document | Status at audited SHA | Use |
|---|---|---|
| `04-api-specification.md` | Current repository-aligned contract | Normative current API summary |
| `04-api-route-inventory.md` | Current exhaustive inventory (288 routes) | Normative implemented route evidence |
| `04-api-gap-register.md` | Current future/mismatch register | Scope and dependency decisions; not implementation authority |
| `01-srs.md`, `02-system-architecture.md`, `03-database-design.md`, `05-ai-ml-design.md`, `06-ui-ux-specification.md`, `07-devops-deployment.md`, `09-testing-qa-plan.md`, `11-local-secrets-and-hosting-security.md` | Historical or requiring future reconciliation | Design context only where it agrees with executable code |
| `08-cursor-implementation-plan.md`, `10-cursor-handoff-guide.md` | Current agent workflow rules | Safe continuation and handoff |

API audit SHA: `c8ba9e87a83b5c19ad574ef7f98f3e5340bd56a2`.

> The latest accepted repository implementation and tests are authoritative for current behavior. Documentation is normative for future work only after it has been reconciled against that implementation and approved.

## How to use the package

Start with the handoff guide, then the current API specification and inventory. Use the gap register to identify unresolved scope. Do not treat an unreconciled greenfield document as permission to add routes, strategies, audit actions, or live trading.

Accepted safety boundaries remain in force: the three canonical strategies are the only active portfolio; SK System is diagnostic; deployment-qualified paper sessions are fail-closed; live trading is deferred.
