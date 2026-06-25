using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures PostgreSQL optimistic concurrency using the built-in <c>xmin</c>
/// system column as a row-version token. (The older Npgsql
/// <c>UseXminAsConcurrencyToken</c> helper is not present in this provider
/// version, so we map the shadow property explicitly — same on-disk behaviour,
/// no extra migration column.)
/// </summary>
internal static class XminConcurrencyExtensions
{
    public static void XminConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
