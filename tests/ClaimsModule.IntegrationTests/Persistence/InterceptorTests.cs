using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Conventions;
using ClaimsModule.Persistence.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.IntegrationTests.Persistence;

/// <summary>Audit columns, soft delete, tenant isolation and append-only guards on a real database (S14.2, S15.1).</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class InterceptorTests(SqlServerDatabaseFixture db)
{
    private readonly PersistenceHarness _h = new(db);

    private async Task<Claim> SaveNewClaimAsync()
    {
        var claim = _h.NewClaim();
        await using var context = _h.NewContext();
        context.Claims.Add(claim);
        await context.SaveChangesAsync();
        return claim;
    }

    private async Task<(DateTimeOffset? UpdatedAt, byte[] RowVer, Guid? UserModified)> ClaimAuditColumnsAsync(Guid claimId)
    {
        await using var context = db.CreateContext();
        var row = await context.Claims.IgnoreQueryFilters()
            .Where(c => c.Id == claimId)
            .Select(c => new
            {
                UpdatedAt = EF.Property<DateTimeOffset?>(c, ShadowProperties.UpdatedAt),
                RowVer = EF.Property<byte[]>(c, ShadowProperties.RowVersion),
                UserModified = EF.Property<Guid?>(c, ShadowProperties.UserModified),
            })
            .SingleAsync();
        return (row.UpdatedAt, row.RowVer, row.UserModified);
    }

    [Fact]
    public async Task Insert_stamps_tenant_created_at_and_creating_user_on_the_root_and_children()
    {
        var claim = await SaveNewClaimAsync();

        await using var context = db.CreateContext();
        var party = await context.Set<ClaimParty>().Where(p => p.ClaimId == claim.Id)
            .Select(p => new
            {
                Org = EF.Property<Guid>(p, ShadowProperties.OrganisationId),
                CreatedAt = EF.Property<DateTimeOffset>(p, ShadowProperties.CreatedAt),
                UserCreated = EF.Property<Guid?>(p, ShadowProperties.UserCreated),
                UpdatedAt = EF.Property<DateTimeOffset?>(p, ShadowProperties.UpdatedAt),
            })
            .SingleAsync();

        party.Org.ShouldBe(SqlServerDatabaseFixture.OrganisationId);
        party.CreatedAt.ShouldBe(_h.Clock.Now);
        party.UserCreated.ShouldBe(PersistenceHarness.Handler.UserId);
        party.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Update_stamps_updated_at_and_modifying_user_and_keeps_created_columns()
    {
        var claim = await SaveNewClaimAsync();
        _h.Clock.Now = _h.Clock.Now.AddHours(2);
        _h.User.Actor = PersistenceHarness.Supervisor;

        await using (var context = _h.NewContext())
        {
            var loaded = await context.Claims.SingleAsync(c => c.Id == claim.Id);
            loaded.UpdateNotes("Called claimant.", PersistenceHarness.Supervisor, _h.Clock.Now);
            await context.SaveChangesAsync();
        }

        var (updatedAt, _, userModified) = await ClaimAuditColumnsAsync(claim.Id);
        updatedAt.ShouldBe(_h.Clock.Now);
        userModified.ShouldBe(PersistenceHarness.Supervisor.UserId);
        (await db.ScalarAsync<DateTimeOffset>($"SELECT CreatedAt FROM Claims WHERE ClaimId = '{claim.Id}'"))
            .ShouldBe(_h.Clock.Now.AddHours(-2));
    }

    [Fact]
    public async Task Changing_only_a_child_stamps_the_claim_root_and_bumps_its_row_version()
    {
        var claim = await SaveNewClaimAsync();
        var before = await ClaimAuditColumnsAsync(claim.Id);
        _h.Clock.Now = _h.Clock.Now.AddHours(1);

        await using (var context = _h.NewContext())
        {
            var loaded = await context.Claims.Include(c => c.Parties).SingleAsync(c => c.Id == claim.Id);
            loaded.AddParty(new PartyDetails(PartyRole.Witness, PartyType.Person, "John", "Smith"), PersistenceHarness.Handler, _h.Clock.Now);
            await context.SaveChangesAsync();
        }

        var after = await ClaimAuditColumnsAsync(claim.Id);
        before.UpdatedAt.ShouldBeNull();
        after.UpdatedAt.ShouldBe(_h.Clock.Now);
        after.RowVer.ShouldNotBe(before.RowVer);
    }

    [Fact]
    public async Task D_13_recording_an_sla_breach_does_not_count_as_activity()
    {
        var claim = await SaveNewClaimAsync();
        _h.Clock.Now = _h.Clock.Now.AddDays(3);

        await using (var context = _h.NewContext())
        {
            var loaded = await context.Claims.SingleAsync(c => c.Id == claim.Id);
            loaded.TryRecordSlaBreach(_h.Clock.Now.AddDays(-3), _h.Clock.Now).ShouldBeTrue();
            await context.SaveChangesAsync();
        }

        (await ClaimAuditColumnsAsync(claim.Id)).UpdatedAt.ShouldBeNull();
        (await db.ScalarAsync<bool>($"SELECT IsSlaBreached FROM Claims WHERE ClaimId = '{claim.Id}'")).ShouldBeTrue();
    }

    [Fact]
    public async Task Removed_risk_object_is_soft_deleted_and_hidden_by_the_global_filter()
    {
        var claim = await SaveNewClaimAsync();
        var riskObjectId = claim.RiskObjects.Single().Id;

        await using (var context = _h.NewContext())
        {
            var loaded = await context.Claims.Include(c => c.RiskObjects).SingleAsync(c => c.Id == claim.Id);
            loaded.RemoveRiskObject(riskObjectId, PersistenceHarness.Handler, _h.Clock.Now);
            await context.SaveChangesAsync();
        }

        (await db.ScalarAsync<bool>($"SELECT IsDeleted FROM ClaimRiskObjects WHERE ClaimRiskObjectId = '{riskObjectId}'")).ShouldBeTrue();
        (await db.ScalarAsync<DateTimeOffset>($"SELECT DeletedAt FROM ClaimRiskObjects WHERE ClaimRiskObjectId = '{riskObjectId}'")).ShouldBe(_h.Clock.Now);

        await using var reader = db.CreateContext();
        (await reader.Claims.Include(c => c.RiskObjects).SingleAsync(c => c.Id == claim.Id)).RiskObjects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Other_tenant_cannot_see_the_claim_and_tenant_cannot_be_changed()
    {
        var claim = await SaveNewClaimAsync();

        await using (var otherTenant = db.CreateContext(Guid.NewGuid()))
        {
            (await otherTenant.Claims.AnyAsync(c => c.Id == claim.Id)).ShouldBeFalse();
        }

        await using var context = _h.NewContext();
        var loaded = await context.Claims.SingleAsync(c => c.Id == claim.Id);
        context.Entry(loaded).Property(ShadowProperties.OrganisationId).CurrentValue = Guid.NewGuid();

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ append-only (BR-A-01, D-22)

    private async Task<ClaimAuditLog> SaveAuditEntryAsync()
    {
        var claim = await SaveNewClaimAsync();
        var entry = new ClaimAuditLog
        {
            AuditLogId = SequentialGuid.NewGuid(),
            OrganisationId = SqlServerDatabaseFixture.OrganisationId,
            ClaimId = claim.Id,
            EventType = "CLAIM_CREATED",
            Description = "Claim created.",
            CreatedAt = _h.Clock.Now,
        };
        await using var context = _h.NewContext();
        context.AuditLog.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task Audit_entry_cannot_be_modified_or_deleted_through_ef()
    {
        var saved = await SaveAuditEntryAsync();

        await using (var context = _h.NewContext())
        {
            var entry = await context.AuditLog.SingleAsync(a => a.AuditLogId == saved.AuditLogId);
            context.Entry(entry).Property(a => a.Description).CurrentValue = "tampered";
            (await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync())).Message.ShouldContain("append-only");
        }

        await using (var context = _h.NewContext())
        {
            context.AuditLog.Remove(await context.AuditLog.SingleAsync(a => a.AuditLogId == saved.AuditLogId));
            await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("UPDATE ClaimAuditLog SET Description = 'tampered' WHERE AuditLogId = '{0}'")]
    [InlineData("DELETE FROM ClaimAuditLog WHERE AuditLogId = '{0}'")]
    public async Task Database_trigger_blocks_raw_sql_update_and_delete(string sqlTemplate)
    {
        var saved = await SaveAuditEntryAsync();

        var ex = await Should.ThrowAsync<SqlException>(() => db.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, sqlTemplate, saved.AuditLogId)));

        ex.Number.ShouldBe(51000);
        (await db.ScalarAsync<string>($"SELECT Description FROM ClaimAuditLog WHERE AuditLogId = '{saved.AuditLogId}'")).ShouldBe("Claim created.");
    }

    [Fact]
    public async Task Event_sourced_reserve_fields_cannot_change_after_insert()
    {
        var claim = await SaveNewClaimAsync();
        var component = ClaimReserveComponent.Create(claim.Id, ReserveComponentType.Indemnity);
        var txn = component.Submit(ReserveTransactionType.Add, 5_000m, "Initial reserve", PersistenceHarness.Handler, Money.Zero, _h.Clock.Now).Transaction;
        await using (var context = _h.NewContext())
        {
            context.ReserveComponents.Add(component);
            await context.SaveChangesAsync();
        }

        await using var tamper = _h.NewContext();
        var loaded = await tamper.ReserveTransactions.SingleAsync(t => t.Id == txn.Id);
        tamper.Entry(loaded).Property(t => t.Amount).CurrentValue = Money.Of(9_999m);

        (await Should.ThrowAsync<InvalidOperationException>(() => tamper.SaveChangesAsync())).Message.ShouldContain("Amount");
    }
}
