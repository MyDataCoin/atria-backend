namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for public media (property photos/documents). Files are copied to a remote host over
/// SCP and served by that host's web server (nginx); the persisted URL is absolute. The upload path
/// and the public URL mirror each other: <c>{RemoteRoot}/{category}/{name}</c> on disk ↔
/// <c>{PublicBaseUrl}{PublicBasePath}/{category}/{name}</c> as URL.
/// </summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>Absolute base of the public URL, e.g. <c>https://atria-api.eaysdev.online</c>.</summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    /// <summary>URL path prefix the files are served under, e.g. <c>/media</c>.</summary>
    public string PublicBasePath { get; init; } = "/media";

    /// <summary>Remote filesystem root files are copied into, e.g. <c>/media</c>.</summary>
    public string RemoteRoot { get; init; } = "/media";

    /// <summary>SCP/SSH target host (IP or DNS), e.g. <c>84.54.12.242</c>.</summary>
    public string ScpHost { get; init; } = string.Empty;

    public int ScpPort { get; init; } = 22;

    public string ScpUsername { get; init; } = string.Empty;

    /// <summary>Password auth (used when <see cref="ScpPrivateKeyPath"/> is empty). Secret.</summary>
    public string ScpPassword { get; init; } = string.Empty;

    /// <summary>Optional path to a private key file (preferred over password).</summary>
    public string ScpPrivateKeyPath { get; init; } = string.Empty;
}
