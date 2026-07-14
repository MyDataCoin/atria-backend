namespace Atria.Domain.Publications;

/// <summary>
/// Publication lifecycle. Drafts are admin-only; only <see cref="Published"/> rows are visible to
/// investors and anonymous callers.
/// </summary>
public enum PublicationStatus
{
    Draft = 0,
    Published = 1
}
