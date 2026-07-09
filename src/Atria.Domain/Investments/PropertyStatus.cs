namespace Atria.Domain.Investments;

/// <summary>
/// Property lifecycle. A property is <see cref="Draft"/> when created, becomes <see cref="Open"/>
/// once published (accepting investment), and <see cref="Completed"/> when its offering is finished.
/// </summary>
public enum PropertyStatus
{
    Draft = 0,
    Open = 1,
    Completed = 2
}
