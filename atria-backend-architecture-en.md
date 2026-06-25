# Atria Backend Architecture

> Real estate tokenization investment platform (RWA).
> .NET 9 / ASP.NET Core Web API / Clean Architecture / Modular Monolith / PostgreSQL / JWT

## 0. Core idea

One backend, split into **modules along domain boundaries** inside Clean Architecture
(not microservices, but ready to be split out as it grows). Each module is a vertical
slice (Identity, KYC, Applications, Investments, Documents, Notifications, Audit).
Communication between modules happens **only through Domain Events**, never through
direct calls into each other's services. This gives isolation without the network
overhead of microservices.

---

## 1. Project structure

```
Atria.sln
src/
  Atria.Domain/                      # Entities, Value Objects, Domain Events, State interfaces
    Common/
      Entity.cs
      AggregateRoot.cs
      IDomainEvent.cs
      ValueObject.cs
    Users/
      User.cs
      Role.cs
    Kyc/
      KycProfile.cs
      KycStatus.cs                   # State pattern marker
      States/
        IKycState.cs
        PendingKycState.cs
        UnderReviewKycState.cs
        ApprovedKycState.cs
        RejectedKycState.cs
      Events/
        KycApprovedEvent.cs
        KycRejectedEvent.cs
    Applications/                    # investor applications to invest in a property
      InvestorApplication.cs
      ApplicationStatus.cs
      States/
        IApplicationState.cs
        DraftState.cs
        SubmittedState.cs
        UnderReviewState.cs
        ApprovedState.cs
        RejectedState.cs
      Events/
        ApplicationApprovedEvent.cs
        ApplicationSubmittedEvent.cs
        ApplicationRejectedEvent.cs
    Investments/
      Property.cs                    # real estate property / token pool
      Investment.cs                  # investor token purchase
      PaymentTransaction.cs
      Events/
        InvestmentCreatedEvent.cs
        PaymentCompletedEvent.cs
    Documents/
      DocumentRecord.cs
      DocumentType.cs
    Notifications/
      Notification.cs
      NotificationTemplate.cs
    Audit/
      AuditLogEntry.cs
    Factories/
      InvestorApplicationFactory.cs
      InvestmentFactory.cs

  Atria.Application/                 # Use cases, interfaces (CQRS-light), DTOs
    Abstractions/
      IRepository.cs
      IUnitOfWork.cs
      IDomainEventDispatcher.cs
      IKycProviderStrategy.cs
      IPaymentProviderStrategy.cs
      ICurrentUserService.cs
      IDocumentStorage.cs
      INotificationSender.cs
    Kyc/
      Commands/SubmitKycCommand.cs / Handler.cs
      Commands/ReviewKycCommand.cs / Handler.cs
      Queries/GetKycStatusQuery.cs / Handler.cs
    Applications/
      Commands/CreateApplicationCommand.cs / Handler.cs
      Commands/ApproveApplicationCommand.cs / Handler.cs
      Queries/...
    Investments/
      Commands/CreateInvestmentCommand.cs / Handler.cs
      Commands/ConfirmPaymentCommand.cs / Handler.cs
      Queries/GetPortfolioQuery.cs / Handler.cs
    Documents/
      Commands/UploadDocumentCommand.cs / Handler.cs
    Notifications/
      EventHandlers/
        SendKycApprovedNotificationHandler.cs
        SendApplicationApprovedNotificationHandler.cs
    Audit/
      EventHandlers/
        AuditAllDomainEventsHandler.cs

  Atria.Infrastructure/               # EF Core, repositories, external adapters
    Persistence/
      AtriaDbContext.cs
      Configurations/                 # IEntityTypeConfiguration<T> per entity
      Repositories/
        Repository.cs                # generic
        ApplicationRepository.cs
        InvestmentRepository.cs
        KycRepository.cs
      UnitOfWork.cs
      Migrations/
    Kyc/
      Providers/
        SumSubKycProvider.cs          # Strategy impl
        ManualKycProvider.cs
    Payments/
      Providers/
        StripePaymentProvider.cs      # Strategy impl
        BankTransferPaymentProvider.cs
    Storage/
      S3DocumentStorageAdapter.cs     # Adapter
    Notifications/
      EmailNotificationAdapter.cs     # Adapter
      SmsNotificationAdapter.cs
    Identity/
      JwtTokenGenerator.cs
    DependencyInjection.cs

  Atria.Api/                          # Controllers, middlewares, Swagger
    Controllers/
      AuthController.cs
      KycController.cs
      ApplicationsController.cs
      PropertiesController.cs
      InvestmentsController.cs
      DocumentsController.cs
      NotificationsController.cs
      AdminAuditController.cs
    Middleware/
      ExceptionHandlingMiddleware.cs
      AuditRequestMiddleware.cs
    Program.cs
    appsettings.json

tests/
  Atria.Domain.Tests/
  Atria.Application.Tests/
  Atria.Api.IntegrationTests/
```

