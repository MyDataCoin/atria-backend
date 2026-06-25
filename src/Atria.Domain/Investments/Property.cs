using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A real estate property that issues a fixed pool of tokens. Investors buy tokens
/// against the available supply.
/// </summary>
public sealed class Property : AggregateRoot
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Address { get; private set; }
    public decimal TotalValue { get; private set; }
    public decimal TokenPrice { get; private set; }
    public long TotalTokens { get; private set; }
    public long AvailableTokens { get; private set; }
    public string Currency { get; private set; } = null!;
    public bool IsActive { get; private set; }

    // private ctor: creation only through the factory method
    private Property() { }

    public static Property Create(
        string name, string? description, string? address, decimal totalValue,
        decimal tokenPrice, long totalTokens, string currency)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Property name is required.");
        if (totalValue <= 0)
            throw new DomainException("Property total value must be positive.");
        if (tokenPrice <= 0)
            throw new DomainException("Token price must be positive.");
        if (totalTokens <= 0)
            throw new DomainException("Total tokens must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        return new Property
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Address = address,
            TotalValue = totalValue,
            TokenPrice = tokenPrice,
            TotalTokens = totalTokens,
            AvailableTokens = totalTokens, // full supply available at creation
            Currency = currency,
            IsActive = true
        };
    }

    /// <summary>Reserves <paramref name="count"/> tokens, reducing the available supply.</summary>
    public void AllocateTokens(long count)
    {
        if (count <= 0)
            throw new DomainException("Token allocation count must be positive.");
        if (count > AvailableTokens)
            throw new DomainException("Cannot allocate more tokens than are available.");

        AvailableTokens -= count;
    }
}
