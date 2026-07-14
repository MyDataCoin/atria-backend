using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Publications.Commands;

/// <summary>Takes a publication down (hard delete). Admin only.</summary>
public sealed record DeletePublicationCommand(Guid Id) : IRequest<Result>;

public sealed class DeletePublicationCommandHandler : IRequestHandler<DeletePublicationCommand, Result>
{
    private readonly IPublicationRepository _publications;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePublicationCommandHandler(IPublicationRepository publications, IUnitOfWork unitOfWork)
    {
        _publications = publications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeletePublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.Id, ct);
        if (publication is null)
            return Result.Failure(Error.NotFound("publication.not_found", "Publication not found."));

        _publications.Remove(publication);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
