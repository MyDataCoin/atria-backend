# Atria Backend. Final code generation prompt

Pass this prompt to a coding agent (Claude Code, Cursor, Copilot) together with
`atria-backend-architecture.md`. The architecture document is the source of truth for
project structure, entities, relationships and base patterns. This prompt adds the
missing integrations and hardens security and reliability on top of it.

---

## Role and goal

You are a senior .NET backend developer. Build a compilable .NET 9 solution for the
Atria platform (a real estate tokenization investment platform: RWA tokens, KYC,
investor applications, investments, rental dividends).

Atria is a regulated fintech. The system handles personal data, money and blockchain
tokens. Therefore security and reliability are not optional: the hardening sections
are required, not best effort.

## Source of truth and base structure

Take the solution structure, the set of entities, the relationships and the base
patterns strictly from `atria-backend-architecture.md`:

- 4 projects: `Atria.Domain`, `Atria.Application`, `Atria.Infrastructure`,
  `Atria.Api`, with references as in the document (Domain depends on nothing).
- Recreate the folder structure, entities and relationships exactly as in the document.
- Apply the patterns as described: State (no if/else over status), Strategy
  (selection through DI, not if/else over a string), Repository, Domain Events as the
  only channel between modules, Adapter, Factory Method with invariants.
- Controllers are thin, JWT with roles Admin / Investor / Compliance, Swagger with JWT.

Do not invent an alternative architecture and do not simplify the patterns.
Everything below is an addition and a hardening, not a replacement.

---

## Part 1. Missing integrations

### A. Asynchronous providers through webhooks

External KYC and payment providers work asynchronously: the backend creates a session,
the user completes it on the provider side, and the provider calls a webhook with the
result. The synchronous "call and get the result inline" model is wrong for them.

- Extend `IKycProviderStrategy`: a method to create a session returns a link or a
  session id, and a separate method parses the provider callback.
- Extend `IPaymentProviderStrategy` the same way.
- Add a webhooks controller: `POST /api/webhooks/kyc/{provider}` and
  `POST /api/webhooks/payments/{provider}`.
- Each webhook: verify the provider signature, check freshness (timestamp, replay
  protection), enforce idempotency by event id.
- The webhook result moves the aggregate State (`KycProfile` or `Investment`) and
  raises a domain event. Other modules are not touched.

### B. Didit as the primary KYC provider (Strategy)

- Implement `DiditKycProvider : IKycProviderStrategy` in
  `Atria.Infrastructure/Kyc/Providers`.
- This is the primary KYC provider for the project. Keep SumSub as a second strategy
  or remove it.
- Flow: create a verification session (hosted flow, returns a URL to redirect the
  user to), then handle the webhook with the result (approved or declined) and move
  `KycProfile` through the State objects.
- Configuration (api key, webhook secret, base url) only through `IOptions`.

### C. SMS registration through Nikita Pro

Two parts.

1. SMS adapter. Implement `NikitaProSmsAdapter : ISmsSender` into the
   `SmsNotificationAdapter` slot. Both OTP and regular SMS notifications go through it.
2. Phone registration (OTP) in the Auth module:
   - `POST /api/auth/register/phone/request-otp` with body `{ phone }`: generates a
     code, sends it through Nikita Pro, stores the code.
   - `POST /api/auth/register/phone/verify-otp` with body `{ phone, code }`: checks
     the code, creates a `User`, issues a JWT.
   - OTP requirements are in section N (OTP security).
- Nikita Pro configuration (login, sender, api key) only through `IOptions`.

### D. Compliance module. Tessera and the on-chain token

This is the Web3 core that the base architecture does not have. The base architecture
describes the Web2 part (KYC, applications, payments). Without this module the platform
does not perform tokenization. Add a `Compliance` module, keeping the rule that modules
communicate only through Domain Events.

- Reference Tessera through NuGet: `Tessera.Sdk`, `Tessera.Signing`,
  `Tessera.EntityFrameworkCore`. Tessera targets .NET 8 and works fine in a .NET 9
  app. Tessera has its own stores (`IDidStore`, `IIssuerRegistry`) with its own
  DbContext, which is set up alongside `AtriaDbContext`.
- Add a blockchain wallet address field to the investor (`WalletAddress`) with format
  validation.
