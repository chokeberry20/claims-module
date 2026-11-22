using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.ReferenceData;
using ClaimsModule.Domain.Tests.Claims;
using static ClaimsModule.Domain.Tests.TestData;

namespace ClaimsModule.Domain.Tests.ReferenceData;

public sealed class ReferenceDataTests
{
    private static Policy Harborview() => Policy.Create(
        Guid.NewGuid(), "POL-2024-001002", "Harborview Properties Inc",
        new DateOnly(2024, 6, 1), new DateOnly(2026, 5, 31), ["Property", "Liability", "Property"]);

    [Theory]
    [InlineData("2026-05-31", PolicyStatus.Active)]
    [InlineData("2026-06-01", PolicyStatus.Expired)]
    public void D_34_policy_status_is_computed_from_the_date(string today, PolicyStatus expected) =>
        Harborview().StatusOn(DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void Cancelled_policy_is_cancelled_regardless_of_dates() =>
        Policy.Create(Guid.NewGuid(), "P", "C", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), [], isCancelled: true)
            .StatusOn(new DateOnly(2026, 6, 1)).ShouldBe(PolicyStatus.Cancelled);

    [Fact]
    public void Coverage_types_are_trimmed_and_distinct_and_snapshot_carries_display_fields()
    {
        var policy = Harborview();

        policy.CoverageTypes.ShouldBe(["Property", "Liability"]);
        policy.ToSnapshot().ShouldBe(new ClaimsModule.Domain.Claims.PolicySnapshot(policy.Id, "POL-2024-001002", "Harborview Properties Inc"));
        policy.Period.Contains(new DateOnly(2024, 6, 1)).ShouldBeTrue();
    }

    [Fact]
    public void Policy_rejects_expiration_before_effective() =>
        Should.Throw<ArgumentException>(() =>
            Policy.Create(Guid.NewGuid(), "P", "C", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1), []));

    [Fact]
    public void Cause_of_loss_code_is_trimmed() =>
        CauseOfLossCode.Create(Guid.NewGuid(), " COL-FIRE ", " Fire ", PerilCategory.Property, true, 10).Code.ShouldBe("COL-FIRE");

    // ------------------------------------------------------------------ documents

    private static ClaimDocument Register(long size, string name = "police-report.pdf") => ClaimDocument.Register(
        SequentialGuid.NewGuid(), Guid.NewGuid(), "PoliceReport", name, "org/claim/id-police-report.pdf", "application/pdf", size, null, Handler1, Now);

    [Fact]
    public void Document_registration_raises_DOCUMENT_UPLOADED_event()
    {
        var doc = Register(1024);

        doc.UploadedAt.ShouldBe(Now);
        doc.UploadedByUserId.ShouldBe(Handler1.UserId);
        doc.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimDocumentUploaded>().DocumentId.ShouldBe(doc.Id);
    }

    [Theory]
    [InlineData(0, "DOCUMENT_EMPTY")]
    [InlineData(ClaimDocument.MaxFileSizeBytes + 1, "DOCUMENT_TOO_LARGE")]
    public void Document_size_must_be_between_one_byte_and_50MB(long size, string code) =>
        Should.Throw<DomainRuleViolationException>(() => Register(size)).Violations.ShouldHaveSingleItem().Code.ShouldBe(code);

    [Fact]
    public void Exactly_50MB_is_accepted() => Should.NotThrow(() => Register(ClaimDocument.MaxFileSizeBytes));

    // ------------------------------------------------------------------ claim reserve activity

    [Fact]
    public void Reserve_activity_updates_a_persisted_timestamp_so_the_claim_row_version_changes()
    {
        var claim = ClaimFactory.NewDraft();

        claim.RecordReserveActivity(Now);

        claim.LastReserveActivityAt.ShouldBe(Now);
    }
}
