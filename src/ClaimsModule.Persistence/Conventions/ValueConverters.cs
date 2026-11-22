using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClaimsModule.Persistence.Conventions;

/// <summary><see cref="Money"/> ↔ DECIMAL(19,4) (S15.1). Precision is set by convention in ClaimsDbContext.</summary>
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(m => m.Amount, d => Money.Of(d));

/// <summary><see cref="ClaimNumber"/> ↔ NVARCHAR(50).</summary>
internal sealed class ClaimNumberConverter() : ValueConverter<ClaimNumber, string>(n => n.Value, s => ClaimNumber.Parse(s));