- Declare an abstraction `ITesseraComplianceService` in Application, with the
  implementation in Infrastructure on top of the Tessera facades (Holder, Issuer,
  Verifier):
  - issue a DID and attestations to the investor (kyc_verified, resident, phone_verified),
  - verify a presentation against the project policy,
  - add the wallet address to the permissioned token allowlist (BEP-20 on BNB Chain)
    and revoke it,
  - anchor attestation Merkle roots.
- Domain Event handlers:
  - `KycApprovedEvent` triggers issuance of the DID and attestations to the investor,
  - a confirmed payment or an approved application triggers presentation verification,
    adding the address to the allowlist and initiating token allocation,
  - KYC revocation or a sanctions flag triggers a revocation bump and removal of the
    address from the allowlist.
- Network. Tessera ships adapters for Solana and Stellar, it has no EVM/BNB anchor.
  For the pilot it is acceptable to gate off-chain: the backend verifies the
  presentation in C# and writes the address into the BEP-20 allowlist, while anchoring
  of roots stays on Solana. A dedicated EVM anchor (an `IChainAnchor` implementation)
  is a separate task and does not block the backend.

---

## Part 2. Architecture hardening (reliability)

This is a system where domain events lead to movement of money and tokens. Therefore
event delivery and external effects must be reliable and free of duplicates.

### E. Transactional Outbox instead of dispatch after commit

The base approach of calling the dispatcher after `SaveChanges` in process is unsafe:
if the process crashes after commit but before dispatch, an event is lost, and that
event may trigger a token mint or a payout.

- Persist domain events into an outbox table in the same transaction as the aggregate.
- A separate background dispatcher (`BackgroundService`) reads the outbox and invokes
  handlers with at-least-once delivery.
- Handlers must be idempotent (see F).
- This is a lightweight outbox on a table plus a background worker, with no message
  broker. Do not bring in Kafka or RabbitMQ.

### F. Idempotency and exactly-once for money and tokens

- Any effect that moves money or tokens (payment confirmation, token allocation or
  mint, adding to the allowlist) must be idempotent on a stable key (event id or
  external operation id).
- Keep a log of processed keys, a retry does not create a second effect.
- Provider webhook callbacks are idempotent as well (providers retry delivery).

### G. Optimistic concurrency and concurrent transitions

- On aggregates that change state (`KycProfile`, `InvestorApplication`, `Investment`)
  add a version column (row version, `xmin` in Postgres or a dedicated `Version`) and
  optimistic concurrency.
- State transitions must handle races and duplicate or stale callbacks correctly: a
  repeated identical transition does not break the aggregate, a transition from an
  invalid state is rejected.

### H. Blockchain operations as a separate reliable component

- Put network interaction (allowlist, token allocation) behind an interface with
  retries, transaction status tracking and reconciliation (on-chain confirmation is
  asynchronous and may not succeed on the first try).
- Network operations also go through the outbox and are idempotent: a retry does not
  send a second transaction.
- The operation state (created, sent, confirmed, failed) is persisted and available
  for reprocessing.

### I. Observability and resilience to configuration errors

- Structured logging with a correlation id per request. PII, secrets, tokens and OTP
  codes never reach the logs.
- Health checks (`/health/live`, `/health/ready`) that check the database and external
  dependencies.
- Configuration validation at startup: if a secret or a required provider parameter is
  missing, the application fails immediately with a clear error, not at runtime.
- Database connection resilience (retry on transient Npgsql errors).
- API versioning (a version prefix in the route).

---

## Part 3. Security hardening

### J. Authentication and authorization

- JWT: a short-lived access token plus a refresh token with rotation. On refresh the
  old refresh token is invalidated. Detection of reuse of a revoked refresh token
  revokes the whole session.
- Roles Admin, Investor, Compliance plus resource ownership checks. A role is not
  enough: an investor can see and modify only their own applications, investments and
  documents. Implement resource-based authorization in the handlers or in a dedicated
  authorization behavior, not only through a role attribute.
- Enforce protection against horizontal access on every endpoint that takes a resource
  `id`.

### K. Secrets and configuration

- No secrets in code or in the repository `appsettings.json`. Secrets come from the
  environment or from a secret manager (Key Vault, Secrets Manager, Vault) and are
  bound through `IOptions`.
- The repository contains only `appsettings.json` with non-secret defaults and
  `appsettings.Example.json` with placeholders.

