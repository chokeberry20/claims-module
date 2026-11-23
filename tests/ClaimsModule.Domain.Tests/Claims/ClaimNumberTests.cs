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

    [Theory]
    [InlineData("CLM-2026-0000142")]
    [InlineData("clm-2026-0000142")] // search input: case and surrounding spaces are forgiven
    [InlineData("  CLM-2026-0000142 ")]
    public void TryParse_accepts_search_input(string value)
    {
        ClaimNumber.TryParse(value, out var number).ShouldBeTrue();
        number.ShouldBe(ClaimNumber.Create(2026, 142));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CLM-2026-142")]
    [InlineData("CLM-2026-0000000")]
    [InlineData("CLM-1999-0000001")]
    [InlineData("meridian")]
    public void TryParse_returns_false_instead_of_throwing(string? value)
    {
        ClaimNumber.TryParse(value, out var number).ShouldBeFalse();
        number.ShouldBeNull();
    }
}
