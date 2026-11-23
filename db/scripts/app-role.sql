-- Least-privilege database role for the Claims API (P2.5 task 17f).
-- Idempotent; run as the database owner/admin AFTER the migrations. No GO separators, so it can be
-- run by sqlcmd or a plain SqlCommand (the integration tests do exactly that).
--
-- The API gets data access only: no DDL on dbo, and the audit log is append-only at the permission
-- level too (BR-A-01, S14.2), on top of the EF guard and the INSTEAD OF trigger.

IF DATABASE_PRINCIPAL_ID(N'claims_app') IS NULL
    CREATE ROLE claims_app AUTHORIZATION dbo;

-- Data access to the application schema (migrations own the schema; the app never changes it).
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO claims_app;

-- Append-only audit log: inserts and reads only.
DENY UPDATE, DELETE ON OBJECT::dbo.ClaimAuditLog TO claims_app;

-- Hangfire creates and upgrades its own tables at startup (D-39), so it gets DDL inside its own
-- schema only. CREATE TABLE is a database-level permission, but creating a table also needs ALTER on
-- the target schema, which the role has for HangFire and not for dbo.
IF SCHEMA_ID(N'HangFire') IS NULL
    EXEC(N'CREATE SCHEMA HangFire AUTHORIZATION dbo');
GRANT CREATE TABLE TO claims_app;
GRANT ALTER, SELECT, INSERT, UPDATE, DELETE, EXECUTE, REFERENCES ON SCHEMA::HangFire TO claims_app;