**Layering principle:** Domain knows nothing about EF Core or HTTP. Application knows
only abstractions. Infrastructure implements adapters, strategies and repositories.
Api is a thin layer: only routing, authorization and input validation.

---

## 2. Core entities

| Entity | Purpose |
|---|---|
| `User` | account, role (Admin / Investor / Compliance) |
| `KycProfile` | investor KYC data plus current state (State) |
| `InvestorApplication` | application to invest in a specific `Property` |
| `Property` | real estate property that issues tokens |
| `Investment` | the fact of a token purchase (after application approval and payment) |
| `PaymentTransaction` | a payment transaction (through a Strategy provider) |
| `DocumentRecord` | metadata of an uploaded document (passport, contract, dividend statement) |
| `Notification` | an outgoing notification (email/SMS/push) |
| `AuditLogEntry` | an immutable record of a system event |

---

## 3. Relationships between entities

```
User (1) -- (1) KycProfile
User (1) -- (0..N) InvestorApplication
User (1) -- (0..N) Investment
User (1) -- (0..N) DocumentRecord

Property (1) -- (0..N) InvestorApplication
Property (1) -- (0..N) Investment

InvestorApplication (1) -- (0..1) Investment      // application -> on approval creates an investment
Investment (1) -- (1..N) PaymentTransaction

InvestorApplication / KycProfile / Investment -- (0..N) AuditLogEntry   // polymorphic, via EntityType + EntityId
User -- (0..N) Notification
```

### Module-to-module links (through Domain Events, not direct calls)

```
KycApprovedEvent         -> unlocks the ability to create an InvestorApplication
ApplicationApprovedEvent -> creates an Investment (via InvestmentFactory) + Notification + AuditLog
PaymentCompletedEvent    -> moves Investment to Active + Notification
(any domain event)       -> AuditLogEntry (universal handler)
```

---

## 4. Core API endpoints

```
Auth
  POST   /api/auth/register
  POST   /api/auth/login
  POST   /api/auth/refresh

KYC
  POST   /api/kyc/submit                  [Investor]
  GET    /api/kyc/me                      [Investor]
  POST   /api/kyc/{id}/review              [Compliance]   { decision }

Applications (investor applications)
  POST   /api/applications                 [Investor]   { propertyId, amount }
  GET    /api/applications/me              [Investor]
  GET    /api/applications/{id}            [Investor/Compliance/Admin]
  POST   /api/applications/{id}/submit      [Investor]
  POST   /api/applications/{id}/approve     [Compliance]
  POST   /api/applications/{id}/reject      [Compliance]

Properties
  GET    /api/properties
  GET    /api/properties/{id}
  POST   /api/properties                   [Admin]

Investments
  POST   /api/investments/{applicationId}/payments   [Investor]  { provider }
  GET    /api/investments/me                          [Investor]
  GET    /api/investments/{id}                        [Investor/Admin]

Documents
  POST   /api/documents                    [Investor]   (multipart upload)
  GET    /api/documents/{id}               [owner/Admin/Compliance]
  GET    /api/documents/me

Notifications
  GET    /api/notifications/me
  POST   /api/notifications/{id}/read

Audit (audit / compliance only)
  GET    /api/audit?entityType=&entityId=   [Admin/Compliance]
```

---

## 5. Where patterns are used

