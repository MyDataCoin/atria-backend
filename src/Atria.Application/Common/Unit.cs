namespace Atria.Application.Common;

/// <summary>Void substitute for commands that return no payload.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = new();
}
