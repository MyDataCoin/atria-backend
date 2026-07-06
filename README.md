# Atria Backend

Real-estate tokenization investment platform (RWA): KYC, investments,
rental dividends, and an on-chain compliance/allowlist layer.

**.NET 9 · ASP.NET Core Web API · Clean Architecture · Modular Monolith · PostgreSQL · JWT**

Built strictly to `atria-backend-architecture-en.md` (structure, entities, patterns)
and hardened per `atria-codegen-prompt-final-en.md` (integrations, reliability, security).
`CONTRACTS.md` is the internal build contract that every layer was implemented against.

---

## Status

| | |
|---|---|
| Solution build | ✅ `dotnet build` — 0 warnings, 0 errors (4 projects + 3 test projects) |
| Tests | ✅ 75 passing (Domain 56 · Application 15 · Api integration 4) |
| Migration | ✅ `InitialCreate` generates 15 PostgreSQL tables; model matches migration |
| Source files | ~290 `.cs` files |
| Review | ✅ Multi-agent code review pass; all critical/high/medium findings fixed (see history) |

---

## Architecture

One process, one database, modules isolated along domain boundaries inside Clean
Architecture. Modules talk to each other **only through Domain Events** — never direct
service calls — so any module (e.g. Investments/Compliance) can later be split out.

```
src/
  Atria.Domain          # entities, value objects, domain events, State machines, factories. Depends on NOTHING.
  Atria.Application      # CQRS-light use cases (one handler per use case), abstractions, DTOs, event handlers, validators.
  Atria.Infrastructure  # EF Core, repositories, outbox, providers (Strategy), adapters, identity, compliance/Web3, DI.
  Atria.Api             # thin controllers, middleware, auth, Swagger, health, versioning, Program.cs.
tests/
  Atria.Domain.Tests · Atria.Application.Tests · Atria.Api.IntegrationTests
```

### Modules
Users · Kyc · Applications · Investments (+ Properties, Payments) · Documents ·
Notifications · Audit · **Compliance (Web3)** · Outbox.

### Patterns
| Pattern | Where |
|---|---|
| **State** (no if/else over status) | `KycProfile`, `Investment` — EF-friendly variant: only the status enum is persisted, the current state is derived via a stateless state factory. |
| **Strategy** (DI by type, no string switch) | `IKycProviderStrategy` (Didit*, Manual), `IPaymentProviderStrategy` (Stripe, BankTransfer). Handlers receive `IEnumerable<T>` and pick by `ProviderType`. |
| **Repository** | `IRepository<T>` + specialized repos over EF Core; Application never sees `DbContext`. |
| **Domain Events** | only inter-module channel; delivered via the transactional outbox. |
| **Adapter** | `S3DocumentStorageAdapter`, `NikitaProSmsAdapter`, `EmailNotificationAdapter`. |
| **Factory Method** | `InvestmentFactory` enforces creation invariants. |

\* Didit is the primary KYC provider.

---

## Reliability

- **Transactional outbox** — domain events are written to `outbox_messages` in the *same*
  transaction as the aggregate (in `AtriaDbContext.SaveChangesAsync`). A background
  `OutboxDispatcherBackgroundService` delivers them at-least-once with backoff. No event
  is lost between commit and dispatch; no message broker.
- **Idempotency / exactly-once** — every money/token effect (payment confirmation, token
  allocation, allowlist) guards on `IProcessedEventStore` keyed by event id, so retries
  produce the effect once. Covered by an Application test.
- **Optimistic concurrency** — `KycProfile`, `Investment` use the
  PostgreSQL `xmin` system column as a concurrency token (no extra migration column).
- **Reliable on-chain ops** — `BlockchainOperation` rows (`Created → Submitted → Confirmed
  → Failed`) are processed by `BlockchainOperationWorker` with retries, status tracking and
  reconciliation; idempotent on a stable key, so a retry never sends a second transaction.
- **Resilience** — Npgsql `EnableRetryOnFailure`; `/health/live` + `/health/ready`;
  options validated at startup (`ValidateOnStart`) so missing config fails fast.

## Security