| Pattern | Usage |
|---|---|
| **State** | `KycProfile` (Pending -> UnderReview -> Approved/Rejected) and `InvestorApplication` (Draft -> Submitted -> UnderReview -> Approved/Rejected). Transitions and allowed actions are encapsulated in `IKycState` / `IApplicationState`, not in if/else |
| **Strategy** | `IKycProviderStrategy` (SumSub / Manual review) and `IPaymentProviderStrategy` (Stripe / bank transfer). The concrete implementation is selected through DI by provider type |
| **Repository** | `IRepository<T>` plus specialized ones (`IApplicationRepository`, `IInvestmentRepository`) over EF Core. The Application layer does not know about DbContext |
| **Domain Events** | `KycApprovedEvent`, `ApplicationApprovedEvent`, `PaymentCompletedEvent` are the only communication channel between modules (KYC does not know about Notifications, Notifications does not know about Investments) |
| **Adapter** | `S3DocumentStorageAdapter` (document storage), `EmailNotificationAdapter` / `SmsNotificationAdapter` (external notification services); payment/KYC providers also adapt external SDKs to internal interfaces |
| **Factory Method** | `InvestorApplicationFactory.Create(...)`, `InvestmentFactory.CreateFromApprovedApplication(...)` guarantee a valid initial aggregate state and encapsulate creation invariants |

---

## 6. Code examples

### 6.1 Domain. State pattern (Application)

```csharp
// Atria.Domain/Applications/States/IApplicationState.cs
namespace Atria.Domain.Applications.States;

public interface IApplicationState
{
    ApplicationStatus Status { get; }
    IApplicationState Submit(InvestorApplication application);
    IApplicationState Approve(InvestorApplication application);
    IApplicationState Reject(InvestorApplication application, string reason);
}
```

```csharp
// Atria.Domain/Applications/States/DraftState.cs
namespace Atria.Domain.Applications.States;

public sealed class DraftState : IApplicationState
{
    public ApplicationStatus Status => ApplicationStatus.Draft;

    public IApplicationState Submit(InvestorApplication application)
    {
        application.RaiseEvent(new ApplicationSubmittedEvent(application.Id));
        return new SubmittedState();
    }

    public IApplicationState Approve(InvestorApplication application) =>
        throw new InvalidOperationException("Cannot approve an application that was never submitted.");

    public IApplicationState Reject(InvestorApplication application, string reason) =>
        throw new InvalidOperationException("Cannot reject a draft application.");
}
```

```csharp
// Atria.Domain/Applications/States/UnderReviewState.cs
namespace Atria.Domain.Applications.States;

public sealed class UnderReviewState : IApplicationState
{
    public ApplicationStatus Status => ApplicationStatus.UnderReview;

    public IApplicationState Submit(InvestorApplication application) =>
        throw new InvalidOperationException("Already submitted.");

    public IApplicationState Approve(InvestorApplication application)
    {
        application.RaiseEvent(new ApplicationApprovedEvent(
            application.Id, application.PropertyId, application.InvestorId, application.Amount));
        return new ApprovedState();
    }

    public IApplicationState Reject(InvestorApplication application, string reason)
    {
        application.RaiseEvent(new ApplicationRejectedEvent(application.Id, reason));
        return new RejectedState(reason);
    }
}
```

No `switch` over an enum: the transition and its side effect (the event) live inside
the concrete state.

### 6.2 Domain. Aggregate Root that uses State and stores events

```csharp
// Atria.Domain/Common/AggregateRoot.cs
namespace Atria.Domain.Common;

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    public void ClearEvents() => _domainEvents.Clear();
}
```

```csharp
// Atria.Domain/Applications/InvestorApplication.cs
using Atria.Domain.Applications.States;
using Atria.Domain.Common;

namespace Atria.Domain.Applications;

public sealed class InvestorApplication : AggregateRoot
{
    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }
    public decimal Amount { get; private set; }
    public ApplicationStatus Status => _state.Status;

    private IApplicationState _state;

    // private constructor: creation only through the Factory
    private InvestorApplication(Guid investorId, Guid propertyId, decimal amount)
    {
        Id = Guid.NewGuid();
        InvestorId = investorId;
        PropertyId = propertyId;
        Amount = amount;
        _state = new DraftState();
    }

    internal static InvestorApplication CreateDraft(Guid investorId, Guid propertyId, decimal amount)
        => new(investorId, propertyId, amount);

    public void Submit() => _state = _state.Submit(this);
    public void MoveToReview() => _state = new UnderReviewState();
    public void Approve() => _state = _state.Approve(this);
    public void Reject(string reason) => _state = _state.Reject(this, reason);

    // called by state objects through internal access (same assembly)
    internal void RaiseEvent(IDomainEvent @event) => base.RaiseEvent(@event);
}
```

