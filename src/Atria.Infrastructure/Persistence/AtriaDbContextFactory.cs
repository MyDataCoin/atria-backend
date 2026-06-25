using Atria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atria.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations` can build the model without the full
/// application configuration or any real secrets. Uses a no-op encryption service —
/// the PII value converter only needs an instance to build the relational model, the
/// design tools never read/write data.
/// </summary>
public sealed class AtriaDbContextFactory : IDesignTimeDbContextFactory<AtriaDbContext>
{
    public AtriaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AtriaDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=atria;Username=atria;Password=atria")
            .Options;

        return new AtriaDbContext(options, new DesignTimeEncryptionService());
    }

    private sealed class DesignTimeEncryptionService : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
