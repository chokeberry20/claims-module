# Database scripts

`migrate-and-seed.sql` is the **idempotent** script for all EF Core migrations: schema, reference-data seed, compatibility level and the audit-log trigger (D-40). It is generated, never hand-edited:

```bash
dotnet ef migrations script --idempotent \
  --project src/ClaimsModule.Persistence --startup-project src/ClaimsModule.Persistence \
  --output db/scripts/migrate-and-seed.sql
```

You can run it any number of times against an empty or partly migrated database.

**Running it with `sqlcmd`:** pass **`-I`** (QUOTED_IDENTIFIER ON). `sqlcmd` defaults to OFF, and the filtered unique index on `ClaimReserveComponents (ClaimId, Component) WHERE IsDeleted = 0` can't be created without it (Msg 1934). SSMS, Azure Data Studio and ADO.NET (EF, migration bundles) default to ON.

```bash
sqlcmd -S .\SQLEXPRESS01 -E -C -b -I -d ClaimsModule -i db/scripts/migrate-and-seed.sql
```

The normal ways to migrate are `dotnet ef database update` locally and the EF migration bundle in CI/CD. This script is the reviewable, tool-free alternative.
