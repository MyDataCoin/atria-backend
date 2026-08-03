using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Governance.Commands;
using Atria.Domain.Governance;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Raises a dual-approval request to publish a property, and returns the request id. Publication is
/// one of the three actions no single person takes alone: it is what puts an offering in front of
/// investors, and once someone has bought in, pulling it back rewrites the terms of a live issue.
/// The property is only opened when a second administrator approves the request.
/// </summary>
public sealed class PublishPropertyCommandHandler
    : IRequestHandler<PublishPropertyCommand, Result<Guid>>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly ISender _sender;

    public PublishPropertyCommandHandler(
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        ISender sender)
    {
        _properties = properties;
        _currentUser = currentUser;
        _sender = sender;
    }

    public async Task<Result<Guid>> Handle(PublishPropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<Guid>(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure<Guid>(
                Error.Forbidden("property.forbidden", "Only administrators can publish properties."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure<Guid>(
                Error.NotFound("property.notFound", "Property not found."));

        // Checked here as well as in the executor so an impossible request is refused up front rather
        // than sitting in the approval queue until someone tries to approve it.
        if (property.Status is not (PropertyStatus.Draft or PropertyStatus.ComingSoon))
            return Result.Failure<Guid>(
                Error.Conflict("property.not_publishable", "Only a draft or coming-soon property can be published."));

        return await _sender.Send(
            new RequestCriticalActionCommand(
                CriticalActionKind.IssuePublication, property.Id, $"Публикация объекта «{property.Name}»"),
            ct);
    }
}
