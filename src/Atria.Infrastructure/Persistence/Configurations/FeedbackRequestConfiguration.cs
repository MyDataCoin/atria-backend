using Atria.Domain.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class FeedbackRequestConfiguration : IEntityTypeConfiguration<FeedbackRequest>
{
    public void Configure(EntityTypeBuilder<FeedbackRequest> b)
    {
        b.ToTable("feedback_requests");
        b.HasKey(f => f.Id);

        b.Property(f => f.FullName).HasMaxLength(FeedbackRequest.MaxFullNameLength).IsRequired();
        b.Property(f => f.Email).HasMaxLength(FeedbackRequest.MaxEmailLength).IsRequired();
        b.Property(f => f.Phone).HasMaxLength(FeedbackRequest.MaxPhoneLength).IsRequired();
        b.Property(f => f.Message).HasMaxLength(FeedbackRequest.MaxMessageLength).IsRequired();
        b.Property(f => f.HandledAtUtc);

        // The inbox reads newest-first, and the retention sweep deletes by age — both walk this index.
        b.HasIndex(f => f.CreatedAtUtc);

        b.XminConcurrencyToken();
    }
}
