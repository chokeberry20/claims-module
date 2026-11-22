using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaimsModule.Domain.Claims;

/// <summary>
/// Claim number <c>CLM-{YYYY}-{7-digit-zero-padded-sequence}</c> (S5.3, BR-C-04).
/// The sequence is allocated atomically and without gaps by the persistence layer, per organisation
/// and year (D-14). This value object only guarantees the format.
/// </summary>
public sealed partial record ClaimNumber
{
    public const string Prefix = "CLM";
    public const int MaxSequence = 9_999_999;
    public const int MaxLength = 50;

    private ClaimNumber(string value, int year, int sequence)
    {
        Value = value;
        Year = year;
        Sequence = sequence;
    }

    public string Value { get; }

    public int Year { get; }

    public int Sequence { get; }

    public static ClaimNumber Create(int year, int sequence)
    {
        if (year is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be a four-digit year.");
        }

        if (sequence is < 1 or > MaxSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, $"Sequence must be between 1 and {MaxSequence}.");
        }

        var value = string.Create(CultureInfo.InvariantCulture, $"{Prefix}-{year:D4}-{sequence:D7}");
        return new ClaimNumber(value, year, sequence);
    }

    public static ClaimNumber Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = FormatRegex().Match(value);
        if (!match.Success)
        {
            throw new FormatException($"'{value}' is not a valid claim number (expected CLM-YYYY-NNNNNNN).");
        }

        return Create(
            int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture));
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^CLM-(?<year>\d{4})-(?<seq>\d{7})$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatRegex();
}
