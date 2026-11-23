using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

/// <summary>
/// Thin controllers (B2.4): each action builds one command or query and sends it through MediatR, where
/// validation, role checks, transactions and idempotency happen. Errors are turned into the S10.4 body by
/// <see cref="ApiExceptionHandler"/>.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ApiError>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    public const int MaxIdempotencyKeyLength = 200;

    protected ISender Sender => HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>
    /// The optional <c>Idempotency-Key</c> header (S10, D-66): 1–200 visible ASCII characters. A malformed
    /// key is a 422 rather than being ignored, so a client never believes it is protected when it isn't.
    /// </summary>
    protected string? IdempotencyKey
    {
        get
        {
            if (!Request.Headers.TryGetValue(AuthAndIdempotencyOperationFilter.IdempotencyHeader, out var values))
            {
                return null;
            }

            var key = values.Count == 1 ? values[0]?.Trim() : null;
            if (string.IsNullOrEmpty(key) || key.Length > MaxIdempotencyKeyLength || key.Any(c => c is < '!' or > '~'))
            {
                throw new RequestValidationException([new RuleViolation(
                    "IDEMPOTENCY_KEY_INVALID",
                    $"Idempotency-Key must be a single value of 1 to {MaxIdempotencyKeyLength} visible ASCII characters.",
                    AuthAndIdempotencyOperationFilter.IdempotencyHeader)]);
            }

            return key;
        }
    }
}
