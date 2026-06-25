using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for the S3-compatible object store backing document storage.
/// Bound from configuration section "S3"; validated on start. Credentials are
/// resolved by the AWS SDK's default chain (env vars / profile / IAM role), so no
/// access keys live here.
/// </summary>
public sealed class S3Options
{
    public const string SectionName = "S3";

    /// <summary>Target bucket for uploaded documents.</summary>
    [Required]
    public string BucketName { get; init; } = null!;

    /// <summary>AWS region system name, e.g. eu-central-1.</summary>
    [Required]
    public string Region { get; init; } = null!;

    /// <summary>Optional custom endpoint for S3-compatible stores (MinIO, LocalStack). Null = real AWS S3.</summary>
    public string? ServiceUrl { get; init; }
}
