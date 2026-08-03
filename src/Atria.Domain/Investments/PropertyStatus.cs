namespace Atria.Domain.Investments;

/// <summary>
/// Property lifecycle: <see cref="Draft"/> when created, <see cref="ComingSoon"/> once announced
/// (teased on the public site but not yet investable), <see cref="Open"/> once published (accepting
/// investment), and <see cref="Completed"/> when its offering is finished.
/// </summary>
/// <remarks>
/// Enum VALUES are persisted as integers. <see cref="ComingSoon"/> is appended as 3 (not slotted
/// between Draft and Open) so the existing Open=1 / Completed=2 rows keep their stored values.
/// </remarks>
public enum PropertyStatus
{
    Draft = 0,
    Open = 1,
    Completed = 2,
    ComingSoon = 3,

    /// <summary>
    /// The issue was declared invalid (draft Decree, §73). Terminal: shares are withdrawn from
    /// circulation and holders are owed their money back. Nothing can be sold or issued again.
    /// </summary>
    Invalidated = 4
}
