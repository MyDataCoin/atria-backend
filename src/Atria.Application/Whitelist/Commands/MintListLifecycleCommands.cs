using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Whitelist;

namespace Atria.Application.Whitelist.Commands;

/// <summary>Marks a batch as handed to the exchange. Operator only.</summary>
/// <param name="MintListId">The batch that went out.</param>
public sealed record MarkMintListSentCommand(Guid MintListId) : IRequest<Result>;

/// <summary>
/// Draft -> Sent. Recording the hand-over is what makes the batch immutable: from here the exchange
/// holds a document, and a batch that has to change is called off and rebuilt. 404 when the batch does
/// not exist; 409 when it is not a draft.
/// </summary>
public sealed class MarkMintListSentCommandHandler : IRequestHandler<MarkMintListSentCommand, Result>
{
    private readonly IMintListRepository _lists;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public MarkMintListSentCommandHandler(
        IMintListRepository lists, IDateTimeProvider clock, IUnitOfWork uow)
    {
        _lists = lists;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(MarkMintListSentCommand request, CancellationToken ct)
    {
        var list = await _lists.GetByIdAsync(request.MintListId, ct);
        if (list is null)
            return Result.Failure(Error.NotFound("mintList.notFound", "Mint list not found."));

        try
        {
            list.MarkSent(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("mintList.notDraft", ex.Message));
        }

        _lists.Update(list);
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}

/// <summary>Records that the exchange minted a batch. Operator only.</summary>
/// <param name="MintListId">The batch the exchange executed.</param>
public sealed record MarkMintListExecutedCommand(Guid MintListId) : IRequest<Result>;

/// <summary>
/// Sent -> Executed, carrying every request in the batch to <see cref="WhitelistStatus.Minted"/> in the
/// same transaction — the batch and its rows must never disagree about whether the shares exist.
/// 404 when the batch does not exist; 409 when it was never sent.
/// </summary>
public sealed class MarkMintListExecutedCommandHandler
    : IRequestHandler<MarkMintListExecutedCommand, Result>
{
    private readonly IMintListRepository _lists;
    private readonly IWhitelistEntryRepository _entries;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public MarkMintListExecutedCommandHandler(
        IMintListRepository lists, IWhitelistEntryRepository entries, IDateTimeProvider clock, IUnitOfWork uow)
    {
        _lists = lists;
        _entries = entries;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(MarkMintListExecutedCommand request, CancellationToken ct)
    {
        var list = await _lists.GetByIdAsync(request.MintListId, ct);
        if (list is null)
            return Result.Failure(Error.NotFound("mintList.notFound", "Mint list not found."));

        var now = _clock.UtcNow;

        try
        {
            list.MarkExecuted(now);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("mintList.notSent", ex.Message));
        }

        _lists.Update(list);

        foreach (var entry in await _entries.ListByMintListAsync(list.Id, ct))
        {
            if (entry.Status != WhitelistStatus.Batched)
                continue;

            entry.MarkMinted(now);
            _entries.Update(entry);
        }

        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}

/// <summary>Calls a batch off and returns its requests to the mintable pool. Operator only.</summary>
/// <param name="MintListId">The batch to call off.</param>
/// <param name="Reason">Why — mandatory, so a cancelled batch always explains itself.</param>
public sealed record CancelMintListCommand(Guid MintListId, string Reason) : IRequest<Result>;

/// <summary>
/// Draft|Sent -> Cancelled, releasing every request it held back to <see cref="WhitelistStatus.Ready"/>
/// so they can go into the next batch. 404 when the batch does not exist; 409 when it is already
/// executed or cancelled.
/// </summary>
public sealed class CancelMintListCommandHandler : IRequestHandler<CancelMintListCommand, Result>
{
    private readonly IMintListRepository _lists;
    private readonly IWhitelistEntryRepository _entries;
    private readonly IUnitOfWork _uow;

    public CancelMintListCommandHandler(
        IMintListRepository lists, IWhitelistEntryRepository entries, IUnitOfWork uow)
    {
        _lists = lists;
        _entries = entries;
        _uow = uow;
    }

    public async Task<Result> Handle(CancelMintListCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("mintList.reasonRequired", "A cancellation reason is required."));

        var list = await _lists.GetByIdAsync(request.MintListId, ct);
        if (list is null)
            return Result.Failure(Error.NotFound("mintList.notFound", "Mint list not found."));

        try
        {
            list.Cancel(request.Reason);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("mintList.closed", ex.Message));
        }

        _lists.Update(list);

        foreach (var entry in await _entries.ListByMintListAsync(list.Id, ct))
        {
            if (entry.Status != WhitelistStatus.Batched)
                continue;

            entry.ReleaseFromMintList();
            _entries.Update(entry);
        }

        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
