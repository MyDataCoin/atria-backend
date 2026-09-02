# Atria — Build Contracts (single source of truth for all agents)

This file pins the EXACT public surface every layer must implement so that work
done in parallel compiles together. **Do not change signatures here without
updating every dependent.** The two source-of-truth design docs are
`atria-backend-architecture-en.md` and `atria-codegen-prompt-final-en.md`.

The frozen contract layer (already written, do NOT recreate):
`Atria.Domain/Common/*`, all enums, `Atria.Application/Common/*`,
`Atria.Application/Abstractions/**`. Read those files; depend on them.

---

## 0. Global conventions

- **Target**: .NET 9, C# latest, `Nullable` + `ImplicitUsings` enabled (solution-wide via Directory.Build.props).
- **Namespaces**: file-scoped. Domain → `Atria.Domain.<Module>[.States|.Events]`.
  Application abstractions all use `namespace Atria.Application.Abstractions;`
  (regardless of folder). Application use cases →
  `Atria.Application.<Module>.Commands|Queries|EventHandlers|Dtos`.
  Result/Unit/Error → `Atria.Application.Common`.
- **No new NuGet packages.** Only the centrally pinned ones (see Directory.Packages.props).
  Do not edit `.csproj` or `Directory.Packages.props`.
- **Patterns are mandatory**: State (no if/else over status), Strategy (DI by type),
  Repository, Domain Events (only inter-module channel), Adapter, Factory Method.
- **Style**: `sealed` classes where not meant for inheritance; concise XML/`//`
  comments matching existing density; one use case = one handler (no God Service).