### 6.3 Factory Method

```csharp
// Atria.Domain/Factories/InvestorApplicationFactory.cs
namespace Atria.Domain.Factories;

public static class InvestorApplicationFactory
{
    public static InvestorApplication Create(
        Guid investorId, Guid propertyId, decimal amount, KycStatus investorKycStatus)
    {
        if (investorKycStatus != KycStatus.Approved)
            throw new DomainException("KYC must be approved before creating an investment application.");

        if (amount <= 0)
            throw new DomainException("Investment amount must be positive.");

        return InvestorApplication.CreateDraft(investorId, propertyId, amount);
    }
}
```

### 6.4 Strategy. Payment providers

```csharp
// Atria.Application/Abstractions/IPaymentProviderStrategy.cs
namespace Atria.Application.Abstractions;

public interface IPaymentProviderStrategy
{
    PaymentProviderType ProviderType { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}
```

```csharp
// Atria.Infrastructure/Payments/Providers/StripePaymentProvider.cs
namespace Atria.Infrastructure.Payments.Providers;

public sealed class StripePaymentProvider : IPaymentProviderStrategy
{
    private readonly StripeClient _client;
    public PaymentProviderType ProviderType => PaymentProviderType.Stripe;

    public StripePaymentProvider(StripeClient client) => _client = client;

    public async Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
    {
        var charge = await _client.ChargeAsync(request.Amount, request.Currency, request.SourceToken, ct);
        return charge.Succeeded
            ? PaymentResult.Success(charge.TransactionId)
            : PaymentResult.Failure(charge.FailureReason);
    }
}
```

```csharp
// Atria.Application/Investments/Commands/ConfirmPaymentCommandHandler.cs
namespace Atria.Application.Investments.Commands;

public sealed class ConfirmPaymentCommandHandler
{
    private readonly IEnumerable<IPaymentProviderStrategy> _providers;
    private readonly IInvestmentRepository _investments;
    private readonly IUnitOfWork _uow;

    public ConfirmPaymentCommandHandler(
        IEnumerable<IPaymentProviderStrategy> providers,
        IInvestmentRepository investments,
        IUnitOfWork uow)
    {
        _providers = providers;
        _investments = investments;
        _uow = uow;
    }

    public async Task<Result> Handle(ConfirmPaymentCommand cmd, CancellationToken ct)
    {
        var strategy = _providers.Single(p => p.ProviderType == cmd.ProviderType);
        var result = await strategy.ChargeAsync(cmd.ToPaymentRequest(), ct);

        var investment = await _investments.GetByIdAsync(cmd.InvestmentId, ct);
        investment.RegisterPaymentResult(result); // Investment decides how to react (its State)

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

Strategy selection is done through `Single(p => p.ProviderType == ...)`, not
`if (provider == "stripe") ... else if (...)`.

### 6.5 Repository

```csharp
// Atria.Application/Abstractions/IRepository.cs
namespace Atria.Application.Abstractions;

public interface IRepository<TEntity> where TEntity : AggregateRoot
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(TEntity entity, CancellationToken ct);
    void Update(TEntity entity);
}
```

```csharp
// Atria.Infrastructure/Persistence/Repositories/Repository.cs
namespace Atria.Infrastructure.Persistence.Repositories;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : AggregateRoot
{
    protected readonly AtriaDbContext Context;
    public Repository(AtriaDbContext context) => Context = context;

    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await Context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct) =>
        await Context.Set<TEntity>().AddAsync(entity, ct);

    public void Update(TEntity entity) => Context.Set<TEntity>().Update(entity);
}
```

### 6.6 Domain Events. Dispatcher and handler (module-to-module link)

```csharp
// Atria.Application/Abstractions/IDomainEventDispatcher.cs
namespace Atria.Application.Abstractions;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct);
}
```

```csharp
// Atria.Application/Notifications/EventHandlers/SendApplicationApprovedNotificationHandler.cs
namespace Atria.Application.Notifications.EventHandlers;

