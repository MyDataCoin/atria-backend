using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Compliance;
using Atria.Domain.Documents;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;
using Atria.Domain.Notifications;
using Atria.Domain.Outbox;
using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for Atria. Owns timestamp stamping and the
/// transactional outbox: domain events raised by aggregates are serialized into
/// <see cref="OutboxMessage"/> rows in the SAME transaction as the aggregate change.
/// </summary>
public sealed class AtriaDbContext : DbContext
{
    private readonly IEncryptionService _encryption;

    public AtriaDbContext(DbContextOptions<AtriaDbContext> options, IEncryptionService encryption)
        : base(options)
        => _encryption = encryption;

    // Domain aggregates + child entities.
    public DbSet<User> Users => Set<User>();
    public DbSet<KycProfile> KycProfiles => Set<KycProfile>();
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<ComplianceProfile> ComplianceProfiles => Set<ComplianceProfile>();
    public DbSet<BlockchainOperation> BlockchainOperations => Set<BlockchainOperation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Infra-only EF entities.
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Pick up every IEntityTypeConfiguration in this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtriaDbContext).Assembly);

        // Apply PII at-rest encryption to KYC sensitive columns via the injected service.
        var encryptedConverter = new EncryptedConverter(_encryption);
        modelBuilder.Entity<KycProfile>()
            .Property(k => k.FullName)
            .HasConversion(encryptedConverter!);
        modelBuilder.Entity<KycProfile>()
            .Property(k => k.DocumentNumber)
            .HasConversion(encryptedConverter!);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        WriteOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }

    // Cover the synchronous path too (base SaveChanges() funnels through this overload),
    // so a stray sync SaveChanges() can never bypass timestamps or the transactional outbox.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        WriteOutboxMessages();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // (1) CreatedAtUtc on insert, UpdatedAtUtc on update. Infra owns the clock here.
    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(Entity.CreatedAtUtc)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(Entity.UpdatedAtUtc)).CurrentValue = now;
                    break;
            }
        }
    }

    // (2) Collect domain events from tracked aggregates, persist them to the outbox in
    // the same transaction, then clear them so they are not re-emitted.
    private void WriteOutboxMessages()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var @event in aggregate.DomainEvents)
            {
                var type = @event.GetType();
                var message = OutboxMessage.Create(
                    @event.EventId,
                    type.AssemblyQualifiedName!,                 // dispatcher resolves via Type.GetType
                    JsonSerializer.Serialize(@event, type),
                    @event.OccurredOnUtc);
                OutboxMessages.Add(message);
            }

            aggregate.ClearEvents();
        }
    }
}
