using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Tests.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("10000.0000")]
    [InlineData("-8000.5")]
    [InlineData("0.0001")]
    [InlineData("999999999999999.9999")]
    public void Accepts_values_that_fit_decimal_19_4(string value) =>
        Money.Of(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).Amount
            .ShouldBe(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Rejects_more_than_four_decimal_places()
    {
        var ex = Should.Throw<DomainRuleViolationException>(() => Money.Of(1.00001m));

        ex.Contains("AMOUNT_PRECISION").ShouldBeTrue();
    }

    [Fact]
    public void Rejects_values_beyond_decimal_19_4_range() =>
        Should.Throw<DomainRuleViolationException>(() => Money.Of(1_000_000_000_000_000m))
            .Contains("AMOUNT_OUT_OF_RANGE").ShouldBeTrue();

    [Fact]
    public void Arithmetic_and_comparison_work_on_the_amount()
    {
        var a = Money.Of(25_000m);
        var b = Money.Of(5_000m);

        (a + b).ShouldBe(Money.Of(30_000m));
        (b - a).ShouldBe(Money.Of(-20_000m));
        (-a).Abs().ShouldBe(a);
        (a > b).ShouldBeTrue();
        (b <= a).ShouldBeTrue();
        Money.Zero.IsZero.ShouldBeTrue();
        Money.Of(-1m).IsNegative.ShouldBeTrue();
    }

    [Fact]
    public void Formats_as_usd() => Money.Of(10_000m).ToString().ShouldBe("$10,000.00");
}
