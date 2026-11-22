using System.Buffers.Binary;

namespace ClaimsModule.Domain.Common;

/// <summary>
/// Generates GUIDs that sort sequentially in SQL Server's <c>uniqueidentifier</c> order, to avoid
/// clustered-index fragmentation (spec S15.1). The ids are assigned in the domain because domain
/// events need them before SaveChanges. The column still defaults to NEWSEQUENTIALID().
/// </summary>
/// <remarks>
/// SQL Server compares uniqueidentifier bytes in the groups 10-15, then 8-9, then 6-7, 4-5, 0-3.
/// A monotonically increasing 64-bit counter (seeded from UTC ticks) is written big-endian:
/// its high 6 bytes go to bytes 10-15 and its low 2 bytes go to bytes 8-9. The rest stays random.
/// <see cref="Guid.CreateVersion7()"/> is not used because its timestamp sits in the leading bytes,
/// which SQL Server compares last.
/// </remarks>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    public static Guid NewGuid()
    {
        Span<byte> guidBytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(guidBytes);

        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, Interlocked.Increment(ref _counter));

        counterBytes[..6].CopyTo(guidBytes[10..]);
        counterBytes[6..].CopyTo(guidBytes[8..10]);

        return new Guid(guidBytes);
    }
}