public sealed class SendApplicationApprovedNotificationHandler
    : IDomainEventHandler<ApplicationApprovedEvent>
{
    private readonly INotificationSender _sender; // Adapter under the hood

    public SendApplicationApprovedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(ApplicationApprovedEvent e, CancellationToken ct) =>
        _sender.SendAsync(e.InvestorId, NotificationTemplate.ApplicationApproved, ct);
}
```

```csharp
// Atria.Application/Audit/EventHandlers/AuditAllDomainEventsHandler.cs
// Universal handler: every domain event is logged automatically
namespace Atria.Application.Audit.EventHandlers;

public sealed class AuditAllDomainEventsHandler<TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    private readonly IAuditLogRepository _auditRepo;

    public AuditAllDomainEventsHandler(IAuditLogRepository auditRepo) => _auditRepo = auditRepo;

    public Task HandleAsync(TEvent domainEvent, CancellationToken ct) =>
        _auditRepo.AddAsync(AuditLogEntry.FromDomainEvent(domainEvent), ct);
}
```

The dispatcher is invoked in one place, `UnitOfWork.SaveChangesAsync`, after the
transaction commit:

```csharp
// Atria.Infrastructure/Persistence/UnitOfWork.cs
public async Task<int> SaveChangesAsync(CancellationToken ct)
{
    var aggregatesWithEvents = Context.ChangeTracker
        .Entries<AggregateRoot>()
        .Where(e => e.Entity.DomainEvents.Any())
        .Select(e => e.Entity)
        .ToList();

    var result = await Context.SaveChangesAsync(ct);

    var events = aggregatesWithEvents.SelectMany(a => a.DomainEvents).ToList();
    aggregatesWithEvents.ForEach(a => a.ClearEvents());

    await _dispatcher.DispatchAsync(events, ct); // Notifications and Audit react here, independently

    return result;
}
```

### 6.7 Adapter. Document storage

```csharp
// Atria.Application/Abstractions/IDocumentStorage.cs
namespace Atria.Application.Abstractions;

public interface IDocumentStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct);
    Task<Stream> GetAsync(string storageKey, CancellationToken ct);
}
```

```csharp
// Atria.Infrastructure/Storage/S3DocumentStorageAdapter.cs
namespace Atria.Infrastructure.Storage;

public sealed class S3DocumentStorageAdapter : IDocumentStorage
{
    private readonly IAmazonS3 _s3Client; // external SDK
    private readonly string _bucket;

    public S3DocumentStorageAdapter(IAmazonS3 s3Client, IOptions<S3Settings> options)
    {
        _s3Client = s3Client;
        _bucket = options.Value.BucketName;
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        var key = $"{Guid.NewGuid()}-{fileName}";
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content
        }, ct);
        return key; // adapts the S3 SDK to the internal contract
    }

    public Task<Stream> GetAsync(string storageKey, CancellationToken ct) =>
        _s3Client.GetObjectStreamAsync(_bucket, storageKey, ct);
}
```

### 6.8 Controller (thin, no business logic)

```csharp
[ApiController]
[Route("api/applications")]
[Authorize]
public sealed class ApplicationsController : ControllerBase
{
    private readonly ISender _mediator; // MediatR or a simple custom dispatcher

    public ApplicationsController(ISender mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Create(CreateApplicationRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateApplicationCommand(
            request.PropertyId, request.Amount), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Compliance")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ApproveApplicationCommand(id), ct);
        return NoContent();
    }
}
```

---

## 7. What we deliberately avoid

- **God Service**: every use case is its own Command/Query handler, not one
  `ApplicationService` with dozens of methods.
- **if/else over statuses**: all status transitions are encapsulated in State objects.
- **Microservices**: one deployment, one database, modules are logically isolated
  through namespaces plus Domain Events, but physically it is a single process.
- **Unnecessary abstraction**: no separate "Domain Services" layer beyond what is
  needed, no CQRS infrastructure heavier than a team of 2 to 5 developers needs
  (MediatR or a thin custom dispatcher, the team's choice).

## 8. Path to growth (without changing the architecture)

- If the Investments/Payments module starts to dominate the load, it can be split
  into a separate service later, **because it is already isolated through Domain
  Events and has no direct dependencies** on the other modules.
- Strategy providers (KYC, Payments) let you add new external partners without
  changing use case code, only a new implementation of the interface plus DI
  registration.
- AuditAllDomainEventsHandler automatically covers any new events with audit
  without edits in every module.
