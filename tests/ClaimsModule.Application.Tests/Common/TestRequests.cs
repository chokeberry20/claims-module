using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Common;
using FluentValidation;

namespace ClaimsModule.Application.Tests.Common;

public sealed record TestCommand(string Name, string? IdempotencyKey = null) : ICommand<TestResult>, IIdempotentCommand;

public sealed record TestResult(Guid Id, string Name);

[RequiresRole(UserRole.Supervisor)]
public sealed record SupervisorCommand : ICommand<TestResult>;

[SystemRequest]
public sealed record SystemCommand : ICommand<TestResult>;

public sealed class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator() => RuleFor(c => c.Name).NotEmpty().WithErrorCode("NAME_REQUIRED");
}

public sealed class SecondTestCommandValidator : AbstractValidator<TestCommand>
{
    public SecondTestCommandValidator() => RuleFor(c => c.Name).MaximumLength(5).WithErrorCode("NAME_TOO_LONG");
}

internal sealed class StubCurrentUser(Actor? actor) : ICurrentUser
{
    public bool IsAuthenticated => actor is not null;

    public Guid? UserId => actor?.UserId;

    public string? DisplayName => actor?.DisplayName;

    public UserRole? Role => actor?.Role;

    public Actor GetActor() => actor ?? throw new InvalidOperationException("No user.");

    public static StubCurrentUser As(UserRole role) => new(new Actor(Guid.NewGuid(), $"Test {role}", role));

    public static StubCurrentUser Anonymous() => new(null);
}