- **JWT** short-lived access token + refresh token **rotation with reuse detection**
  (a replayed revoked token revokes the whole user session).
- **Resource-based authorization** in handlers via `ICurrentUserService` — an Investor can
  only read/modify their own applications, investments and documents (not just role checks).
- **Webhooks** verify provider signature + timestamp freshness (replay protection) +
  idempotency; the body is never trusted as a command, it only moves State.
- **OTP** (phone registration via Nikita Pro): short-lived, single-use, **stored hashed**,
  constant-time compare, per-code attempt lockout, per-phone rate limit. Login + OTP
  endpoints are rate-limited.
- **PII at rest** — `KycProfile.FullName` / `DocumentNumber` are AES-GCM encrypted via an
  EF value converter; identity data is never written on chain; PII access is auditable.
- **Secrets** never in code — all via `IOptions` from configuration/env; repo ships
  non-secret dev defaults + `appsettings.Example.json` placeholders.
- **No blockchain private keys in the backend** — signing is delegated to an external
  signer through `IBlockchainSigner` (KMS/HSM/custody), designed for multisig.
- Global `ExceptionHandlingMiddleware` returns sanitized `ProblemDetails` (no stack
  traces); correlation id per request; security headers (HSTS, CSP, X-Content-Type-Options…).

---

## Getting started

### Prerequisites
- .NET SDK 9
- PostgreSQL 14+ (for running the API; tests use EF InMemory and need no DB)

### Configure secrets (do NOT commit real secrets)
`appsettings.json` has working dev placeholders so the app boots locally. For real
values use environment variables or user-secrets, e.g.:

```bash
dotnet user-secrets init --project src/Atria.Api
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=atria;Username=atria;Password=..." --project src/Atria.Api
dotnet user-secrets set "Jwt:SigningKey" "<a long random secret>" --project src/Atria.Api
dotnet user-secrets set "Encryption:Key" "<base64 of exactly 32 random bytes>" --project src/Atria.Api
# Didit / Stripe / NikitaPro / S3 / Blockchain secrets likewise
```
`Encryption:Key` must be base64 of **exactly 32 bytes** (AES-256).
See `appsettings.Example.json` for the full key list.

### Run
```bash
dotnet restore
dotnet ef database update --project src/Atria.Infrastructure --startup-project src/Atria.Api
dotnet run --project src/Atria.Api
```
Swagger UI (with JWT auth) is served in Development at `/swagger`.

To regenerate the migration:
```bash
dotnet ef migrations add <Name> --project src/Atria.Infrastructure --startup-project src/Atria.Api -o Persistence/Migrations
```

### Run with Docker (Postgres + API)
The repo ships a multi-stage `Dockerfile` and a `docker-compose.yml` that brings up
PostgreSQL **and** the API together. The API applies EF migrations on startup
(`Database__MigrateOnStartup=true` in compose), so the schema is created automatically.
```bash
docker compose up --build
```
- API: http://localhost:8080  (Swagger at http://localhost:8080/swagger)
- PostgreSQL: localhost:5432  (db `atria`, user `atria`, password `atria`)
- Data persists in the `atria-pgdata` volume. Tear down with `docker compose down`
  (add `-v` to also drop the volume).

Dev secrets come from `appsettings.json` defaults so the stack works out of the box;
the compose file only overrides the connection string to point at the `db` service.
For anything real, supply secrets via environment variables / a secret store and set
`Database__MigrateOnStartup=false`.

### Test
```bash
dotnet test
```

---

## Deployment (GitHub Actions → Ubuntu)

`.github/workflows/deploy.yml` runs on every push: it builds + tests, then (on the
default branch) SSHes into the Ubuntu server and brings the stack up with
`docker compose`. Docker is installed automatically on first run. PostgreSQL stays
**internal** to the compose network (deploy uses `-f docker-compose.yml`, excluding the
local override that publishes 5432); only the API port **8080** is reachable.

**One-time setup**

