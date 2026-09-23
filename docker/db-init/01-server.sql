-- Nivel servidor (requiere sa). Idempotente. Variables de sqlcmd tomadas del entorno (ver init-db.sh).
--   ERP_LOGIN / ERP_PWD_SQL : login de la APLICACION (runtime, sin DDL)
--   MIG_LOGIN / MIG_PWD_SQL : login de MIGRACIONES (servicio `migrate`, con DDL en la base)
SET NOCOUNT ON;

IF DB_ID(N'$(ERP_DBNAME)') IS NULL
    CREATE DATABASE [$(ERP_DBNAME)];

-- Logins SQL dedicados. Sin roles de servidor (nunca sysadmin/securityadmin/serveradmin).
-- CHECK_POLICY=ON: SQL Server aplica sus reglas de complejidad de password.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(ERP_LOGIN)')
    CREATE LOGIN [$(ERP_LOGIN)] WITH PASSWORD = N'$(ERP_PWD_SQL)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [$(ERP_DBNAME)];
ELSE
    -- Rotacion: mantiene el login sincronizado con ERP_DB_PASSWORD del .env.
    ALTER LOGIN [$(ERP_LOGIN)] WITH PASSWORD = N'$(ERP_PWD_SQL)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [$(ERP_DBNAME)];

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(MIG_LOGIN)')
    CREATE LOGIN [$(MIG_LOGIN)] WITH PASSWORD = N'$(MIG_PWD_SQL)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [$(ERP_DBNAME)];
ELSE
    ALTER LOGIN [$(MIG_LOGIN)] WITH PASSWORD = N'$(MIG_PWD_SQL)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [$(ERP_DBNAME)];

-- Defensa: si algun login llegó a tener sysadmin, se retira.
IF IS_SRVROLEMEMBER(N'sysadmin', N'$(ERP_LOGIN)') = 1
    ALTER SERVER ROLE sysadmin DROP MEMBER [$(ERP_LOGIN)];
IF IS_SRVROLEMEMBER(N'sysadmin', N'$(MIG_LOGIN)') = 1
    ALTER SERVER ROLE sysadmin DROP MEMBER [$(MIG_LOGIN)];
