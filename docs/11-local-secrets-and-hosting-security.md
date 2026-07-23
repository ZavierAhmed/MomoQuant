# Local secrets & hosting security (Milestone 23.0)

After rotating any previously committed credentials, configure secrets **outside** tracked `appsettings*.json` files.

## Required configuration keys

| Key | Typical env var override | Notes |
|-----|--------------------------|-------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | MySQL connection string; password must not be empty/`CHANGE_ME` |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | Optional; when present, Redis password must not be a placeholder |
| `Jwt:Secret` | `Jwt__Secret` | ≥ 32 chars; placeholders / weak fragments rejected at startup |
| `Jwt:Issuer` | `Jwt__Issuer` | Default `MOMOQuant` |
| `Jwt:Audience` | `Jwt__Audience` | Default `MomoQuantDashboard` |
| `Seed:AdminPassword` | `Seed__AdminPassword` | Required in Development for admin seed |
| `Seed:AdminEmail` | `Seed__AdminEmail` | Optional override |

Do **not** put real secret values in git, docs, or chat logs.

## API user-secrets (recommended for local Development)

From `src/backend/src/MomoQuant.Api`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your-mysql-connection-string>"
dotnet user-secrets set "ConnectionStrings:Redis" "<your-redis-connection-string>"
dotnet user-secrets set "Jwt:Secret" "<your-long-random-jwt-secret>"
dotnet user-secrets set "Seed:AdminPassword" "<your-dev-admin-password>"
```

Startup calls `StartupSecretsValidator` (skipped only for the `Testing` environment or when `MOMO_SKIP_SECRETS_VALIDATION=true`).

## Environment variables

See root `.env.example` for documented names only. Copy to a local `.env` (gitignored) and fill values after rotation.

## Hosting security readiness (Package Q)

`HostingSecurityReadiness` is intentionally **Blocked** until a future hosting/security milestone. Current blockers:

- JWT stored in browser `localStorage` (XSS risk)
- No full server-side token revocation
- Cookie + CSRF protections not implemented
- Account lockout / auth rate-limiting incomplete

See also: Validation Laboratory readiness (`GET /api/v1/validation-lab/readiness`) and development-only `GET /api/v1/validation-lab/hosting-security`.

## Future hosting / security milestone (brief)

A later milestone must clear `HostingSecurityReadiness` before production internet exposure:

1. Move auth from `localStorage` JWT to httpOnly Secure cookies (or equivalent).
2. Implement CSRF protection for cookie-based sessions.
3. Full server-side token revocation / session invalidation.
4. Complete auth lockout and rate-limiting.
5. Re-evaluate hosting readiness and keep production blocked until status is Ready.