- Handlers return `Result` / `Result<T>` for expected failures (don't throw for those).
- Domain invariant violations throw `DomainException` / `InvalidStateTransitionException`.
- All async methods take `CancellationToken ct` last.

### State pattern — EF-friendly variant (use everywhere)
Persist ONLY the status enum on the entity. Derive the current state from it via a
stateless state factory. Transition methods look like:
```csharp
public void Approve()
    => Status = KycStateFactory.Create(Status).Approve(this).Status;
```
State classes are stateless singletons; any data (e.g. rejection reason) lives on the
entity, not the state. The entity exposes `internal void RaiseDomainEvent(IDomainEvent e)`
so state objects can raise events. This deviates slightly from the doc's mutable
`_state` field, intentionally, so EF can rehydrate from a single column.

---

## 1. DOMAIN entities (project Atria.Domain)

All aggregates derive `AggregateRoot`; child entities derive `Entity`. Use `private`
ctors + static factory methods. Ids are `Guid.NewGuid()` in the factory.

### Users (`Atria.Domain.Users`)
```
sealed class User : AggregateRoot
  string? PhoneNumber; Role Role;
  bool IsActive; bool IsPhoneVerified; DateTime? DeletedAtUtc;
  static User CreateFromPhone(string phoneNumber, Role role)   // role = Investor
  void MarkPhoneVerified(); void Deactivate();
  void SoftDelete(DateTime utc);   // sets DeletedAtUtc + IsActive=false
```

### Kyc (`Atria.Domain.Kyc`)
```
sealed class KycProfile : AggregateRoot
  Guid UserId; KycStatus Status; KycProviderType Provider;
  string? FullName;        // PII — encrypted at rest (see infra converters)
  string? DocumentNumber;  // PII — encrypted at rest
  string? Nationality; string? WalletAddress; string? ProviderSessionId; string? RejectionReason;
  static KycProfile Create(Guid userId);                       // Pending
  void Submit(KycProviderType provider, string sessionId, string? walletAddress,
              string? fullName, string? documentNumber, string? nationality); // -> UnderReview, KycSubmittedEvent
  void Approve();          // -> Approved, KycApprovedEvent
  void Reject(string reason); // -> Rejected, KycRejectedEvent (stores reason)
  internal void RaiseDomainEvent(IDomainEvent e);
States (`Atria.Domain.Kyc.States`): IKycState { KycStatus Status; IKycState Submit(KycProfile); IKycState Approve(KycProfile); IKycState Reject(KycProfile, string reason); }
  PendingKycState (Submit ok), UnderReviewKycState (Approve/Reject ok), ApprovedKycState (terminal), RejectedKycState (terminal)
  static class KycStateFactory { static IKycState Create(KycStatus status); }
Events (`Atria.Domain.Kyc.Events`, records : DomainEventBase):
  KycSubmittedEvent(Guid KycProfileId, Guid UserId)
  KycApprovedEvent(Guid KycProfileId, Guid UserId, string? WalletAddress)
  KycRejectedEvent(Guid KycProfileId, Guid UserId, string Reason)
```

### Investments (`Atria.Domain.Investments`)
```
sealed class Building : AggregateRoot   // groups the units sold inside it; issues NOTHING itself
  string Name; string? Description; string? Address; string? City; string? Developer;
  int? YearBuilt; int? Floors; string? BuildingType; IReadOnlyCollection<BuildingImage> Images;
  static Building Create(string name, ...descriptive optionals);
  void UpdateDetails(...optionals);  // only non-null args applied (PATCH semantics)
  BuildingImage AddImage(string url); BuildingImage? RemoveImage(Guid imageId);  // max Building.MaxImages
sealed class Property : AggregateRoot   // THE unit of issuance: standalone, or one unit of a Building
  string Name; string? Description; string? Address; decimal TotalValue; decimal TokenPrice;
  long TotalTokens; long AvailableTokens; long MinPurchaseTokens; string Currency;
  decimal MinPurchaseAmount;                          // derived: min × price
  decimal? AreaPerTokenSqM;   // derived: (OfferedAreaSqM ?? TotalAreaSqM) ÷ supply; null when neither
  PropertyStatus Status; bool SalesPaused;
  Guid? BuildingId; UnitType UnitType; string? UnitNumber; int? FloorNumber;
  int? RoomCount; decimal? TotalAreaSqM; IReadOnlyCollection<PropertyRoom> Rooms;
  decimal? UsableAreaSqM;     // ≤ TotalAreaSqM, enforced from both sides
  decimal? OfferedAreaSqM;    // the area actually placed when the issue is part of the object; frozen once sold
  decimal? LandAreaHectares;  // the PLOT: neither floor area nor divided across the issue
  string? DocumentedUse;      // permitted use per the title documents; NOT PropertyType (catalogue filter)
  string? BuildingClass; string? WallMaterial; string? Heating; string? Elevator;
  string? Security; string? Parking;                  // free text: a fixed list forces the nearest wrong answer
  string? LocationDescription;                        // the neighbourhood, apart from Description (the object)
  string? LandPlotCode; string? CadastralNumber;      // plot has a code, a built object gets a number
  ConstructionStage ConstructionStage; DateTime? PlannedCompletionDate; int? ReadinessPercent;
  bool? IsFreeOfEncumbrances; DateTime? EncumbranceCheckedAtUtc;  // null = nobody looked ≠ "nothing found"
  DateTime? PlacementOpensAtUtc; DateTime? PlacementClosesAtUtc; decimal? TargetAmount;
  int PlacementExtensionCount; decimal RaisedAmount; bool IsTargetMet;   // derived from supply × price
  PayoutFrequency PayoutFrequency; bool DistributesYet;  // false until commissioned, whatever was entered
  IReadOnlyCollection<PropertyImage> Images; IReadOnlyCollection<PropertyDocument> Documents;
  static Property Create(string name, string? description, string? address, decimal totalValue,
                         decimal tokenPrice, long totalTokens, string currency, ...descriptive optionals,
                         long minPurchaseTokens = TokenAmount.Smallest);
  void AssignToBuilding(Guid? buildingId);            // Guid.Empty == null == standalone
  void SetUnitDetails(UnitType?, string? unitNumber, int? floorNumber, int? roomCount, decimal? totalAreaSqM);
  void SetCharacteristics(decimal? usableAreaSqM, string? documentedUse, ...free-text optionals,
                          string? locationDescription);         // only non-null args applied
  void SetCadastralDetails(string? landPlotCode, string? cadastralNumber, decimal? landAreaHectares);
  void SetConstructionProgress(ConstructionStage, DateTime? plannedCompletion, int? readinessPercent);
  void RecordEncumbranceCheck(bool isFree, DateTime checkedAtUtc);   // both halves or neither
  void SchedulePlacement(DateTime? opens, DateTime? closes, decimal? target, decimal? offeredAreaSqM);
  void ExtendPlacement(DateTime newClosesAtUtc);      // counted: 4 extensions ≠ 1
  void ReplaceRooms(IEnumerable<(string Name, decimal AreaSqM)> rooms);  // whole-list swap; [] clears
  PropertyDocument AddDocument(string url, string fileName, string contentType,
                               PropertyDocumentCategory category, string? title);
  void ReserveTokens(long count);   // holds tokens for a new application; throws if count > AvailableTokens
  void ReleaseTokens(long count);   // returns tokens to the pool on reject/cancel/expiry
sealed class PropertyDocument : Entity   // the paperwork an owner attaches
  Guid PropertyId; string Url; string FileName; string ContentType;
  PropertyDocumentCategory Category;  // legal | technical_passport | valuation | collateral | construction_schedule | layout
  string? Title; string DisplayName;  // Title, falling back to FileName — the scanner's name is not the reader's
sealed class PropertyImage : Entity
  Guid PropertyId; string Url; PropertyImageKind Kind; string? Caption; int SortOrder;
  // Kind travels with the image: a render shown as a photograph is a picture of a building that
  // does not exist, shown to someone deciding whether to fund it. SortOrder[0] is the cover.
sealed class Investment : AggregateRoot
  // No payment on the platform: an application reserves tokens up front, an operator approves it.
  Guid InvestorId; Guid PropertyId; long TokenCount; decimal Amount; string Currency; decimal PricePerToken;
  InvestmentStatus Status; DateTime ReservedUntilUtc; string? ReferralToken;
  string? WalletAddress; string? TokenContractAddress; string? TransactionHash; OnChainStatus OnChainStatus;
  void Approve();               // Reserved -> Active   ; raises InvestmentActivatedEvent
  void Reject(string reason);   // Reserved -> Rejected ; raises InvestmentRejectedEvent (caller releases tokens)
  void Cancel();                // Reserved -> Cancelled; raises InvestmentCancelledEvent (caller releases tokens)
  void Expire();                // Reserved -> Expired  ; raises InvestmentExpiredEvent (caller releases tokens)
  internal void RaiseDomainEvent(IDomainEvent e);
enum InvestmentStatus { Reserved=0, Active=1, Rejected=2, Cancelled=3, Expired=4 }
States (`...Investments.States`): IInvestmentState { InvestmentStatus Status; Approve(...); Reject(...); Cancel(...); Expire(...); }
  ReservedState, ActiveState, RejectedState, CancelledState, ExpiredState. InvestmentStateFactory.Create(status).
Events (`...Investments.Events`):
  InvestmentCreatedEvent(Guid InvestmentId, Guid InvestorId, Guid PropertyId, decimal Amount)
  InvestmentActivatedEvent(Guid InvestmentId, Guid InvestorId, Guid PropertyId, long TokenCount, decimal Amount)
  InvestmentRejectedEvent(Guid InvestmentId, Guid InvestorId, string Reason)
  InvestmentCancelledEvent(Guid InvestmentId, Guid InvestorId)
  InvestmentExpiredEvent(Guid InvestmentId, Guid InvestorId, Guid PropertyId, long TokenCount)
Factory (`Atria.Domain.Factories`):
  static class InvestmentFactory {
    static Investment CreateForInvestor(Guid investorId, Guid propertyId, long tokenCount,
      string currency, decimal pricePerToken, DateTime reservedUntilUtc, string? referralToken = null)
      // Reserved ; raises InvestmentCreatedEvent. Amount is DERIVED (tokenCount × pricePerToken),
      // never passed in: the investor pays for the whole tokens they get, not the sum they typed. }
Token granularity (`Atria.Domain.Investments.TokenAmount`):
  const int Scale = 0; const long Smallest = 1;   // the contract's decimals() is zero — a share does not divide
  static long FromMoney(decimal amount, decimal pricePerToken);   // floor; what the money covers, never more
  static decimal CostOf(long tokens, decimal pricePerToken);      // what those tokens actually cost
  static BigInteger ToMinor(long tokens); static long FromMinor(BigInteger minor);
Reservation expiry: a background sweep (Atria.Infrastructure.Investments.ReservationExpiryBackgroundService)
  reclaims Reserved applications past ReservedUntilUtc (-> Expired, tokens released). Window + sweep pacing
  are configured via the InvestmentReservation section (WindowDays=3, SweepIntervalMinutes=15, SweepBatchSize=100).
```

### Documents (`Atria.Domain.Documents`)
```
sealed class DocumentRecord : AggregateRoot
  Guid OwnerUserId; DocumentType Type; string FileName; string ContentType; string StorageKey; long SizeBytes;
  static DocumentRecord Create(Guid ownerUserId, DocumentType type, string fileName, string contentType, string storageKey, long sizeBytes);
```

### Notifications (`Atria.Domain.Notifications`)
```
sealed class Notification : AggregateRoot
  Guid UserId; NotificationTemplate Template; NotificationChannel Channel; string Title; string Body;
  bool IsRead; DateTime? ReadAtUtc;
  static Notification Create(Guid userId, NotificationTemplate template, NotificationChannel channel, string title, string body);
  void MarkRead(DateTime utc);
```

### Audit (`Atria.Domain.Audit`)
```
sealed class AuditLogEntry : Entity     // NOT an aggregate; immutable record
  string EntityType; Guid? EntityId; string EventType; string? DataJson; Guid? UserId; string? CorrelationId; DateTime OccurredOnUtc;
  static AuditLogEntry FromDomainEvent(IDomainEvent e, string entityType, Guid? entityId, string? dataJson, string? correlationId);
  static AuditLogEntry ForAccess(string entityType, Guid? entityId, string action, Guid? userId, string? correlationId); // PII access logging
```

### Compliance (`Atria.Domain.Compliance`)
```
sealed class WalletAddress : ValueObject   // wraps EVM address ^0x[a-fA-F0-9]{40}$
  string Value;
  static WalletAddress Create(string value);          // throws DomainException if invalid
  static bool TryCreate(string value, out WalletAddress? addr);
  static bool IsValid(string value);
sealed class ComplianceProfile : AggregateRoot
  Guid InvestorId; string? Did; string? WalletAddress; bool IsAllowlisted; bool IsRevoked;
  string? AttestationsJson; string? RevocationReason;
  static ComplianceProfile Create(Guid investorId, string? walletAddress);
  void SetDid(string did); void SetAttestations(string json); void MarkAllowlisted(); void RemoveFromAllowlist();
  void Revoke(string reason);   // IsRevoked=true, IsAllowlisted=false ; raises AttestationsRevokedEvent
sealed class BlockchainOperation : AggregateRoot
  BlockchainOperationType Type; string Payload; string IdempotencyKey; BlockchainOperationStatus Status;
  int Attempts; string? TransactionRef; string? Error; DateTime? ConfirmedAtUtc;
  static BlockchainOperation Create(BlockchainOperationType type, string payload, string idempotencyKey);
  void MarkSubmitted(string txRef); void MarkConfirmed(); void MarkFailed(string error); void IncrementAttempt();
Events (`...Compliance.Events`):
  DidIssuedEvent(Guid InvestorId, string Did)
  AllowlistUpdatedEvent(Guid InvestorId, string WalletAddress, bool Added)
  AttestationsRevokedEvent(Guid InvestorId, string Reason)
```

### Outbox (`Atria.Domain.Outbox`)
```
sealed class OutboxMessage : Entity
  Guid EventId; string Type; string Payload; DateTime OccurredOnUtc; DateTime? ProcessedOnUtc; int Attempts; string? Error;
  static OutboxMessage Create(Guid eventId, string type, string payload, DateTime occurredOnUtc);
  void MarkProcessed(DateTime utc); void MarkFailed(string error);
```

---

## 2. APPLICATION (project Atria.Application)

### Specialized repositories (`namespace Atria.Application.Abstractions`, folder Abstractions/Persistence)
```
IUserRepository : IRepository<User> { Task<User?> GetByEmailAsync(string email, ct); Task<User?> GetByPhoneAsync(string phone, ct); }
IKycRepository : IRepository<KycProfile> { Task<KycProfile?> GetByUserIdAsync(Guid userId, ct); Task<KycProfile?> GetBySessionIdAsync(string sessionId, ct); }
IInvestmentRepository : IRepository<Investment> { Task<IReadOnlyList<Investment>> GetByInvestorAsync(Guid investorId, ct); Task<(decimal TotalInvested, int ActiveCount)> GetActiveTotalsAsync(Guid investorId, ct); }
IPropertyRepository : IRepository<Property> { Task<IReadOnlyList<Property>> GetAllAsync(ct); Task<IReadOnlyList<Property>> GetByBuildingAsync(Guid buildingId, ct); }
IBuildingRepository : IRepository<Building> { Task<IReadOnlyList<Building>> GetAllAsync(ct); Task<bool> HasUnitsAsync(Guid buildingId, ct); }
IDocumentRepository : IRepository<DocumentRecord> { Task<IReadOnlyList<DocumentRecord>> GetByOwnerAsync(Guid ownerId, ct); }
INotificationRepository : IRepository<Notification> { Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, ct); }
IComplianceRepository : IRepository<ComplianceProfile> { Task<ComplianceProfile?> GetByInvestorAsync(Guid investorId, ct); }
IAuditLogRepository { Task AddAsync(AuditLogEntry e, ct); Task<IReadOnlyList<AuditLogEntry>> QueryAsync(string? entityType, Guid? entityId, ct); }
```

### Use cases (Command/Query : IRequest<...>, plus one IRequestHandler each). DTO fields are
the implementer's choice (keep minimal, in `<Module>/Dtos`). Resource ownership checks
(ICurrentUserService) live IN the handler — an Investor may only touch their OWN rows.

- **Auth** (`Atria.Application.Auth`): RegisterCommand→Result<AuthTokensDto>; LoginCommand→Result<AuthTokensDto>;
  RefreshTokenCommand(string refreshToken)→Result<AuthTokensDto> (rotate + reuse detection);
  RequestPhoneOtpCommand(string phone, string? ip)→Result; VerifyPhoneOtpCommand(string phone, string code)→Result<AuthTokensDto>.
  AuthTokensDto(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken).
- **Kyc**: SubmitKycCommand(provider, walletAddress, fullName, documentNumber, nationality)→Result<KycStatusDto>;
  ReviewKycCommand(Guid kycId, bool approve, string? reason)→Result [Compliance];
  GetKycStatusQuery→Result<KycStatusDto> (current user);
  HandleKycCallbackCommand(string provider, WebhookPayload payload)→Result (webhook).
- **Properties**: CreatePropertyCommand(...)→Result<Guid> [Admin]; GetPropertiesQuery→Result<IReadOnlyList<PropertyDto>>;
  GetPropertyByIdQuery(Guid id)→Result<PropertyDto>. Creating/updating takes the unit fields
  (buildingId, unitType, unitNumber, floorNumber, roomCount, totalAreaSqM, rooms[]); tokens are
  always issued per property, i.e. per apartment/garage, never on the building.
- **Buildings**: CreateBuildingCommand(...)→Result<Guid> [Admin]; UpdateBuildingCommand(...)→Result [Admin];
  DeleteBuildingCommand(Guid id)→Result [Admin, 409 while units remain];
  AddBuildingImageCommand/RemoveBuildingImageCommand [Admin];
  GetBuildingsQuery→Result<IReadOnlyList<BuildingDto>>; GetBuildingByIdQuery(Guid id)→Result<BuildingDto>
  (both carry the building's units; draft units are staff-only).
- **Investments**: CreateInvestmentCommand(Guid propertyId, decimal amount)→Result<Guid> [Investor, KYC-gated]
  (the amount is floored to whole tokens and the application is priced off that count);
  QuoteInvestmentQuery(Guid propertyId, decimal amount)→Result<InvestmentQuoteDto> — what the sum buys,
  what it costs and the leftover, shown before confirming; reserves nothing;
  CreatePaymentSessionCommand(Guid investmentId, PaymentProviderType provider)→Result<PaymentSessionDto>;
  HandlePaymentCallbackCommand(string provider, WebhookPayload payload)→Result (webhook, idempotent);
  GetMyInvestmentsQuery→Result<IReadOnlyList<InvestmentDto>>; GetInvestmentByIdQuery(Guid id)→Result<InvestmentDto>;
  GetPortfolioQuery→Result<PortfolioDto>.
- **Documents**: UploadDocumentCommand(Stream content, string fileName, string contentType, DocumentType type)→Result<Guid>;
  GetMyDocumentsQuery→Result<IReadOnlyList<DocumentDto>>; GetDocumentByIdQuery(Guid id)→Result<DocumentDownloadDto> (owner/Admin/Compliance).
- **Notifications**: GetMyNotificationsQuery→Result<IReadOnlyList<NotificationDto>>; MarkNotificationReadCommand(Guid id)→Result.
- **Audit**: GetAuditLogQuery(string? entityType, Guid? entityId)→Result<IReadOnlyList<AuditLogDto>> [Admin/Compliance].

### Domain event handlers (`<Module>/EventHandlers`, implement IDomainEventHandler<TEvent>)
- Audit: `AuditAllDomainEventsHandler<TEvent>` — universal, logs EVERY event to IAuditLogRepository.
- Notifications: on KycApprovedEvent, KycRejectedEvent,
  PaymentCompletedEvent, InvestmentActivatedEvent → INotificationSender.SendAsync.
- Compliance: on KycApprovedEvent → create ComplianceProfile + ITesseraComplianceService.IssueDidAndAttestationsAsync (idempotent).
  on PaymentCompletedEvent (or InvestmentActivatedEvent) → VerifyPresentationAsync + AddToAllowlistAsync + enqueue token allocation (idempotent, exactly-once via IProcessedEventStore).
  on KycRejectedEvent / AttestationsRevokedEvent → RevokeAttestationsAsync + RemoveFromAllowlistAsync.
- **Idempotency**: every handler that moves money/tokens/allowlist checks `IProcessedEventStore.IsProcessedAsync(key)`
  where key = `$"{nameof(Handler)}:{domainEvent.EventId}"`, acts, then `MarkProcessedAsync(key)`.

### DI for Application
Handlers, validators (FluentValidation), and pipeline behaviors are registered by
**Infrastructure** (it scans `typeof(IUserRepository).Assembly`). Do NOT add a DI
package to Application. Validators derive `AbstractValidator<TCommand>` in `<Module>/Validators`.

---

## 3. INFRASTRUCTURE (project Atria.Infrastructure)

- **Persistence/AtriaDbContext** : DbContext. `DbSet`s for every aggregate + PaymentTransaction +
  OutboxMessage + ProcessedEvent + RefreshToken. Applies all `IEntityTypeConfiguration` from the assembly.
  `SaveChanges` override: set CreatedAtUtc/UpdatedAtUtc via ChangeTracker; collect AggregateRoot.DomainEvents,
  write each as an OutboxMessage (System.Text.Json payload + assembly-qualified Type) in the SAME transaction, then ClearEvents.
- **Concurrency**: `UseXminAsConcurrencyToken()` for KycProfile, Investment (Npgsql). (InMemory ignores it — fine for tests.)
- **PII encryption**: KycProfile.FullName + DocumentNumber use an EF value converter backed by IEncryptionService (AES-GCM). Provide an `EncryptedConverter`.
- **Persistence/Repositories**: `Repository<T>` (generic) + one class per specialized interface above.
- **Persistence/UnitOfWork** : IUnitOfWork over AtriaDbContext.SaveChangesAsync.
- **Persistence entities (infra-only, EF classes, NOT in Domain)**: `ProcessedEvent { string Key (PK); DateTime ProcessedAtUtc; }`,
  `RefreshToken { Guid Id; Guid UserId; string TokenHash; DateTime ExpiresAtUtc; bool IsRevoked; DateTime CreatedAtUtc; }`.
- **Messaging**: `Mediator : ISender` (resolves IRequestHandler<,> + runs IPipelineBehavior<,>), `ValidationBehavior`, `LoggingBehavior`.
- **Events**: `DomainEventDispatcher : IDomainEventDispatcher` (resolves IDomainEventHandler<T> via IServiceProvider, reflection-invoke).
- **Outbox/OutboxDispatcherBackgroundService** : BackgroundService polling unprocessed OutboxMessages, deserialize → dispatch → MarkProcessed; exponential backoff + Attempts cap.
- **Kyc/Providers**: `DiditKycProvider` (PRIMARY, KycProviderType.Didit, hosted session via HttpClient, HMAC webhook verify, IOptions<DiditOptions>),
  `ManualKycProvider` (KycProviderType.Manual). (SumSub optional second strategy.)
- **Payments/Providers**: `StripePaymentProvider` (Stripe.net), `BankTransferPaymentProvider`. IPaymentProviderStrategy. Webhook signature verify.
- **Notifications**: `NikitaProSmsAdapter : ISmsSender` (HttpClient + IOptions<NikitaProOptions>), `EmailNotificationAdapter : IEmailSender` (log/SMTP stub), `NotificationSender : INotificationSender` (persists Notification + picks channel).
- **Storage**: `S3DocumentStorageAdapter : IDocumentStorage` (AWSSDK.S3 + IOptions<S3Options>).
- **Identity**: `JwtTokenGenerator` (IOptions<JwtOptions>), `BcryptPasswordHasher`, `AesGcmEncryptionService` (IOptions<EncryptionOptions>), `SystemDateTimeProvider`, `OtpService` (IOptions<OtpOptions>, ISmsSender, hashes codes, rate-limit + lockout, constant-time compare), `RefreshTokenStore`, `ProcessedEventStore`.
- **Compliance**: `TesseraComplianceService : ITesseraComplianceService` (LOCAL implementation — see note),
  `ExternalBlockchainSigner : IBlockchainSigner` (calls a configured external signer URL; NO private keys held),
  `SolanaChainAnchor : IChainAnchor` (anchors roots; pilot stub), `BlockchainOperationQueue : IBlockchainOperationQueue` (persists BlockchainOperation),
  `BlockchainOperationWorker` : BackgroundService (sends via IBlockchainSigner, tracks status, reconciles, idempotent).
- **Options** (folder Configuration, each `[Required]`-annotated, validated `ValidateDataAnnotations().ValidateOnStart()`):
  JwtOptions(Issuer, Audience, SigningKey, AccessTokenMinutes, RefreshTokenDays),
  EncryptionOptions(Key /*base64 32 bytes*/), OtpOptions(Length, TtlMinutes, MaxAttempts, RequestsPerHour),
  DiditOptions(ApiKey, WebhookSecret, BaseUrl), SumSubOptions(...), StripeOptions(ApiKey, WebhookSecret),
  BankTransferOptions(WebhookSecret), NikitaProOptions(Login, Sender, ApiKey, BaseUrl), S3Options(BucketName, Region, ServiceUrl?),
  TesseraOptions(PolicyId, IssuerDid), BlockchainOptions(SignerUrl, ChainId, TokenContractAddress, AnchorNetwork).
- **DependencyInjection.cs**: `public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)` registers:
  DbContext (UseNpgsql + EnableRetryOnFailure), all repositories, UnitOfWork, ISender+behaviors, dispatcher,
  all strategies (registered as IEnumerable so handlers pick by ProviderType), adapters, identity services,
  compliance services, options (bind+validate), hosted services (outbox + blockchain worker), application handlers + validators (assembly scan).

> **Tessera note (ambiguity resolved per prompt §D / Definition of Done):** the
> `Tessera.Sdk` / `Tessera.Signing` / `Tessera.EntityFrameworkCore` NuGet packages are
> not publicly resolvable, so we implement `TesseraComplianceService` as a LOCAL
> in-house service that follows the Tessera principles: identity data stays off-chain
> (persisted on `ComplianceProfile`, PII encrypted), only attestation Merkle roots are
> anchored on chain (via `IChainAnchor`), and the permissioned BEP-20 allowlist is
> updated via `IBlockchainOperationQueue` → `IBlockchainSigner`. Leave a `// NOTE:`
> comment where a real Tessera SDK call would go. This keeps the build green.

---

## 4. API (project Atria.Api)

- **Program.cs**: Serilog (console, correlation-id enrichment, NO PII/secret/OTP in logs);
  bind+validate all Options; `AddInfrastructure(builder.Configuration)`; controllers + FluentValidation;
  JWT bearer auth (JwtOptions) + role policies Admin/Investor/Compliance; Swagger with JWT bearer scheme;
  API versioning (`/api/v1/...` route prefix); health checks `/health/live` + `/health/ready` (ready checks Npgsql);
  rate limiting (built-in `AddRateLimiter`) on auth/otp endpoints; ExceptionHandlingMiddleware (sanitized ProblemDetails);
  CorrelationIdMiddleware; SecurityHeadersMiddleware (HSTS, X-Content-Type-Options, restrictive CSP for Swagger); HTTPS redirection.
- **CurrentUserService : ICurrentUserService** lives in Api (reads IHttpContextAccessor); registered in Program.
- **Controllers** (`Atria.Api.Controllers`, thin, `[ApiController]`, route `api/v{version:apiVersion}/<name>`, inject ISender):
  AuthController, KycController, PropertiesController, InvestmentsController,
  DocumentsController, NotificationsController, AdminAuditController, WebhooksController
  (`POST api/v1/webhooks/kyc/{provider}` + `POST api/v1/webhooks/payments/{provider}` — build WebhookPayload from the raw request,
  read raw body + headers + signature, [AllowAnonymous], verified inside the strategy).
- **Endpoints** must match `atria-backend-architecture-en.md §4` plus the webhook + phone-OTP routes from the prompt.
- **appsettings.json**: non-secret defaults + connection string placeholder. **appsettings.Example.json**: all keys with placeholders. NO real secrets committed.

---

## 5. TESTS

- **Atria.Domain.Tests**: xUnit + FluentAssertions. State transition tests for KycProfile, Investment —
  cover happy path + invalid transitions throw + repeated/stale transitions are rejected (or idempotent). Test factories' invariants.
- **Atria.Application.Tests**: xUnit + FluentAssertions + NSubstitute + EF InMemory. At least one idempotency test:
  a money/token effect handler invoked twice with the same event id produces the effect ONCE (via IProcessedEventStore).
- **Atria.Api.IntegrationTests**: WebApplicationFactory (EF InMemory), smoke test `/health/live` returns 200; register→login happy path if feasible.

---

## 6. File ownership (avoid collisions during parallel work)
Each agent writes ONLY files in its assigned folders. Shared wiring files
(`AtriaDbContext.cs`, `DependencyInjection.cs`, `Program.cs`) are owned by a single
agent each (persistence agent / api agent). Never two agents in the same file.
