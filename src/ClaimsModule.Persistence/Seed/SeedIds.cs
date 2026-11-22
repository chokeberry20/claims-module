using System.Security.Cryptography;
using System.Text;

namespace ClaimsModule.Persistence.Seed;

/// <summary>
/// Stable, name-based GUIDs for seeded rows. The same name always gives the same id, so the
/// migrations are reproducible and no GUID literals are hand-maintained. The first 16 bytes of
/// SHA-256 are used, with the RFC 4122 version 8 ("custom") and variant bits set.
/// </summary>
internal static class SeedIds
{
    public static Guid For(string name)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes("ClaimsModule.Seed:" + name), hash);
        var bytes = hash[..16].ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80); // version 8 (byte order as used by Guid(byte[]))
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
