IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [CauseOfLossCodes] (
        [CauseOfLossCodeId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(255) NOT NULL,
        [PerilCategory] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(0 AS bit),
        [SortOrder] int NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_CauseOfLossCodes] PRIMARY KEY ([CauseOfLossCodeId]),
        CONSTRAINT [AK_CauseOfLossCodes_OrganisationId_Code] UNIQUE ([OrganisationId], [Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimNumberSequences] (
        [OrganisationId] uniqueidentifier NOT NULL,
        [Year] int NOT NULL,
        [LastValue] int NOT NULL,
        CONSTRAINT [PK_ClaimNumberSequences] PRIMARY KEY ([OrganisationId], [Year])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimStatusTransitions] (
        [ClaimStatusTransitionId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [FromStatus] nvarchar(50) NOT NULL,
        [ToStatus] nvarchar(50) NOT NULL,
        [RequiredPermission] nvarchar(50) NOT NULL,
        [RequiresReason] bit NOT NULL DEFAULT CAST(0 AS bit),
        [Description] nvarchar(255) NOT NULL,
        CONSTRAINT [PK_ClaimStatusTransitions] PRIMARY KEY ([ClaimStatusTransitionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [IdempotencyRecords] (
        [IdempotencyRecordId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [IdempotencyKey] nvarchar(200) NOT NULL,
        [RequestMethod] nvarchar(10) NOT NULL,
        [RequestPath] nvarchar(500) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [ResponseStatusCode] int NOT NULL,
        [ResponseBody] nvarchar(max) NULL,
        [ResponseContentType] nvarchar(100) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [ExpiresAt] datetimeoffset(7) NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([IdempotencyRecordId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [Policies] (
        [PolicyId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [PolicyNumber] nvarchar(50) NOT NULL,
        [ClientName] nvarchar(255) NOT NULL,
        [EffectiveDate] date NOT NULL,
        [ExpirationDate] date NOT NULL,
        [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        [CoverageTypes] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Policies] PRIMARY KEY ([PolicyId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [Claims] (
        [ClaimId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimNumber] nvarchar(50) NOT NULL,
        [PolicyId] uniqueidentifier NULL,
        [PolicyNumber] nvarchar(50) NULL,
        [ClientName] nvarchar(255) NULL,
        [Status] nvarchar(50) NOT NULL,
        [Severity] nvarchar(50) NOT NULL,
        [ClaimType] nvarchar(50) NOT NULL,
        [ReportedDate] datetimeoffset(7) NOT NULL,
        [AssignedHandlerId] uniqueidentifier NULL,
        [AssignedHandlerName] nvarchar(255) NULL,
        [ClosedAt] datetimeoffset(7) NULL,
        [ClosureReason] nvarchar(500) NULL,
        [Notes] nvarchar(max) NULL,
        [ReserveLimitOverride] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ReserveLimitOverrideSetByUserId] uniqueidentifier NULL,
        [ReserveLimitOverrideSetAt] datetimeoffset(7) NULL,
        [ReopenedAt] datetimeoffset(7) NULL,
        [ReopenCount] int NOT NULL,
        [IsSlaBreached] bit NOT NULL DEFAULT CAST(0 AS bit),
        [LastSlaBreachAt] datetimeoffset(7) NULL,
        [LastReserveActivityAt] datetimeoffset(7) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [RowVer] rowversion NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_Claims] PRIMARY KEY ([ClaimId]),
        CONSTRAINT [FK_Claims_Policies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [Policies] ([PolicyId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimAuditLog] (
        [AuditLogId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [OrganisationId] uniqueidentifier NOT NULL,
        [ClaimId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [OldValue] nvarchar(max) NULL,
        [NewValue] nvarchar(max) NULL,
        [RelatedEntityId] uniqueidentifier NULL,
        [RelatedEntityType] nvarchar(100) NULL,
        [CorrelationId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimAuditLog] PRIMARY KEY ([AuditLogId]),
        CONSTRAINT [FK_ClaimAuditLog_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimDocuments] (
        [ClaimDocumentId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [DocumentType] nvarchar(100) NOT NULL,
        [DocumentName] nvarchar(255) NOT NULL,
        [BlobPath] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileSizeBytes] bigint NOT NULL,
        [UploadedAt] datetimeoffset(7) NOT NULL,
        [UploadedByUserId] uniqueidentifier NOT NULL,
        [UploadedByName] nvarchar(255) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimDocuments] PRIMARY KEY ([ClaimDocumentId]),
        CONSTRAINT [FK_ClaimDocuments_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimParties] (
        [ClaimPartyId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [PartyRole] nvarchar(50) NOT NULL,
        [PartyType] nvarchar(20) NOT NULL,
        [FirstName] nvarchar(100) NULL,
        [LastName] nvarchar(100) NULL,
        [CompanyName] nvarchar(255) NULL,
        [Email] nvarchar(255) NULL,
        [Phone] nvarchar(50) NULL,
        [Notes] nvarchar(max) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(0 AS bit),
        [RemovedAt] datetimeoffset(7) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimParties] PRIMARY KEY ([ClaimPartyId]),
        CONSTRAINT [FK_ClaimParties_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimReserveComponents] (
        [ReserveComponentId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [Component] nvarchar(50) NOT NULL,
        [CurrentAmount] decimal(19,4) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [LastChangeSequence] int NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [RowVer] rowversion NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimReserveComponents] PRIMARY KEY ([ReserveComponentId]),
        CONSTRAINT [FK_ClaimReserveComponents_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimRiskObjects] (
        [ClaimRiskObjectId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [AssetType] nvarchar(50) NOT NULL,
        [AssetDescription] nvarchar(500) NOT NULL,
        [DamageDescription] nvarchar(max) NULL,
        [AssetReference] nvarchar(255) NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimRiskObjects] PRIMARY KEY ([ClaimRiskObjectId]),
        CONSTRAINT [FK_ClaimRiskObjects_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ClaimValidationIssues] (
        [ValidationIssueId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Severity] nvarchar(50) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [Field] nvarchar(100) NULL,
        [Status] nvarchar(50) NOT NULL,
        [RaisedAt] datetimeoffset(7) NOT NULL,
        [ResolvedAt] datetimeoffset(7) NULL,
        [AcknowledgedByUserId] uniqueidentifier NULL,
        [AcknowledgedAt] datetimeoffset(7) NULL,
        [WaivedByUserId] uniqueidentifier NULL,
        [WaivedAt] datetimeoffset(7) NULL,
        [WaiverReason] nvarchar(500) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimValidationIssues] PRIMARY KEY ([ValidationIssueId]),
        CONSTRAINT [FK_ClaimValidationIssues_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [LossEvents] (
        [LossEventId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [LossDate] datetimeoffset(7) NOT NULL,
        [LossDescription] nvarchar(max) NOT NULL,
        [LossLocation] nvarchar(500) NULL,
        [CauseOfLossCode] nvarchar(50) NOT NULL,
        [EstimatedLossAmount] decimal(19,4) NULL,
        [ReportDate] datetimeoffset(7) NOT NULL,
        [PoliceReportNumber] nvarchar(100) NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_LossEvents] PRIMARY KEY ([LossEventId]),
        CONSTRAINT [FK_LossEvents_CauseOfLossCodes_OrganisationId_CauseOfLossCode] FOREIGN KEY ([OrganisationId], [CauseOfLossCode]) REFERENCES [CauseOfLossCodes] ([OrganisationId], [Code]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LossEvents_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE TABLE [ReserveHistory] (
        [ReserveHistoryId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ReserveComponentId] uniqueidentifier NOT NULL,
        [ClaimId] uniqueidentifier NOT NULL,
        [Component] nvarchar(50) NOT NULL,
        [TransactionType] nvarchar(50) NOT NULL,
        [Amount] decimal(19,4) NOT NULL,
        [PreviousBalance] decimal(19,4) NOT NULL,
        [NewBalance] decimal(19,4) NOT NULL,
        [ApprovalStatus] nvarchar(50) NOT NULL,
        [RequiredApprovalLevel] nvarchar(50) NOT NULL,
        [RequiresLimitOverride] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ApprovedByUserId] uniqueidentifier NULL,
        [ApprovedAt] datetimeoffset(7) NULL,
        [RejectedByUserId] uniqueidentifier NULL,
        [RejectedAt] datetimeoffset(7) NULL,
        [RejectionReason] nvarchar(500) NULL,
        [CancelledAt] datetimeoffset(7) NULL,
        [ChangeReason] nvarchar(500) NOT NULL,
        [PostingStatus] nvarchar(50) NOT NULL,
        [PostingJobId] nvarchar(100) NULL,
        [PostedAt] datetimeoffset(7) NULL,
        [PostingFailureReason] nvarchar(max) NULL,
        [IdempotencyKey] nvarchar(200) NOT NULL,
        [ChangeSequence] int NOT NULL,
        [SubmittedByUserId] uniqueidentifier NOT NULL,
        [SubmittedByName] nvarchar(255) NOT NULL,
        [SubmittedAt] datetimeoffset(7) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [DeletedAt] datetimeoffset(7) NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [UpdatedAt] datetimeoffset(7) NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ReserveHistory] PRIMARY KEY ([ReserveHistoryId]),
        CONSTRAINT [FK_ReserveHistory_ClaimReserveComponents_ReserveComponentId] FOREIGN KEY ([ReserveComponentId]) REFERENCES [ClaimReserveComponents] ([ReserveComponentId]),
        CONSTRAINT [FK_ReserveHistory_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_CauseOfLossCodes_OrganisationId] ON [CauseOfLossCodes] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_ClaimId_CreatedAt] ON [ClaimAuditLog] ([ClaimId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_ClaimId_EventType_CreatedAt] ON [ClaimAuditLog] ([ClaimId], [EventType], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_OrganisationId] ON [ClaimAuditLog] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimDocuments_ClaimId] ON [ClaimDocuments] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimDocuments_OrganisationId] ON [ClaimDocuments] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimParties_ClaimId_PartyRole] ON [ClaimParties] ([ClaimId], [PartyRole]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimParties_OrganisationId] ON [ClaimParties] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ClaimReserveComponents_ClaimId_Component] ON [ClaimReserveComponents] ([ClaimId], [Component]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimReserveComponents_OrganisationId] ON [ClaimReserveComponents] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimRiskObjects_ClaimId] ON [ClaimRiskObjects] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimRiskObjects_OrganisationId] ON [ClaimRiskObjects] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Claims_AssignedHandlerId] ON [Claims] ([AssignedHandlerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Claims_OrganisationId] ON [Claims] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Claims_OrganisationId_ClaimNumber] ON [Claims] ([OrganisationId], [ClaimNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Claims_PolicyId] ON [Claims] ([PolicyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Claims_ReportedDate] ON [Claims] ([ReportedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Claims_Status] ON [Claims] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClaimStatusTransitions_FromStatus_ToStatus] ON [ClaimStatusTransitions] ([FromStatus], [ToStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimValidationIssues_ClaimId_Code] ON [ClaimValidationIssues] ([ClaimId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ClaimValidationIssues_OrganisationId] ON [ClaimValidationIssues] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_IdempotencyRecords_ExpiresAt] ON [IdempotencyRecords] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_IdempotencyRecords_OrganisationId_UserId_IdempotencyKey] ON [IdempotencyRecords] ([OrganisationId], [UserId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LossEvents_ClaimId] ON [LossEvents] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_LossEvents_LossDate] ON [LossEvents] ([LossDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_LossEvents_OrganisationId] ON [LossEvents] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_LossEvents_OrganisationId_CauseOfLossCode] ON [LossEvents] ([OrganisationId], [CauseOfLossCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Policies_ClientName] ON [Policies] ([ClientName]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_Policies_OrganisationId] ON [Policies] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Policies_OrganisationId_PolicyNumber] ON [Policies] ([OrganisationId], [PolicyNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_ApprovalStatus] ON [ReserveHistory] ([ApprovalStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_ClaimId] ON [ReserveHistory] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReserveHistory_IdempotencyKey] ON [ReserveHistory] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_OrganisationId] ON [ReserveHistory] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_PostingStatus] ON [ReserveHistory] ([PostingStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReserveHistory_ReserveComponentId_ChangeSequence] ON [ReserveHistory] ([ReserveComponentId], [ChangeSequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234536_Initial'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122234536_Initial', N'9.0.23');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] ON;
    EXEC(N'INSERT INTO [CauseOfLossCodes] ([CauseOfLossCodeId], [Code], [CreatedAt], [DeletedAt], [IsActive], [Name], [OrganisationId], [PerilCategory], [SortOrder], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''0e0b415b-a154-8104-b800-f6d8a05f0ae6'', N''COL-WIND'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Wind / Storm'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Weather'', 80, NULL, NULL, NULL),
    (''383fac17-4a98-8fc6-a29b-693cc88cdeb4'', N''COL-VEH-COL'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Vehicle Collision'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Auto'', 40, NULL, NULL, NULL),
    (''3bc61cd4-c73d-8b59-957d-53faa0398ddc'', N''COL-LIAB'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Third Party Liability'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Liability'', 60, NULL, NULL, NULL),
    (''3c9af9e4-008e-8d32-8fd5-860cf423fcbd'', N''COL-THEFT'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Theft'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Crime'', 30, NULL, NULL, NULL),
    (''46549c37-d254-8dd5-8a49-ce73eac26c88'', N''COL-FLOOD'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Flood'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Weather'', 20, NULL, NULL, NULL),
    (''867bd163-8091-8c53-8aa4-1ab764733da9'', N''COL-EQUIP'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Equipment Breakdown'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Equipment'', 70, NULL, NULL, NULL),
    (''a203b2b1-fe5d-8135-a92b-e71d6571ac45'', N''COL-INJURY'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Bodily Injury'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Liability'', 90, NULL, NULL, NULL),
    (''aba0e4f8-34d3-8171-9a31-f6b9c5a1889f'', N''COL-FIRE'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Fire'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Property'', 10, NULL, NULL, NULL),
    (''b1fb919c-f42d-8cbc-a910-ba8a9a9b2534'', N''COL-OTHER'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Other / Unknown'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''General'', 100, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] ON;
    EXEC(N'INSERT INTO [CauseOfLossCodes] ([CauseOfLossCodeId], [Code], [CreatedAt], [DeletedAt], [Name], [OrganisationId], [PerilCategory], [SortOrder], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''c1752185-79d8-8d29-9a17-4e2b0e3c31a2'', N''COL-LEGACY-SUBS'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Subsidence (legacy, retired)'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Property'', 900, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] ON;
    EXEC(N'INSERT INTO [CauseOfLossCodes] ([CauseOfLossCodeId], [Code], [CreatedAt], [DeletedAt], [IsActive], [Name], [OrganisationId], [PerilCategory], [SortOrder], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''e54f4051-c2ce-8202-89ab-02206b25b815'', N''COL-VEH-COMP'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Vehicle Comprehensive'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''Auto'', 50, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [ToStatus])
    VALUES (''1cb480b3-fdf6-8919-8f9d-a3370950ebb6'', N''No unresolved or unwaived Critical issues, all Warnings acknowledged, at least one Claimant, handler assigned'', N''Draft'', N''Handler'', N''Open''),
    (''2fb9998a-47cd-8204-802a-83104409cb6e'', N''At least one approved reserve transaction'', N''Open'', N''Handler'', N''PendingPayment''),
    (''36c74ad0-00b4-8f6e-b601-d814c2cda8ec'', N''Handler manually reverts'', N''UnderInvestigation'', N''Handler'', N''Open'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [RequiresReason], [ToStatus])
    VALUES (''44b876d3-c64e-87d7-ac43-06f73771eb5d'', N''Reopen reason provided; Supervisor role required'', N''Closed'', N''Supervisor'', CAST(1 AS bit), N''Reopened'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [ToStatus])
    VALUES (''48ac7583-674f-844f-97ed-c7492afd1a0a'', N''At least one approved reserve transaction'', N''UnderInvestigation'', N''Handler'', N''PendingPayment''),
    (''5e2b8ca4-d26a-8233-bfc2-7ff6973cc4aa'', N''Immediately on reopen'', N''Reopened'', N''Supervisor'', N''Open'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [RequiresReason], [ToStatus])
    VALUES (''5f16c8d2-ac9b-84c1-87c3-e50e02f6a3aa'', N''Withdrawal reason provided'', N''UnderInvestigation'', N''Handler'', CAST(1 AS bit), N''Withdrawn'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [ToStatus])
    VALUES (''8d3a7088-e8a1-8cc9-9130-1b5f7ce34e70'', N''All closure conditions satisfied (CC-01..CC-04)'', N''Open'', N''Handler'', N''Closed'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [RequiresReason], [ToStatus])
    VALUES (''a333b102-bbe7-83e7-b1f3-2ec0ab420b06'', N''Withdrawal reason provided'', N''Open'', N''Handler'', CAST(1 AS bit), N''Withdrawn'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'RequiresReason', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [Description], [FromStatus], [RequiredPermission], [ToStatus])
    VALUES (''ad6d0e3f-d37f-807f-b7b8-c4a4b219ea88'', N''No additional conditions'', N''Open'', N''Handler'', N''UnderInvestigation''),
    (''dca4d7c1-d6c5-8489-b11c-e3d055477025'', N''All closure conditions satisfied (CC-01..CC-04)'', N''PendingPayment'', N''Handler'', N''Closed''),
    (''f0d4a9b9-d8c2-8272-8db5-3ab6dc09cd1b'', N''All closure conditions satisfied (CC-01..CC-04)'', N''UnderInvestigation'', N''Handler'', N''Closed'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'Description', N'FromStatus', N'RequiredPermission', N'ToStatus') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'PolicyId', N'ClientName', N'CreatedAt', N'DeletedAt', N'EffectiveDate', N'ExpirationDate', N'OrganisationId', N'PolicyNumber', N'UpdatedAt', N'UserCreated', N'UserModified', N'CoverageTypes') AND [object_id] = OBJECT_ID(N'[Policies]'))
        SET IDENTITY_INSERT [Policies] ON;
    EXEC(N'INSERT INTO [Policies] ([PolicyId], [ClientName], [CreatedAt], [DeletedAt], [EffectiveDate], [ExpirationDate], [OrganisationId], [PolicyNumber], [UpdatedAt], [UserCreated], [UserModified], [CoverageTypes])
    VALUES (''030b49e8-6eff-8ceb-b232-cbc13593c216'', N''Bluewater Marina Holdings'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2026-03-01'', ''2028-02-29'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2026-003002'', NULL, NULL, NULL, N''["Property","Liability","Equipment"]''),
    (''13b458fc-40ac-87e5-a29d-d96e9cfc4258'', N''Meridian Transport LLC'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2024-01-01'', ''2026-12-31'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2024-001001'', NULL, NULL, NULL, N''["Vehicle","Cargo"]''),
    (''5a2514ef-117c-81cb-a805-f18c2dc1c85a'', N''Stanton Medical Group'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2025-01-01'', ''2026-12-31'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2025-002002'', NULL, NULL, NULL, N''["Liability","Vehicle"]''),
    (''72dd9d26-58c9-8523-80b3-08e511c0864b'', N''Archived Corp'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2020-01-01'', ''2021-12-31'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2023-000099'', NULL, NULL, NULL, N''["Property"]''),
    (''8624ba21-e892-832a-99a0-579cc34e987c'', N''Northwind Logistics Ltd'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2026-01-01'', ''2027-12-31'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2026-003001'', NULL, NULL, NULL, N''["Vehicle","Cargo"]''),
    (''9853294c-0443-85d4-b328-010e41788d89'', N''Coastal Builders Group'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2025-03-01'', ''2027-02-28'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2025-002001'', NULL, NULL, NULL, N''["Property","Equipment"]''),
    (''a13fcfc3-0ea9-8671-bd79-b31715a86394'', N''Harborview Properties Inc'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2024-06-01'', ''2026-05-31'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2024-001002'', NULL, NULL, NULL, N''["Property","Liability"]''),
    (''ddea2f79-7c44-8857-9600-7113b98fb8ae'', N''Summit Engineering Partners'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2025-07-01'', ''2027-06-30'', ''3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64'', N''POL-2025-002010'', NULL, NULL, NULL, N''["Equipment","Property"]'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'PolicyId', N'ClientName', N'CreatedAt', N'DeletedAt', N'EffectiveDate', N'ExpirationDate', N'OrganisationId', N'PolicyNumber', N'UpdatedAt', N'UserCreated', N'UserModified', N'CoverageTypes') AND [object_id] = OBJECT_ID(N'[Policies]'))
        SET IDENTITY_INSERT [Policies] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234834_SeedReferenceData'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122234834_SeedReferenceData', N'9.0.23');
END;

COMMIT;
GO

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122235031_SetCompatibilityLevel160'
)
BEGIN
    ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 160;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122235031_SetCompatibilityLevel160'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122235031_SetCompatibilityLevel160', N'9.0.23');
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122235304_AuditLogAppendOnlyTrigger'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_ClaimAuditLog_AppendOnly]
    ON [dbo].[ClaimAuditLog]
    INSTEAD OF UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        THROW 51000, ''ClaimAuditLog is append-only: UPDATE and DELETE are not permitted (BR-A-01).'', 1;
    END');
END;

IF NOT EXISTS (
    SELECT * FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122235304_AuditLogAppendOnlyTrigger'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122235304_AuditLogAppendOnlyTrigger', N'9.0.23');
END;

COMMIT;
GO

