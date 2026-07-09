namespace Atria.Domain.Support;

/// <summary>
/// Who wrote a ticket message. Derived from the caller's JWT role at the application
/// layer (an Investor writes as <see cref="Investor"/>, an Admin as <see cref="Support"/>),
/// never trusted from the request body.
/// </summary>
public enum MessageAuthor
{
    Investor = 0,
    Support = 1
}
