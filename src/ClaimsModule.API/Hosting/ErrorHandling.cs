using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClaimsModule.API.Hosting;

/// <summary>
/// The error body of every failed request (S10.4, task 33):
/// <c>{ "type": "ValidationError", "title": "...", "status": 422, "errors": { "LossDate": ["..."] } }</c>.
/// <para>
/// Additions to the spec's shape: <see cref="Codes"/> has the machine-readable rule codes per field
/// (for the UI), <see cref="Detail"/> a human-readable summary, and <see cref="CorrelationId"/> ties
/// the response to the logs and audit entries. Field-less rule violations use the key <c>General</c>.
/// </para>
/// </summary>
public sealed record ApiError
{
    public const string GeneralField = "General";

    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
    public IReadOnlyDictionary<string, string[]>? Codes { get; init; }
    public Guid? CorrelationId { get; init; }
}

internal static class ApiErrors
{
    public static ApiError Validation(IEnumerable<RuleViolation> violations)
    {
        var byField = violations.GroupBy(v => string.IsNullOrWhiteSpace(v.Field) ? ApiError.GeneralField : v.Field!).ToList();
        return new ApiError
        {
            Type = "ValidationError",
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status422UnprocessableEntity,
            Errors = byField.ToDictionary(g => g.Key, g => g.Select(v => v.Message).Distinct().ToArray()),
            Codes = byField.ToDictionary(g => g.Key, g => g.Select(v => v.Code).Distinct().ToArray()),
        };
    }

    public static ApiError Unauthorized() => new()
    {
        Type = "Unauthorized",
        Title = "Authentication is required.",
        Status = StatusCodes.Status401Unauthorized,
        Detail = "Sign in with POST /api/auth/token and send the token as 'Authorization: Bearer <token>'.",
    };

    public static ApiError Forbidden(string detail, string? code = null) => new()
    {
        Type = "Forbidden",
        Title = "You are not allowed to perform this action.",
        Status = StatusCodes.Status403Forbidden,
        Detail = detail,
        Codes = code is null ? null : new Dictionary<string, string[]> { [ApiError.GeneralField] = [code] },
    };

    public static ApiError NotFound(string detail) => new()
    {
        Type = "NotFound",
        Title = "The resource was not found.",
        Status = StatusCodes.Status404NotFound,
        Detail = detail,
    };

    public static ApiError Conflict(string detail, string code) => new()
    {
        Type = "Conflict",
        Title = "The request conflicts with the current state.",
        Status = StatusCodes.Status409Conflict,
        Detail = detail,
        Codes = new Dictionary<string, string[]> { [ApiError.GeneralField] = [code] },
    };

    public static ApiError ServerError() => new()
    {
        Type = "ServerError",
        Title = "An unexpected error occurred.",
        Status = StatusCodes.Status500InternalServerError,
        Detail = "The error has been logged. Quote the correlation id when reporting it.",
    };

    /// <summary>Maps an exception to its error body; null for exceptions that are not expected (500).</summary>
    public static ApiError? FromException(Exception exception) => exception switch
    {
        RequestValidationException e => Validation(e.Violations),
        DomainRuleViolationException e => Validation(e.Violations),
        UnauthenticatedException => Unauthorized(),
        ForbiddenAccessException e => Forbidden(e.Message, "ROLE_REQUIRED"),
        DomainForbiddenException e => Forbidden(e.Message, e.Code),
        NotFoundException e => NotFound(e.Message),
        DomainNotFoundException e => NotFound(e.Message),
        ConcurrencyException => Conflict(
            "The claim was changed by someone else at the same time. Reload it and try again.", "CONCURRENCY_CONFLICT"),
        IdempotencyConflictException e => Conflict(e.Message, "IDEMPOTENCY_CONFLICT"),
        _ => null,
    };

    public static Task WriteAsync(HttpContext http, ApiError error)
    {
        var correlation = http.RequestServices.GetService<ICorrelationContext>()?.CorrelationId;
        var json = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        http.Response.StatusCode = error.Status;
        return http.Response.WriteAsJsonAsync(error with { CorrelationId = correlation }, json, contentType: "application/problem+json");
    }

    /// <summary>MVC model binding of parsable input (wrong types, unknown enum values, missing body): the same 422 body. Unparsable JSON is a 400 (<see cref="MalformedJsonFilter"/>).</summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var violations = context.ModelState
            .Where(e => e.Value is { Errors.Count: > 0 })
            .SelectMany(e => e.Value!.Errors.Select(err => new RuleViolation(
                "REQUEST_VALUE_INVALID",
                string.IsNullOrWhiteSpace(err.ErrorMessage) ? "The value is not valid." : err.ErrorMessage,
                FieldName(e.Key))))
            .ToList();
        return new ApiErrorResult(Validation(violations.Count > 0 ? violations : [new RuleViolation("REQUEST_VALUE_INVALID", "The request body is not valid.")]));
    }

    // "$.lossDate" (System.Text.Json path) or "body.LossDate" become "LossDate", like the FluentValidation keys.
    private static string? FieldName(string key)
    {
        var name = key.TrimStart('$', '.');
        if (name.Length == 0)
        {
            return null;
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}

/// <summary>An action result that writes an <see cref="ApiError"/> exactly like the exception handler (status, problem+json, correlation id).</summary>
internal sealed class ApiErrorResult(ApiError error) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => ApiErrors.WriteAsync(context.HttpContext, error);
}

/// <summary>
/// Turns exceptions into the S10.4 body (B6.2 "middleware error handling pipeline"). Expected
/// exceptions (rules, auth, not found, conflicts) are logged at Information without a stack trace;
/// anything else is logged as an error and answered with a generic 500 that leaks no details.
/// </summary>
internal sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var error = ApiErrors.FromException(exception);
        if (error is null)
        {
            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                return true; // the client went away; nothing to answer
            }

            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
            error = ApiErrors.ServerError();
        }
        else
        {
            LogExpected(logger, error.Status, exception.GetType().Name, exception.Message);
        }

        await ApiErrors.WriteAsync(httpContext, error);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {Status} ({ExceptionType}): {Reason}")]
    private static partial void LogExpected(ILogger logger, int status, string exceptionType, string reason);
}

internal static class JsonSetup
{
    /// <summary>Enums travel as their names ("Indemnity", "PendingApproval"), for the API and its Swagger schemas.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    }
}
