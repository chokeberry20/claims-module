using ClaimsModule.Domain.Claims;
using ClaimsModule.Domain.Documents;
using ClaimsModule.Domain.Reserves;
using ClaimsModule.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>ClaimReserveComponents (S9.5): an aggregate root with RowVer and one row per (claim, component) (D-46).</summary>
internal sealed class ClaimReserveComponentConfiguration : BusinessEntityConfiguration<ClaimReserveComponent>
{
    protected override string KeyColumnName => "ReserveComponentId";

    protected override string TableName => "ClaimReserveComponents";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimReserveComponent> builder)
    {
        AddRowVersion(builder);

        builder.Property(c => c.ComponentType).HasColumnName("Component").IsRequired();
        builder.Property(c => c.CurrentAmount).IsRequired();
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Notes);
        builder.Property(c => c.LastChangeSequence).IsRequired();

        builder.HasOne<Claim>().WithMany().HasForeignKey(c => c.ClaimId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Transactions).WithOne().HasForeignKey(t => t.ReserveComponentId).OnDelete(DeleteBehavior.ClientCascade);
        builder.Navigation(c => c.Transactions).HasField("_transactions").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.PendingTransaction);
        builder.Ignore(c => c.HasPendingApproval);
        builder.Ignore(c => c.PendingAmount);
        builder.Ignore(c => c.IsOpened);

        builder.HasIndex(nameof(ClaimReserveComponent.ClaimId), nameof(ClaimReserveComponent.ComponentType))
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}

/// <summary>ReserveHistory: the append-only reserve transaction log (S9.6, D-22, D-43).</summary>
internal sealed class ReserveTransactionConfiguration : BusinessEntityConfiguration<ReserveTransaction>
{
    protected override string KeyColumnName => "ReserveHistoryId";

    protected override string TableName => "ReserveHistory";

    protected override void ConfigureEntity(EntityTypeBuilder<ReserveTransaction> builder)
    {
        builder.Property(t => t.ComponentType).HasColumnName("Component").IsRequired();
        builder.Property(t => t.TransactionType).IsRequired();
        builder.Property(t => t.Amount).IsRequired();
        builder.Property(t => t.PreviousBalance).IsRequired();
        builder.Property(t => t.NewBalance).IsRequired();
        builder.Property(t => t.ApprovalStatus).IsRequired();
        builder.Property(t => t.RequiredApprovalLevel).IsRequired();
        builder.Property(t => t.RequiresLimitOverride).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.RejectionReason).HasMaxLength(ReserveTransaction.MaxRejectionReasonLength);
        builder.Property(t => t.ChangeReason).HasMaxLength(ReserveTransaction.MaxChangeReasonLength).IsRequired();
        builder.Property(t => t.PostingStatus).IsRequired();
        builder.Property(t => t.PostingJobId).HasMaxLength(100);
        builder.Property(t => t.PostingFailureReason);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(ReserveTransaction.MaxIdempotencyKeyLength).IsRequired();
        builder.Property(t => t.ChangeSequence).IsRequired();
        builder.Property(t => t.SubmittedByUserId).IsRequired();
        builder.Property(t => t.SubmittedByName).HasMaxLength(255).IsRequired();
        builder.Property(t => t.SubmittedAt).IsRequired();

        // ClaimId is denormalised for queries (S9.6). Restrict keeps SQL Server free of cascade paths.
        builder.HasOne<Claim>().WithMany().HasForeignKey(t => t.ClaimId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(t => t.IsApproved);
        builder.Ignore(t => t.IsPendingApproval);

        // BR-R-06: the idempotency key is unique. The sequence is unique per component (S9.6).
        builder.HasIndex(t => t.IdempotencyKey).IsUnique();
        builder.HasIndex(t => new { t.ReserveComponentId, t.ChangeSequence }).IsUnique();
        builder.HasIndex(t => t.ApprovalStatus);
        builder.HasIndex(t => t.PostingStatus);
    }
}

/// <summary>ClaimDocuments: metadata only. The bytes live in blob storage (S9.7, S13).</summary>
internal sealed class ClaimDocumentConfiguration : BusinessEntityConfiguration<ClaimDocument>
{
    protected override string KeyColumnName => "ClaimDocumentId";

    protected override string TableName => "ClaimDocuments";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimDocument> builder)
    {
        builder.Property(d => d.DocumentType).HasMaxLength(ClaimDocument.MaxDocumentTypeLength).IsRequired();
        builder.Property(d => d.DocumentName).HasMaxLength(ClaimDocument.MaxDocumentNameLength).IsRequired();
        builder.Property(d => d.BlobPath).HasMaxLength(ClaimDocument.MaxBlobPathLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(ClaimDocument.MaxContentTypeLength).IsRequired();
        builder.Property(d => d.FileSizeBytes).IsRequired();
        builder.Property(d => d.UploadedAt).IsRequired();
        builder.Property(d => d.UploadedByUserId).IsRequired();
        builder.Property(d => d.UploadedByName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.Notes).HasMaxLength(ClaimDocument.MaxNotesLength);

        builder.HasOne<Claim>().WithMany().HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(d => d.DomainEvents);
        builder.HasIndex(d => d.ClaimId);
    }
}
