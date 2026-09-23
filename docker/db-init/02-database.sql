-- Nivel base de datos (se ejecuta con -d <base>). Idempotente.
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(ERP_LOGIN)')
    CREATE USER [$(ERP_LOGIN)] FOR LOGIN [$(ERP_LOGIN)] WITH DEFAULT_SCHEMA = dbo;
ELSE
    ALTER USER [$(ERP_LOGIN)] WITH LOGIN = [$(ERP_LOGIN)];  -- re-vincula si el SID del login cambio

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(MIG_LOGIN)')
    CREATE USER [$(MIG_LOGIN)] FOR LOGIN [$(MIG_LOGIN)] WITH DEFAULT_SCHEMA = dbo;
ELSE
    ALTER USER [$(MIG_LOGIN)] WITH LOGIN = [$(MIG_LOGIN)];

-- APLICACION (runtime): solo DML. El esquema lo cambia unicamente el servicio `migrate`.
--   datareader/datawriter -> SELECT/INSERT/UPDATE/DELETE (EF, Identity, servicios del ERP)
ALTER ROLE db_datareader ADD MEMBER [$(ERP_LOGIN)];
ALTER ROLE db_datawriter ADD MEMBER [$(ERP_LOGIN)];
-- Revocacion explicita: despliegues previos le habian dado DDL a la app. Idempotente.
IF IS_ROLEMEMBER(N'db_ddladmin', N'$(ERP_LOGIN)') = 1
    ALTER ROLE db_ddladmin DROP MEMBER [$(ERP_LOGIN)];
IF IS_ROLEMEMBER(N'db_owner', N'$(ERP_LOGIN)') = 1
    ALTER ROLE db_owner DROP MEMBER [$(ERP_LOGIN)];

-- MIGRACIONES (servicio `migrate`):
--   ddladmin              -> CREATE/ALTER/DROP de tablas, indices, constraints, columnas
--   datareader/datawriter -> historial de migraciones, backfills de datos y seeds (roles, permisos, admin)
ALTER ROLE db_datareader ADD MEMBER [$(MIG_LOGIN)];
ALTER ROLE db_datawriter ADD MEMBER [$(MIG_LOGIN)];
ALTER ROLE db_ddladmin   ADD MEMBER [$(MIG_LOGIN)];
-- Defensa: nunca db_owner.
IF IS_ROLEMEMBER(N'db_owner', N'$(MIG_LOGIN)') = 1
    ALTER ROLE db_owner DROP MEMBER [$(MIG_LOGIN)];
