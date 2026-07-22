using Atria.Application.Abstractions;
using Atria.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers.Common;

/// <summary>
/// Thin base for all controllers. Holds the in-process mediator (<see cref="ISender"/>)
/// and centralizes the Result -&gt; HTTP mapping so controllers stay free of business logic.
/// </summary>
[ApiController]
[Consumes("application/json")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The mediator every controller dispatches commands/queries through.</summary>
    protected ISender Sender { get; }

    protected ApiControllerBase(ISender sender) => Sender = sender;

    /// <summary>Maps a non-generic <see cref="Result"/> to 204 on success, or a ProblemDetails error.</summary>
    protected IActionResult ToActionResult(Result result)
        => result.IsSuccess ? NoContent() : Problem(result.Error);

    /// <summary>Maps a <see cref="Result{T}"/> to 200 + value on success, or a ProblemDetails error.</summary>
    protected IActionResult ToActionResult<T>(Result<T> result)
        => result.IsSuccess ? Ok(result.Value) : Problem(result.Error);

    /// <summary>
    /// Maps a successful <see cref="Result{T}"/> to 201 Created (pointing at the supplied
    /// action/route), or to a ProblemDetails error otherwise.
    /// </summary>
    protected IActionResult ToCreatedResult<T>(Result<T> result, string actionName, object? routeValues)
        => result.IsSuccess
            ? CreatedAtAction(actionName, routeValues, result.Value)
            : Problem(result.Error);

    /// <summary>Builds a sanitized ProblemDetails from the domain error, choosing the HTTP status by ErrorType.</summary>
    private ObjectResult Problem(Error error)
    {
        var status = StatusFor(error.Type);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Code,
            Detail = error.Message
        };

        // Surface a machine-readable "reason" for a banned account so the client can show the
        // dedicated blocked screen (it keys off 403 + a ban marker), not a generic error. The
        // super-admin's ban reason travels in the error message (detail); expose it as banReason too
        // so the client can render it directly. Suppress the generic fallback text as a reason.
        if (error.Code == Error.AccountBannedCode)
        {
            problem.Extensions["reason"] = "banned";
            if (!string.IsNullOrWhiteSpace(error.Message) && error.Message != Error.AccountBannedMessage)
                problem.Extensions["banReason"] = error.Message;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }

    /// <summary>Maps a domain <see cref="ErrorType"/> to the matching HTTP status code.</summary>
    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.ExternalService => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}
