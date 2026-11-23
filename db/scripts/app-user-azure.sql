-- Azure SQL only: map the API's system-assigned managed identity to a contained database user and
-- put it in the least-privilege role from app-role.sql (P2.5 task 17f). Idempotent.
--
-- The user is created from the identity's application (client) ID: WITH SID = <client ID>, TYPE = E.
-- That needs no Microsoft Entra directory lookup. CREATE USER ... FROM EXTERNAL PROVIDER does a
-- lookup, and when a service principal (the GitHub OIDC identity) runs it, the lookup fails with
-- Msg 33134 "Server identity is not configured" unless the SQL server has an identity with the
-- Directory Readers role (first deploy, 2026-11-23).
--
-- Variables:
--   $(AppPrincipalName)  the managed identity's display name = the App Service name (Bicep output apiAppName)
--   $(AppClientId)       the managed identity's client ID (Bicep output apiIdentityClientId)
-- Manual run with go-sqlcmd (-b: stop and exit non-zero on the first error):
--   sqlcmd -b -S <server>.database.windows.net -d ClaimsModule --authentication-method ActiveDirectoryDefault \
--          -v AppPrincipalName=<api-app-name> AppClientId=<client-id> -i db/scripts/app-role.sql -i db/scripts/app-user-azure.sql
-- deploy.yml substitutes both variables with sed and runs the script with azure/sql-action (-b).

IF DATABASE_PRINCIPAL_ID(N'$(AppPrincipalName)') IS NULL
BEGIN
    DECLARE @sid nvarchar(40) = CONVERT(nvarchar(40), CONVERT(varbinary(16), CONVERT(uniqueidentifier, N'$(AppClientId)')), 1);
    EXEC(N'CREATE USER [$(AppPrincipalName)] WITH SID = ' + @sid + N', TYPE = E');
END;

IF IS_ROLEMEMBER(N'claims_app', N'$(AppPrincipalName)') = 0
    ALTER ROLE claims_app ADD MEMBER [$(AppPrincipalName)];

-- Fail loudly if the user is still missing or not in the role (the grant must never pass silently).
IF DATABASE_PRINCIPAL_ID(N'$(AppPrincipalName)') IS NULL OR IS_ROLEMEMBER(N'claims_app', N'$(AppPrincipalName)') <> 1
    THROW 51001, N'The API managed identity user is missing or not in role claims_app.', 1;
