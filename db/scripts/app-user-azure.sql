-- Azure SQL only: map the API's system-assigned managed identity to a contained database user and
-- put it in the least-privilege role from app-role.sql (P2.5 task 17f). Idempotent.
--
-- Must be run by a Microsoft Entra principal (the SQL Entra admin, or a member of the admin group),
-- because CREATE USER ... FROM EXTERNAL PROVIDER resolves the name in Entra ID.
--
-- $(AppPrincipalName) is the managed identity's display name, which equals the App Service name
-- (the Bicep output apiAppName). Manual run with go-sqlcmd:
--   sqlcmd -S <server>.database.windows.net -d ClaimsModule --authentication-method ActiveDirectoryDefault \
--          -v AppPrincipalName=<api-app-name> -i db/scripts/app-role.sql -i db/scripts/app-user-azure.sql
-- deploy.yml substitutes the variable with sed and runs the script with azure/sql-action.

IF DATABASE_PRINCIPAL_ID(N'$(AppPrincipalName)') IS NULL
    EXEC(N'CREATE USER [$(AppPrincipalName)] FROM EXTERNAL PROVIDER');

IF IS_ROLEMEMBER(N'claims_app', N'$(AppPrincipalName)') = 0
    ALTER ROLE claims_app ADD MEMBER [$(AppPrincipalName)];
