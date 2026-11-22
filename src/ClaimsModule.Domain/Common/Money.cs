using System.Globalization;

namespace ClaimsModule.Domain.Common;

/// <summary>
/// Monetary amount in the single system currency (USD, see the ai-log assumptions). Precision matches
/// DECIMAL(19,4) (S15.1): at most 4 decimal places and at most 15 integer digits.
/// The sign is allowed; whether a negative value is valid depends on context (BR-R-01).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const int Scale = 4;
    public const string CurrencyCode = "USD";

    /// <summary>The largest absolute value that fits DECIMAL(19,4).</summary>
    public static readonly decimal MaxAbsoluteAmount = 999_999_999_999_999.9999m;

    private Money(decimal amount) => Amount = amount;

    public static Money Zero => default;

    public decimal Amount { get; }

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Of(decimal amount)
    {
        if (decimal.Round(amount, Scale) != amount)
        {
            throw new DomainRuleViolationException(
                "AMOUNT_PRECISION",
                $"Amount must have at most {Scale} decimal places.",
                "Amount");
        }

        if (Math.Abs(amount) > MaxAbsoluteAmount)
        {
            throw new DomainRuleViolationException("AMOUNT_OUT_OF_RANGE", "Amount is out of range.", "Amount");
        }

        return new Money(amount);
    }

    public Money Abs() => new(Math.Abs(Amount));

    public Money Negate() => new(-Amount);

    public Money Add(Money other) => Of(Amount + other.Amount);

    public Money Subtract(Money other) => Of(Amount - other.Amount);

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() => Amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;
}
