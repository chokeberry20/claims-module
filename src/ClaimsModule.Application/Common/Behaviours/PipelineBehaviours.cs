using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Application.Common.Behaviours;

// Pipeline order (outermost first), registered in DependencyInjection.AddApplication:
//   Logging → Validation → Authorization → Transaction (commands) → Idempotency (commands) → handler.
// Validation and authorization run before any database work. Idempotency runs inside the
// transaction, so the stored result commits atomically with the command's changes.

/// <summary>Logs every request with its duration and outcome (brief §6.2 structured logging).</summary>
internal sealed partial class LoggingBehaviour<TRequest, TResponse>(
    ILogger<LoggingBehaviour<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken);
            LogHandled(logger, name, currentUser.UserId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            LogFailed(logger, name, currentUser.UserId, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name, ex.Message);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} for user {UserId} in {ElapsedMs:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, Guid? userId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} for user {UserId} failed after {ElapsedMs:0.0} ms: {ExceptionType}: {ExceptionMessage}")]
    private static partial void LogFailed(ILogger logger, string requestName, Guid? userId, double elapsedMs, string exceptionType, string exceptionMessage);
}

/// <summary>
/// Runs every FluentValidation validator for the request (D-05 layer a). All failures are collected
/// into one <see cref="RequestValidationException"/> (HTTP 422, S10.4); nothing is saved.
/// </summary>
internal sealed class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<RuleViolation>();
        foreach (var validator in validators)
        {
            // A fresh context per validator: a shared ValidationContext accumulates failures, so a
            // second validator would report the first one's errors again (caught by a unit test).
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);
            failures.AddRange(result.Errors.Select(e => new RuleViolation(e.ErrorCode, e.ErrorMessage, e.PropertyName)));
        }

        if (failures.Count > 0)
        {
            throw new RequestValidationException(failures);
        }

        return await next(cancellationToken);
    }
}

/// <summary>
/// Requires an authenticated user and the request's minimum role (<see cref="RequiresRoleAttribute"/>,
/// default Handler). <see cref="SystemRequestAttribute"/> requests (background jobs) skip the check.
/// </summary>
internal sealed class AuthorizationBehaviour<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, (bool IsSystem, UserRole Required)> Rules = new();

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var (isSystem, required) = Rules.GetOrAdd(typeof(TRequest), static type => (
            type.GetCustomAttribute<SystemRequestAttribute>() is not null,
            type.GetCustomAttribute<RequiresRoleAttribute>()?.Role ?? UserRole.Handler));

        if (!isSystem)
        {
            if (!currentUser.IsAuthenticated || currentUser.Role is null)
            {
                throw new UnauthenticatedException();
            }

            if (currentUser.Role < required)
            {
                throw new ForbiddenAccessException(required);
            }
        }

        return next(cancellationToken);
    }
}

/// <summary>
/// Runs the rest of the pipeline for a command in one database transaction and saves once at the
/// end. With the retrying execution strategy a transient failure replays the whole unit, idempotency
/// check and handler included, on a cleared change tracker (D-56).
/// </summary>
internal sealed class TransactionBehaviour<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var response = await next(ct);
                await unitOfWork.SaveChangesAsync(ct);
                return response;
            },
            cancellationToken);
}

/// <summary>
/// Idempotency-Key replay (S10, D-66). A key seen before (same user, unexpired) returns the stored
/// result if the payload matches, or a 409 if it doesn't. Otherwise the command runs and its result
/// is stored in the same transaction.
/// </summary>
internal sealed class IdempotencyBehaviour<TRequest, TResponse>(
    IIdempotencyStore store,
    ICurrentUser currentUser,
    TimeProvider clock) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IIdempotentCommand { IdempotencyKey: { Length: > 0 } key } || currentUser.UserId is not { } userId)
        {
            return await next(cancellationToken);
        }

        var requestType = typeof(TRequest).FullName!;
        var requestHash = Hash(request);
        var now = clock.GetUtcNow();

        var existing = await store.FindAsync(userId, key, now, cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestType != requestType || existing.RequestHash != requestHash)
            {
                throw new IdempotencyConflictException("This Idempotency-Key was already used for a different request.");
            }

            return JsonSerializer.Deserialize<TResponse>(existing.ResponseJson, Json)!;
        }

        var response = await next(cancellationToken);
        store.Add(new StoredIdempotentResponse(
            userId, key, requestType, requestHash, JsonSerializer.Serialize(response, Json), now, now + Retention));
        return response;
    }

    internal static string Hash(TRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));
}