### L. Keys and signing of on-chain operations

- The backend does not hold blockchain private keys in process or in config. Signing
  of transactions (allowlist, mint) is delegated through an `IBlockchainSigner`
  interface to an external signer: KMS, HSM or a custody service. The key itself lives
  there.
- Critical on-chain operations are designed for multisig on the signer side, the
  backend only builds and submits the signing request.

### M. Webhook protection

- Verify the provider signature on every webhook (HMAC or asymmetric, per the
  provider specification).
- Replay protection: check the timestamp and the one-time nature of the event id.
- Idempotency (see F). Where possible, restrict to the provider IP allowlist.
- The webhook body is not trusted as a command: it only moves State and raises an
  event, through the same invariants as the normal flow.

### N. OTP protection and anti-abuse

- The OTP code is short-lived (for example, 5 minutes), single-use, stored as a hash,
  and compared in constant time.
- A limit on the number of entry attempts per code and a lock after it is exceeded.
- A rate limit on requesting a code per phone number and per IP (protection against
  brute force and against running up the SMS cost).
- A CAPTCHA or similar anti-automation on the code request and on registration is
  recommended.
- Rate limiting and lockout after a series of failures also on `login`.

### O. Personal data protection

- Sensitive KYC fields are encrypted at the application or column level (encryption at
  rest), not stored in the clear.
- Data minimization: store only what is necessary. Identity data is never written on
  chain (this is the Tessera principle, follow it off-chain too).
- Access to PII is written to the audit log. Retention and deletion on request are
  accounted for in the model (soft delete or anonymization, per the data requirements).

### P. Input validation, safe errors, headers

- Validate all incoming commands and queries (for example, FluentValidation), and
  reject at the edge.
- A global `ExceptionHandlingMiddleware` returns a sanitized ProblemDetails without
  internal details or stack traces to the outside. The full log with a correlation id
  is written internally.
- Security headers (HSTS, X-Content-Type-Options, a restrictive CSP for Swagger, and
  similar). HTTPS only.
- Parameterized EF Core queries, no SQL concatenation.

---

## Hard constraints

From the base architecture:

- No God Service: one use case is one Command or Query handler.
- No if/else over statuses: only State objects.
- No microservices: one process, one primary database, one Web API.
- No abstractions beyond the document (no separate Domain Services layer, no heavier
  CQRS).

Additionally for the hardening, but without re-architecting:

- Do not introduce a message broker, event sourcing or a separate read database. The
  outbox is a table plus a background worker.
- Do not store private keys in the backend. Signing only through `IBlockchainSigner`.
- A new provider is a Strategy, a new external service is an Adapter, communication
  between modules is only through Domain Events.
- All secrets through `IOptions` from a secure source.

## Definition of Done

- The solution compiles, all 4 projects plus tests build.
- The base structure, entities and patterns match `atria-backend-architecture.md`.
- Implemented: Didit (KYC through webhook), Nikita Pro (SMS adapter and OTP
  registration), the Compliance module with Tessera (DID and attestations, presentation
  verification, allowlist, anchoring or off-chain gating).
- Domain events flow through the transactional outbox, handlers are idempotent, money
  and token effects are exactly-once.
- On-chain signing only through `IBlockchainSigner`, no keys in process.
- Resource-based authorization, webhook and OTP protection, secrets outside code,
  sanitized errors, security headers.
- Unit tests on State transitions (`InvestorApplication`, `KycProfile`), including
  rejection of invalid and repeated transitions.
- A test for idempotency of a money or token effect handler.

## Response format and generation order

1. First show the file tree you will create and reconcile it with the structure from
   the document (plus the new folders: webhooks, the Compliance module, outbox,
   blockchain operations).
2. Create files by layer: Domain, then Application, then Infrastructure, then Api,
   then Tests.
3. After each layer, a short summary without pattern theory.
4. If something is ambiguous (for example, the exact set of DTO fields), pick the
   simplest solution in the spirit of the document and note it with a short comment in
   the code, without asking extra questions.

## Run commands

```
dotnet restore
dotnet ef migrations add InitialCreate --project src/Atria.Infrastructure --startup-project src/Atria.Api
dotnet ef database update --project src/Atria.Infrastructure --startup-project src/Atria.Api
dotnet run --project src/Atria.Api
```
