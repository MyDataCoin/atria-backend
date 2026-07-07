using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Consents;

namespace Atria.Application.Consents.Commands;

/// <summary>Records the current investor's acceptance of a consent document version.</summary>
public sealed record RecordConsentCommand(ConsentType Type, string Version, bool Accepted)
    : IRequest<Result<Guid>>;

/// <summary>
/// Persists an acceptance record (who / when / type+version) for the authenticated investor.
/// Idempotent: re-posting the same type+version returns the existing record instead of
/// creating a duplicate. The acceptance is what a regulator relies on to prove the user
/// consented to a specific version of the text.
/// </summary>
public sealed class RecordConsentCommandHandler
    : IRequestHandler<RecordConsentCommand, Result<Guid>>
{
    private readonly IConsentRepository _consents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public RecordConsentCommandHandler(
        IConsentRepository consents, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _consents = consents;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RecordConsentCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<Guid>(Error.Unauthorized("consent.unauthorized", "Authentication required."));

        if (!request.Accepted)
            return Result.Failure<Guid>(Error.Validation("consent.not_accepted", "Consent must be accepted."));

        // Idempotent: the same type+version accepted twice is one acceptance.
        var existing = await _consents.GetAsync(userId, request.Type, request.Version, ct);
        if (existing is not null)
            return Result.Success(existing.Id);

        var consent = Consent.Record(userId, request.Type, request.Version);
        await _consents.AddAsync(consent, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(consent.Id);
    }
}
