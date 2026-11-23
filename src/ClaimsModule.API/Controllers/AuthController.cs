using ClaimsModule.API.Contracts;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using ClaimsModule.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClaimsModule.API.Controllers;

/// <summary>A demo user as shown by the role switcher. No password, ever.</summary>
public sealed record DemoUserDto(Guid UserId, string UserName, string DisplayName, UserRole Role);

/// <summary>The issued bearer token.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, Guid UserId, string DisplayName, UserRole Role);

/// <summary>The signed-in caller, from the token.</summary>
public sealed record CurrentUserDto(Guid UserId, string DisplayName, UserRole Role);

/// <summary>
/// Mock authentication (S11.4, D-32): demo users with passwords from Key Vault or user secrets. It is
/// identity plumbing, not claims behaviour, so it calls the Infrastructure services directly rather than
/// going through MediatR (D-72).
/// </summary>
[Route("api/auth")]
[Tags("Authentication")]
public sealed class AuthController(IDemoUserAuthenticator users, IJwtTokenIssuer tokens) : ApiControllerBase
{
    public const int MaxCredentialLength = 200;

    /// <summary>Signs in a demo user and returns an HS256 bearer token (8 hours).</summary>
    /// <remarks>Rate limited per client address. A wrong user name or password gives the same 401.</remarks>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.SignIn)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public IActionResult Token(TokenRequest request)
    {
        var violations = new List<RuleViolation>();
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > MaxCredentialLength)
        {
            violations.Add(new RuleViolation("USERNAME_REQUIRED", "User name is required.", nameof(request.UserName)));
        }

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > MaxCredentialLength)
        {
            violations.Add(new RuleViolation("PASSWORD_REQUIRED", "Password is required.", nameof(request.Password)));
        }

        if (violations.Count > 0)
        {
            throw new ClaimsModule.Application.Common.Exceptions.RequestValidationException(violations);
        }

        var user = users.Authenticate(request.UserName.Trim(), request.Password);
        if (user is null)
        {
            return new ApiErrorResult(ApiErrors.Unauthorized() with { Title = "Invalid user name or password.", Detail = null });
        }

        var issued = tokens.Issue(user);
        return Ok(new TokenResponse(issued.AccessToken, "Bearer", issued.ExpiresAt, user.UserId, user.DisplayName, user.Role));
    }

    /// <summary>The demo users, for the UI's role switcher (names and roles only).</summary>
    [HttpGet("users")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<DemoUserDto>>(StatusCodes.Status200OK)]
    public IReadOnlyList<DemoUserDto> Users() =>
        users.ListAccounts().Select(u => new DemoUserDto(u.UserId, u.UserName, u.DisplayName, u.Role)).ToList();

    /// <summary>The signed-in caller.</summary>
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public CurrentUserDto Me([FromServices] ICurrentUser currentUser)
    {
        var actor = currentUser.GetActor();
        return new CurrentUserDto(actor.UserId, actor.DisplayName, actor.Role);
    }
}
