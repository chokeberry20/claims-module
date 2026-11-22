using ClaimsModule.Domain.Claims;

namespace ClaimsModule.Domain.Tests.Claims;

public sealed class ClaimNumberTests
{
    [Theory]
    [InlineData(2026, 142, "CLM-2026-0000142")]
    [InlineData(2026, 1, "CLM-2026-0000001")]
    [InlineData(2027, 9_999_999, "CLM-2027-9999999")]
    public void Formats_as_CLM_year_and_seven_digit_sequence(int year, int sequence, string expected)
    {
        var number = ClaimNumber.Create(year, sequence);

        number.Value.ShouldBe(expected);
        number.Year.ShouldBe(year);
        number.Sequence.ShouldBe(sequence);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 10_000_000)]
    [InlineData(999, 1)]
    public void Rejects_out_of_range_parts(int year, int sequence) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ClaimNumber.Create(year, sequence));

    [Fact]
    public void Parse_round_trips() =>
        ClaimNumber.Parse("CLM-2026-0000142").ShouldBe(ClaimNumber.Create(2026, 142));

    [Theory]
    [InlineData("CLM-2026-142")]
    [InlineData("clm-2026-0000142")]
    [InlineData("CLM-26-0000142")]
    [InlineData("CLM-2026-0000142 ")]
    [InlineData("")]
    public void Parse_rejects_malformed_values(string value) =>
        Should.Throw<FormatException>(() => ClaimNumber.Parse(value));

    [Fact]
    public void Parse_rejects_zero_sequence() =>
        Should.Throw<ArgumentOutOfRangeException>(() => ClaimNumber.Parse("CLM-2026-0000000"));
}
