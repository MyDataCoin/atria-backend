using System.Reflection;
using Amazon;
using Amazon.S3;
using Atria.Application.Abstractions;
using Atria.Application.Audit.EventHandlers;
using Atria.Application.Investments;
using Atria.Domain.Common;
using Atria.Infrastructure.Audit;
using Atria.Infrastructure.Compliance;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Deals;
using Atria.Infrastructure.Events;
using Atria.Infrastructure.Identity;
using Atria.Infrastructure.Kyc.Providers;
using Atria.Infrastructure.Messaging;
using Atria.Infrastructure.Notifications;
using Atria.Infrastructure.Outbox;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Repositories;
using Atria.Infrastructure.Persistence.Seeding;
using Atria.Infrastructure.Persistence.Stores;
using Atria.Infrastructure.Storage;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure;

/// <summary>
/// Single composition root for the Infrastructure layer. Wires persistence, the
/// in-process mediator + pipeline, domain-event dispatch, identity/security,
/// provider strategies, adapters, compliance/web3, hosted workers, and scans the
/// Application assembly for request/event handlers and validators.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddMessaging(services);
        AddIdentity(services);
        AddStrategies(services);
        AddAdapters(services);
        AddCompliance(services);
        AddHostedServices(services);
        AddApplication(services);

        return services;
    }

    // --- Options: bind + DataAnnotations validation + validate on start ---
    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        BindValidated<JwtOptions>(services, configuration, JwtOptions.SectionName);
        BindValidated<EncryptionOptions>(services, configuration, EncryptionOptions.SectionName);
        BindValidated<OtpOptions>(services, configuration, OtpOptions.SectionName);

        // Admin/Realtor/SuperAdmin have no configuration: they are ordinary rows in the users table
        // (username + password hash) and log in purely against the database.

        // Referral link base URL (used to build shareable deal links). Optional; a relative link is
        // returned when unset.
        services.Configure<ReferralOptions>(configuration.GetSection(ReferralOptions.SectionName));

        // Reservation window + background sweep pacing for offering applications. Optional; the
        // built-in defaults (3-day window, 15-minute sweep) apply when the section is absent.
        BindValidated<InvestmentReservationOptions>(services, configuration, InvestmentReservationOptions.SectionName);

        // Public media storage location (property photos/documents).
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        BindValidated<DiditOptions>(services, configuration, DiditOptions.SectionName);
        BindValidated<NikitaProOptions>(services, configuration, NikitaProOptions.SectionName);
        BindValidated<S3Options>(services, configuration, S3Options.SectionName);
        BindValidated<TesseraOptions>(services, configuration, TesseraOptions.SectionName);
        BindValidated<BlockchainOptions>(services, configuration, BlockchainOptions.SectionName);
    }

    private static void BindValidated<T>(
        IServiceCollection services, IConfiguration configuration, string section)
        where T : class
        => services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

    // --- Persistence: DbContext, repositories, UnitOfWork, stores ---
    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // IEncryptionService is a ctor dependency of AtriaDbContext (PII converters). Singleton, stateless.
        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();

        services.AddDbContext<AtriaDbContext>(o =>
            o.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.EnableRetryOnFailure()));

        // Open-generic generic repository for IRepository<T>.
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Specialized repositories.
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IKycRepository, KycRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();
        services.AddScoped<IInvestmentRepository, InvestmentRepository>();
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IDealRepository, DealRepository>();
        services.AddScoped<IRealtorProfileRepository, RealtorProfileRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IComplianceRepository, ComplianceRepository>();
        services.AddScoped<IHolderPositionRepository, HolderPositionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ISupportTicketRepository, SupportTicketRepository>();
        services.AddScoped<IAppealRepository, AppealRepository>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Infra-only stores.
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IOtpCodeStore, OtpCodeStore>();
    }

    // --- Messaging + domain-event dispatch ---
    private static void AddMessaging(IServiceCollection services)
    {
        services.AddScoped<ISender, Mediator>();

        // Open-generic pipeline behaviors, registered outer-to-inner: validation, then concurrency
        // (converts a DbUpdateConcurrencyException to a 409 Result), then logging closest to the
        // handler so it still records the raw exception before ConcurrencyBehavior converts it.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ConcurrencyBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
    }

    // --- Identity / security ---
    private static void AddIdentity(IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        // IEncryptionService is registered in AddPersistence (also required there by AtriaDbContext).
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IReferralLinkBuilder, ReferralLinkBuilder>();
        // Audit entries are written from inside commands so they share the action's transaction.
        services.AddScoped<IAuditWriter, AuditWriter>();
    }

    // --- Strategies (registered per concrete type so handlers get IEnumerable<...>) ---
    private static void AddStrategies(IServiceCollection services)
    {
        // KYC providers. Didit uses a typed HttpClient (BaseAddress from DiditOptions.BaseUrl so
        // relative endpoints like /v2/session/ resolve). Manual is stateless.
        services.AddHttpClient<IKycProviderStrategy, DiditKycProvider>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<DiditOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);
        });
        services.AddScoped<IKycProviderStrategy, ManualKycProvider>();

        // No payment providers: the platform does not take money. Primary placement activates by
        // operator approval of an application; settlement happens off-platform.
    }

    // --- Notification + storage adapters ---
    private static void AddAdapters(IServiceCollection services)
    {
        // Nikita Pro SMS adapter requires an HttpClient -> typed client (BaseAddress from options).
        services.AddHttpClient<ISmsSender, NikitaProSmsAdapter>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<NikitaProOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);
        });
        services.AddScoped<IEmailSender, EmailNotificationAdapter>();
        services.AddScoped<INotificationSender, NotificationSender>();

        // S3 client from S3Options (region + optional custom endpoint for S3-compatible stores).
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<S3Options>>().Value;
            var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region) };
            if (!string.IsNullOrWhiteSpace(opts.ServiceUrl))
            {
                config.ServiceURL = opts.ServiceUrl;
                config.ForcePathStyle = true;
            }

            // Credentials resolved by the AWS SDK default chain (env / profile / IAM role).
            return new AmazonS3Client(config);
        });
        services.AddScoped<IDocumentStorage, S3DocumentStorageAdapter>();

        // Public media (property photos/documents) copied to a remote host over SCP, served by nginx.
        services.AddScoped<IMediaStorage, ScpMediaStorage>();
    }

    // --- Compliance / web3 ---
    private static void AddCompliance(IServiceCollection services)
    {
        services.AddScoped<IChainAnchor, SolanaChainAnchor>();
        // External signer calls a configured signer URL via HttpClient (BaseAddress from options).
        services.AddHttpClient<IBlockchainSigner, ExternalBlockchainSigner>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BlockchainOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.SignerUrl))
                client.BaseAddress = new Uri(opts.SignerUrl);
        });
        services.AddScoped<IBlockchainOperationQueue, BlockchainOperationQueue>();
        services.AddScoped<ITesseraComplianceService, TesseraComplianceService>();
    }

    // --- Background workers ---
    private static void AddHostedServices(IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcherBackgroundService>();
        services.AddHostedService<BlockchainOperationWorker>();
        services.AddHostedService<DealExpiryBackgroundService>();
        services.AddHostedService<Investments.ReservationExpiryBackgroundService>();
    }

    // --- Application assembly scan: request handlers, event handlers, validators ---
    private static void AddApplication(IServiceCollection services)
    {
        var assembly = typeof(IUserRepository).Assembly;

        var types = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .ToArray();

        // Closed IRequestHandler<,> implementations.
        RegisterClosedImplementations(services, types, typeof(IRequestHandler<,>));

        // Closed IDomainEventHandler<> implementations (excludes the open-generic universal handler).
        RegisterClosedImplementations(services, types, typeof(IDomainEventHandler<>));

        // Open-generic universal audit handler: IDomainEventHandler<> -> AuditAllDomainEventsHandler<>.
        services.AddScoped(typeof(IDomainEventHandler<>), typeof(AuditAllDomainEventsHandler<>));

        // FluentValidation validators (IValidator<T>). Registered by reflection so the
        // Infrastructure project needs no FluentValidation.DependencyInjectionExtensions reference.
        RegisterClosedImplementations(services, types, typeof(IValidator<>));
    }

    /// <summary>
    /// Registers every non-generic type that implements one or more closed versions
    /// of <paramref name="openInterface"/>, mapping each closed interface to the
    /// concrete type with a scoped lifetime.
    /// </summary>
    private static void RegisterClosedImplementations(
        IServiceCollection services, IEnumerable<Type> types, Type openInterface)
    {
        foreach (var type in types)
        {
            var closedInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

            foreach (var closed in closedInterfaces)
            {
                services.AddScoped(closed, type);
            }
        }
    }
}
