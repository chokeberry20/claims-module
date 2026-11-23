using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Domain.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClaimsModule.Persistence.Services;

/// <summary>
/// Gapless, atomic claim numbers (S5.3, BR-C-04, D-14). There is one counter row per
/// (organisation, year), incremented by a single MERGE … OUTPUT under HOLDLOCK:
/// <list type="bullet">
/// <item><b>Atomic:</b> the key-range lock serialises concurrent FNOL submissions, including the
/// first number of a new year, so no two claims get the same number.</item>
/// <item><b>Gapless:</b> the increment joins the caller's transaction. If claim creation rolls back,
/// the increment rolls back with it. A SQL SEQUENCE can't guarantee this.</item>
/// <item><b>No reuse:</b> counters only increase, and soft-deleted claims keep their number.</item>
/// </list>
/// The cost is that FNOL creations for one organisation are serialised until commit, which is
/// acceptable at FNOL volumes.
/// </summary>
internal sealed class ClaimNumberGenerator(ClaimsDbContext context, ITenantProvider tenantProvider) : IClaimNumberGenerator
{
    private const string NextValueSql = """
        MERGE [dbo].[ClaimNumberSequences] WITH (HOLDLOCK) AS target
        USING (SELECT @OrganisationId AS OrganisationId, @Year AS [Year]) AS source
            ON target.[OrganisationId] = source.[OrganisationId] AND target.[Year] = source.[Year]
        WHEN MATCHED THEN
            UPDATE SET [LastValue] = target.[LastValue] + 1
        WHEN NOT MATCHED THEN
            INSERT ([OrganisationId], [Year], [LastValue]) VALUES (source.[OrganisationId], source.[Year], 1)
        OUTPUT inserted.[LastValue];
        """;

    public async Task<ClaimNumber> NextAsync(int year, CancellationToken cancellationToken = default)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Claim numbers must be allocated inside a database transaction, so that a rollback releases the number (D-14).");

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = NextValueSql;
        AddParameter(command, "@OrganisationId", tenantProvider.OrganisationId);
        AddParameter(command, "@Year", year);

        var value = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Claim number allocation returned no value.");

        return ClaimNumber.Create(year, Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
