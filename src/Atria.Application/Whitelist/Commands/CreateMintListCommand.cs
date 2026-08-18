using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Whitelist;

namespace Atria.Application.Whitelist.Commands;

/// <summary>
/// Assembles a mint list for an issuance out of its mintable whitelist requests. Operator only.
/// </summary>
/// <param name="PropertyId">The issuance to build the batch for.</param>
/// <param name="EntryIds">
/// The requests to include. Empty means every mintable request the issuance currently has — the common
/// case, and the one that does not require the operator to tick two hundred boxes.
/// </param>
/// <param name="Note">What this batch is, in the operator's words.</param>
public sealed record CreateMintListCommand(
    Guid PropertyId, IReadOnlyList<Guid> EntryIds, string? Note) : IRequest<Result<Guid>>;

/// <summary>
/// Builds the batch and moves its requests out of the mintable pool in one transaction, so a request
/// can never end up in two batches. 404 when the issuance does not exist; 409 when there is nothing
/// mintable to put in a batch or a named request is not in a state to be batched.
/// </summary>
public sealed class CreateMintListCommandHandler : IRequestHandler<CreateMintListCommand, Result<Guid>>
{
    private readonly IMintListRepository _lists;
    private readonly IWhitelistEntryRepository _entries;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public CreateMintListCommandHandler(
        IMintListRepository lists,
        IWhitelistEntryRepository entries,
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _lists = lists;
        _entries = entries;
        _properties = properties;
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<Guid>> Handle(CreateMintListCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<Guid>(Error.Unauthorized("mintList.unauthorized", "Authentication required."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<Guid>(Error.NotFound("mintList.propertyNotFound", "Property not found."));

        // Named requests are fetched by id and then checked; an unnamed batch takes whatever the
        // issuance has ready. Either way the aggregate re-checks every line before it accepts them.
        IReadOnlyList<WhitelistEntry> selected;
        if (request.EntryIds.Count > 0)
        {
            var found = await _entries.GetManyAsync(request.EntryIds.Distinct().ToList(), ct);
            if (found.Count != request.EntryIds.Distinct().Count())
                return Result.Failure<Guid>(Error.NotFound(
                    "mintList.entryNotFound", "One or more of the selected requests no longer exist."));

            var unusable = found.FirstOrDefault(e => e.PropertyId != request.PropertyId || !e.IsMintable);
            if (unusable is not null)
                return Result.Failure<Guid>(Error.Conflict(
                    "mintList.entryNotMintable",
                    "A selected request is not approved, already batched, belongs to another issuance, "
                    + "or has no wallet address."));

            selected = found;
        }
        else
        {
            var ready = await _entries.ListForOperatorAsync(
                request.PropertyId, WhitelistStatus.Ready, int.MaxValue, ct);
            var mintable = ready.Select(r => r.Entry).Where(e => e.IsMintable).Select(e => e.Id).ToList();
            if (mintable.Count == 0)
                return Result.Failure<Guid>(Error.Conflict(
                    "mintList.nothingMintable", "This issuance has no requests ready to be minted."));

            // Re-fetched tracked: the operator read above is untracked and cannot be transitioned.
            selected = await _entries.GetManyAsync(mintable, ct);
        }

        var number = await NextNumberAsync(ct);

        MintList list;
        try
        {
            list = MintList.Create(
                number, request.PropertyId, property.TokenContractAddress, property.TokenChain,
                userId.Value, request.Note, selected);
        }
        catch (DomainException ex)
        {
            return Result.Failure<Guid>(Error.Conflict("mintList.invalid", ex.Message));
        }

        await _lists.AddAsync(list, ct);

        foreach (var entry in selected)
        {
            entry.IncludeIn(list.Id);
            _entries.Update(entry);
        }

        await _uow.SaveChangesAsync(ct);

        return Result.Success(list.Id);
    }

    /// <summary>
    /// The next batch number of the year, <c>ML-{year}-{seq:D4}</c>. Two operators assembling a batch
    /// at the same instant would compute the same sequence; the unique index on the number is what
    /// stops them, and the loser retries. Operators build batches by hand, a few times a week — a
    /// sequence table to close a race that rare would cost more than it saves.
    /// </summary>
    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var year = _clock.UtcNow.Year;
        var count = await _lists.CountForYearAsync(year, ct);
        return $"ML-{year}-{count + 1:D4}";
    }
}
