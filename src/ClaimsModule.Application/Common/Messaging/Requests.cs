using ClaimsModule.Domain.Common;
using MediatR;

namespace ClaimsModule.Application.Common.Messaging;

/// <summary>
/// A write (brief §2.4: every write is a Command). Commands run inside one database transaction
/// (TransactionBehaviour); the Unit of Work saves once, at the end.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A command with no meaningful result.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>A read (brief §2.4: every read is a Query). Queries never change state and use no transaction.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>
/// A command that honours the <c>Idempotency-Key</c> header (S10). The API copies the header into
/// <see cref="IdempotencyKey"/>; a repeat with the same key and the same payload replays the first
/// result instead of running again (IdempotencyBehaviour).
/// </summary>
public interface IIdempotentCommand
{
    string? IdempotencyKey { get; }
}

/// <summary>
/// The minimum role needed to send the request (S3; roles inherit downwards). Without this
/// attribute any authenticated user may send it. Finer rules (amount authority, ownership) stay in
/// the domain.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresRoleAttribute(UserRole role) : Attribute
{
    public UserRole Role { get; } = role;
}

/// <summary>
/// Sent by background jobs, not by users (GL posting, SLA scan, stale-posting sweep). No user is
/// required, and the API never exposes these requests.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemRequestAttribute : Attribute;
