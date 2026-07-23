# MOMO Quant

## Part 10 — Cursor Handoff Guide

**Version:** 1.0 Draft

---

## 1. Purpose

This guide explains how to hand MOMO Quant documentation to Cursor and how to use Cursor without letting it corrupt the architecture. Do not ask Cursor to “build the bot.” Ask Cursor to implement one verified milestone at a time.

---

## 2. Required `/docs` Package

Place these files at the root of the project under `/docs`:

```text
/docs/01-srs.md
/docs/02-system-architecture.md
/docs/03-database-design.md
/docs/04-api-specification.md
/docs/05-ai-ml-design.md
/docs/06-ui-ux-specification.md
/docs/07-devops-deployment.md
/docs/08-cursor-implementation-plan.md
/docs/09-testing-qa-plan.md
/docs/10-cursor-handoff-guide.md
```

These are the source of truth.

---

## 3. Cursor Reading Order

```text
1. 01-srs.md
2. 02-system-architecture.md
3. 03-database-design.md
4. 04-api-specification.md
5. 05-ai-ml-design.md
6. 06-ui-ux-specification.md
7. 07-devops-deployment.md
8. 08-cursor-implementation-plan.md
9. 09-testing-qa-plan.md
10. 10-cursor-handoff-guide.md
```

For each milestone, list the most relevant docs in the prompt.

---

## 4. Cursor Master Instruction

Use this as the first instruction in Cursor:

```text
You are working on MOMO Quant, an AI-assisted quantitative crypto futures trading platform.

Before writing code, read the /docs folder. The documents are the source of truth.

Rules:
1. Build module by module.
2. Do not generate the whole app at once.
3. Use .NET 8 backend.
4. Use React + Vite + Tailwind dashboard.
5. Use MySQL with EF Core.
6. Use Redis for runtime cache/state where needed.
7. Use Python FastAPI for AI service.
8. Build as modular monolith first.
9. Do not create microservices except Python AI service.
10. Do not implement real live trading in MVP.
11. AI cannot place orders.
12. Strategies cannot place orders.
13. Risk engine approves every trade before execution.
14. Backtesting, replay, paper, future live share abstractions.
15. Log every important trading decision.
16. Use UTC timestamps.
17. Use decimal for trading financial values.
18. Use standard API response format.
19. Use role-based authorization.
20. Dangerous actions create audit logs.
21. Live trading remains disabled until readiness passes.
22. Return full updated files when modifying code.
23. Add tests for important logic.
24. Do not invent architecture that conflicts with docs.
```

---

## 5. Milestone Workflow

```text
1. Start a new Git branch.
2. Give Cursor one milestone prompt from 08-cursor-implementation-plan.md.
3. Tell Cursor which docs to read.
4. Let Cursor create/modify files.
5. Build.
6. Run tests.
7. Manually inspect code.
8. Fix issues.
9. Commit only when working.
10. Move to next milestone.
```

Never run multiple milestones together.

---

## 6. Correct Prompt Format

```text
Context:
You are working on MOMO Quant. Read the relevant docs in /docs.

Relevant docs:
- /docs/...

Task:
Implement [specific module].

Requirements:
[list exact requirements]

Architecture rules:
[list safety/architecture rules]

Acceptance criteria:
[list what must work]

Output:
Return full updated files.
```

---

## 7. Example Prompt — Milestone 1

```text
Context:
You are working on MOMO Quant.

Relevant docs:
- /docs/01-srs.md
- /docs/02-system-architecture.md
- /docs/08-cursor-implementation-plan.md
- /docs/09-testing-qa-plan.md
- /docs/10-cursor-handoff-guide.md

Task:
Implement Milestone 1 — Repository and Solution Skeleton.

Requirements:
Create repository structure for .NET 8 backend, React + Vite frontend, Python FastAPI AI service, Docker deployment files, scripts folder, and docs folder.

Inside src/backend, create MomoQuant.sln with projects:
- MomoQuant.Api
- MomoQuant.Application
- MomoQuant.Domain
- MomoQuant.Infrastructure
- MomoQuant.Persistence
- MomoQuant.Worker
- MomoQuant.Shared
- MomoQuant.UnitTests
- MomoQuant.IntegrationTests

Architecture rules:
- Do not implement trading logic.
- Do not implement live trading.
- Follow documented folder structure.
- Set correct references.

Acceptance criteria:
- Solution builds.
- All projects exist.
- References correct.
- No trading logic.

Output:
Return full created/updated files.
```

---

## 8. What Cursor Must Not Do

Cursor must not build entire app in one prompt, invent architecture, implement live trading early, let AI/strategies place orders, skip risk, skip audit logs, use float/double for money, ignore UTC, hide decisions, hardcode keys, expose secrets, put trading decisions in frontend, create unnecessary microservices, add Kubernetes early, or add complex AI before rule-based MVP works.

---

## 9. Required Human Review

Review architecture boundaries, risk logic, execution realism, DB relationships, decimal precision, authorization, secrets, audit logging, error handling, backtest assumptions, and paper trading behavior.

Cursor can generate code, but you are responsible for the system.

---

## 10. First Implementation Sequence

```text
1. Save all docs in /docs.
2. Create Git repository.
3. Create feature/milestone-01-skeleton.
4. Run Cursor Milestone 1 prompt.
5. Build solution.
6. Commit.
7. Create feature/milestone-02-domain.
8. Run Cursor Milestone 2 prompt.
9. Build and test.
10. Commit.
11. Continue milestone by milestone.
```

---

## 11. Local Setup Needed

Install .NET 8 SDK, Node.js LTS, Python 3.11+, Docker Desktop, Git, Cursor, optional MySQL/Redis clients.

Recommended local services: MySQL and Redis in Docker, .NET API local, React local, Python AI local.

---

## 12. Documentation Update Rule

If implementation reveals a better design, update the relevant doc before continuing. Do not let code and docs drift.

---

## 13. Final Rule

Never ask Cursor to build MOMO Quant. Ask Cursor to build the next verified milestone.
