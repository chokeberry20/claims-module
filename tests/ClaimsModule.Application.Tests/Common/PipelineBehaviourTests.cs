using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Behaviours;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Domain.Common;
using FluentValidation;
using MediatR;
using NSubstitute;

namespace ClaimsModule.Application.Tests.Common;

public sealed class ValidationBehaviourTests
{
    [Fact]
    public async Task Collects_failures_from_every_validator_into_one_422_exception_without_calling_the_handler()
    {
        var behaviour = new ValidationBehaviour<TestCommand, TestResult>(
            [new TestCommandValidator(), new SecondTestCommandValidator()]);
        var handlerCalled = false;

        var error = await Should.ThrowAsync<RequestValidationException>(() =>
            behaviour.Handle(new TestCommand(""), _ => { handlerCalled = true; return Task.FromResult(new TestResult(Guid.Empty, "")); }, default));

        error.Violations.Select(v => (v.Code, v.Field)).ShouldBe([("NAME_REQUIRED", "Name")]);
        handlerCalled.ShouldBeFalse();

        error = await Should.ThrowAsync<RequestValidationException>(() =>
            behaviour.Handle(new TestCommand("far too long"), _ => Task.FromResult(new TestResult(Guid.Empty, "")), default));
        error.Violations.Single().Code.ShouldBe("NAME_TOO_LONG");
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behaviour = new ValidationBehaviour<TestCommand, TestResult>([new TestCommandValidator()]);

        var result = await behaviour.Handle(new TestCommand("ok"), _ => Task.FromResult(new TestResult(Guid.Empty, "done")), default);

        result.Name.ShouldBe("done");
    }
}

public sealed class AuthorizationBehaviourTests
{
    private static readonly RequestHandlerDelegate<TestResult> Ok = _ => Task.FromResult(new TestResult(Guid.Empty, "ok"));

    [Fact]
    public async Task Anonymous_caller_is_rejected_with_401()
    {
        var behaviour = new AuthorizationBehaviour<TestCommand, TestResult>(StubCurrentUser.Anonymous());

        await Should.ThrowAsync<UnauthenticatedException>(() => behaviour.Handle(new TestCommand("x"), Ok, default));
    }

    [Theory]
    [InlineData(UserRole.Handler, false)]
    [InlineData(UserRole.Supervisor, true)]
    [InlineData(UserRole.Manager, true)]
    public async Task Required_role_is_enforced_with_inheritance(UserRole role, bool allowed)
    {
        var behaviour = new AuthorizationBehaviour<SupervisorCommand, TestResult>(StubCurrentUser.As(role));

        if (allowed)
        {
            (await behaviour.Handle(new SupervisorCommand(), Ok, default)).Name.ShouldBe("ok");
        }
        else
        {
            (await Should.ThrowAsync<ForbiddenAccessException>(() => behaviour.Handle(new SupervisorCommand(), Ok, default)))
                .RequiredRole.ShouldBe(UserRole.Supervisor);
        }
    }

    [Fact]
    public async Task Without_an_attribute_any_authenticated_user_may_send()
    {
        var behaviour = new AuthorizationBehaviour<TestCommand, TestResult>(StubCurrentUser.As(UserRole.Handler));

        (await behaviour.Handle(new TestCommand("x"), Ok, default)).Name.ShouldBe("ok");
    }

    [Fact]
    public async Task System_requests_need_no_user()
    {
        var behaviour = new AuthorizationBehaviour<SystemCommand, TestResult>(StubCurrentUser.Anonymous());

        (await behaviour.Handle(new SystemCommand(), Ok, default)).Name.ShouldBe("ok");
    }
}

public sealed class TransactionBehaviourTests
{
    [Fact]
    public async Task Runs_the_handler_and_saves_once_inside_the_unit_of_work_transaction()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var calls = new List<string>();
        uow.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<TestResult>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                calls.Add("begin");
                var result = await ci.Arg<Func<CancellationToken, Task<TestResult>>>()(CancellationToken.None);
                calls.Add("commit");
                return result;
            });
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ => { calls.Add("save"); return 1; });
        var behaviour = new TransactionBehaviour<TestCommand, TestResult>(uow);

        var result = await behaviour.Handle(new TestCommand("x"), _ => { calls.Add("handler"); return Task.FromResult(new TestResult(Guid.Empty, "ok")); }, default);

        result.Name.ShouldBe("ok");
        calls.ShouldBe(["begin", "handler", "save", "commit"]);
    }
}

public sealed class IdempotencyBehaviourTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly StubCurrentUser _user = StubCurrentUser.As(UserRole.Handler);
    private readonly TimeProvider _clock = TimeProvider.System;
    private int _handlerCalls;

    private IdempotencyBehaviour<TestCommand, TestResult> Behaviour() => new(_store, _user, _clock);

    private Task<TestResult> Send(TestCommand command) =>
        Behaviour().Handle(command, _ => { _handlerCalls++; return Task.FromResult(new TestResult(Guid.NewGuid(), command.Name)); }, default);

    [Fact]
    public async Task Without_a_key_the_handler_runs_and_nothing_is_stored()
    {
        await Send(new TestCommand("a"));

        _handlerCalls.ShouldBe(1);
        _store.DidNotReceive().Add(Arg.Any<StoredIdempotentResponse>());
    }

    [Fact]
    public async Task First_use_of_a_key_runs_the_handler_and_stores_the_result()
    {
        StoredIdempotentResponse? stored = null;
        _store.Add(Arg.Do<StoredIdempotentResponse>(r => stored = r));

        var result = await Send(new TestCommand("a", "key-1"));

        _handlerCalls.ShouldBe(1);
        stored.ShouldNotBeNull();
        stored.UserId.ShouldBe(_user.UserId!.Value);
        stored.IdempotencyKey.ShouldBe("key-1");
        stored.RequestType.ShouldBe(typeof(TestCommand).FullName);
        stored.ResponseJson.ShouldContain(result.Id.ToString());
        (stored.ExpiresAt - stored.CreatedAt).ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task Same_key_and_payload_replays_the_stored_result_without_running_the_handler()
    {
        StoredIdempotentResponse? stored = null;
        _store.Add(Arg.Do<StoredIdempotentResponse>(r => stored = r));
        var first = await Send(new TestCommand("a", "key-1"));
        _store.FindAsync(_user.UserId!.Value, "key-1", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(stored);

        var replay = await Send(new TestCommand("a", "key-1"));

        replay.ShouldBe(first);
        _handlerCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_payload_is_a_conflict()
    {
        StoredIdempotentResponse? stored = null;
        _store.Add(Arg.Do<StoredIdempotentResponse>(r => stored = r));
        await Send(new TestCommand("a", "key-1"));
        _store.FindAsync(_user.UserId!.Value, "key-1", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(stored);

        await Should.ThrowAsync<IdempotencyConflictException>(() => Send(new TestCommand("b", "key-1")));
        _handlerCalls.ShouldBe(1);
    }
}
