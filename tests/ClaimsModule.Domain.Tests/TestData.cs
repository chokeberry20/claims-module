using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Tests;

/// <summary>Shared fixed clock and actors for domain tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    public static readonly Actor Handler1 = new(Guid.NewGuid(), "Hannah Handler", UserRole.Handler);
    public static readonly Actor Handler2 = new(Guid.NewGuid(), "Harry Handler", UserRole.Handler);
    public static readonly Actor Supervisor = new(Guid.NewGuid(), "Sam Supervisor", UserRole.Supervisor);
    public static readonly Actor Supervisor2 = new(Guid.NewGuid(), "Sara Supervisor", UserRole.Supervisor);
    public static readonly Actor Manager = new(Guid.NewGuid(), "Maya Manager", UserRole.Manager);
}