1. Create a GitHub repo and push this code (Actions only run on GitHub).
2. Add repository **Secrets** (Settings → Secrets and variables → Actions). Nothing
   sensitive lives in the repo — the workflow reads these:

   | Secret | Value |
   |---|---|
   | `DEPLOY_HOST` | server IP/hostname |
   | `DEPLOY_USER` | SSH user (prefer a dedicated `deploy` user) |
   | `DEPLOY_SSH_KEY` | private SSH key (recommended) — or use the password below |
   | `DEPLOY_SSH_PASSWORD` | SSH password (only if not using a key) |
   | `DEPLOY_SSH_PORT` | optional, defaults to 22 |

   With the `gh` CLI:
   ```bash
   gh secret set DEPLOY_HOST    --body "<server-ip>"
   gh secret set DEPLOY_USER    --body "<ssh-user>"
   gh secret set DEPLOY_SSH_KEY --body "$(cat ~/.ssh/atria_deploy)"   # recommended
   # or password auth instead of a key:
   # gh secret set DEPLOY_SSH_PASSWORD --body "<password>"
   ```
3. Push to `main`/`master` (or run the workflow manually) → the API comes up at
   `http://<server-ip>:8080` (Swagger at `/swagger`).

**Production hardening (before real traffic)**
- Use an SSH **key** + non-root `deploy` user instead of a root password.
- This serves plain **HTTP** on 8080. Put a reverse proxy (nginx/Caddy/Traefik) with
  TLS in front, switch `ASPNETCORE_ENVIRONMENT` to `Production` (enables HSTS), replace
  the dev secrets in `appsettings.json` (JWT/Encryption keys, DB password), and clear
  `Otp:DevFixedCode`.
- Open the firewall for the API port: `ufw allow 8080`.

---

## API (v1, prefix `api/v1`)

```
Auth        POST register/phone/request-otp · register/phone/verify-otp · refresh   (phone-only, KG +996; no email/password)
KYC         POST kyc/submit [Investor] · GET kyc/me [Investor] · POST kyc/{id}/review [Compliance]
Properties  GET properties · GET {id} · POST [Admin]
Investments POST investments [Investor] · POST investments/{investmentId}/payments [Investor] · GET me · GET {id} · GET portfolio
Documents   POST documents (multipart) [Investor] · GET {id} [owner/Admin/Compliance] · GET me
Notifications GET notifications/me · POST {id}/read
Audit       GET audit?entityType=&entityId= [Admin/Compliance]
Webhooks    POST webhooks/kyc/{provider} · POST webhooks/payments/{provider}   (signature-verified, idempotent)
Health      GET /health/live · GET /health/ready
```

---

## Notable implementation decisions

These are pragmatic choices made where the spec left room (per the prompt: pick the
simplest solution in the spirit of the document and note it).

- **Mediator** — a thin in-house `ISender`/`IRequestHandler` + `IPipelineBehavior`
  (validation, logging) instead of MediatR, to avoid an external/licensed dependency.
  Validation runs as a pipeline behavior (FluentValidation) and maps to `Error.Validation`.
- **Tessera (Web3 core)** — the `Tessera.Sdk` / `Tessera.Signing` /
  `Tessera.EntityFrameworkCore` NuGet packages are not publicly resolvable, so
  `TesseraComplianceService` is a **local in-house implementation that follows the Tessera
  principles**: identity data stays off-chain (`ComplianceProfile`, PII encrypted), only
  attestation Merkle roots are anchored on chain (`IChainAnchor`, Solana pilot stub), and
  the permissioned BEP-20 allowlist is updated via `IBlockchainOperationQueue` →
  `IBlockchainSigner`. `// NOTE:` markers flag where a real SDK call would go. This keeps
  the build green; swapping in the real SDK is an adapter change, not an architecture change.
- **Optimistic concurrency** — Npgsql 9.0.4 no longer exposes
  `UseXminAsConcurrencyToken()`, so an equivalent shadow `xmin` (`xid`) concurrency token
  is configured manually. Same runtime behavior, no extra column.
- **External provider field shapes** (Didit / Nikita Pro / Stripe webhooks) use sensible
  defaults marked with `// NOTE:` where the exact vendor payload should be confirmed.
- **Config** — package versions are centrally pinned (`Directory.Packages.props`) on the
  .NET 9 line; framework-coupled families stay on `9.0.*`.
