# Desarrollo local en Windows

Windows es el entorno principal de desarrollo; el servidor de referencia es Ubuntu ([instalacion-servidor.md](instalacion-servidor.md)).

## Requisitos

- SDK de **.NET 10** (versión fijada en `global.json`).
- SQL Server LocalDB y `sqlcmd`.
- Git. Node.js solo para las pruebas E2E.

## Configuración inicial (una vez)

Los secretos van en user-secrets, nunca en el repositorio. El lanzador lee la clave plana `ConnectionStrings:DefaultConnection`.

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=TheBuryProjectDb;Trusted_Connection=True;TrustServerCertificate=True" --project TheBuryProyect.csproj
dotnet user-secrets set "Database:InitializeOnStartup" "false" --project TheBuryProyect.csproj
dotnet user-secrets set "Admin:Email" "<correo>" --project TheBuryProyect.csproj
dotnet user-secrets set "Admin:Password" "<contraseña propia>" --project TheBuryProyect.csproj
```

Con `Database:InitializeOnStartup=false`, arrancar **no** migra ni siembra, y **tampoco avisa** si a la base le faltan migraciones. Por eso se usa el lanzador.

### Base nueva

Solo para una base **nueva y vacía** (nunca una con datos). La base debe **existir**: si no existe, `--migrate` termina con error.

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -b -Q "CREATE DATABASE [TheBuryProjectDb]"   # falla si ya existe: no continuar sin saber qué contiene
dotnet build TheBuryProyect.csproj
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet bin\Debug\net10.0\TheBuryProyect.dll --migrate     # esperado: [migrate] OK: N migraciones aplicadas, 0 pendientes.
Remove-Item Env:ASPNETCORE_ENVIRONMENT
```

En Development, `--migrate` crea además los usuarios de prueba que falten.

## Arrancar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\iniciar-erp-local.ps1
```

Usarlo en lugar de `dotnet run` o F5. **No inicia** si:

| Control | Motivo |
|---|---|
| La rama no es `main` | El trabajo en curso va en otro worktree |
| No se puede consultar `origin` | No se puede asegurar la versión (no hay modo sin conexión) |
| `HEAD` difiere de `origin/main` | Código viejo o commits sin publicar |
| Hay cambios de código sin commit | Se ejecutaría algo que no está en GitHub |
| A la base le faltan migraciones | La aplicación fallaría |
| La base tiene migraciones desconocidas | Código más viejo que la base |
| El puerto está ocupado | Ya hay otra instancia |
| El servidor de base no es local | Es una herramienta de desarrollo |

No bloquean: `.claude/settings.local.json`, las carpetas `.agents/`, `.claude/` y `.codex/`, ni archivos sueltos sin versionar que no se compilen ni se sirvan.
Nunca aplica migraciones, descarta cambios ni cambia de rama.

Opciones: `-SoloVerificar`, `-Actualizar` (fast-forward), `-Base <nombre>` (otra base local), `-Servidor`, `-Puerto`.

Pruebas del lanzador (repositorios temporales y `sqlcmd` simulado): `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-iniciar-erp-local.ps1`.
Todavía **no** corren en CI.

## Si el puerto está ocupado

Identificar el proceso antes de detenerlo:

```powershell
$id = (Get-NetTCPConnection -State Listen -LocalPort 18787).OwningProcess
Get-CimInstance Win32_Process -Filter "ProcessId=$id" | Select-Object ProcessId, CreationDate, ExecutablePath, CommandLine
```

Para saber qué código ejecuta, mirar el commit grabado en la DLL (no su fecha):
`(Get-Item <carpeta>\TheBuryProyect.dll).VersionInfo.ProductVersion`.

## Aplicar migraciones a una base con datos

**Requiere autorización del propietario.** El ERP debe estar detenido.

1. Respaldar y **probar** la restauración; los dos pasos deben terminar en OK. `-Raiz` es la carpeta desde la que se ejecuta el ERP
   (donde están `keys\`, `wwwroot\uploads\` y `App_Data\`):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\respaldo-local.ps1 -Accion Respaldar -Raiz (Get-Location).Path
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\respaldo-local.ps1 -Accion Probar    -Raiz (Get-Location).Path
   ```

2. Leer las migraciones pendientes (el lanzador las lista) y ensayarlas sobre una copia restaurada del respaldo.
3. Aplicarlas con las líneas de `dotnet build` y `--migrate` de «Base nueva» (**sin** el `CREATE DATABASE`). `--migrate` aplica migraciones **y** datos iniciales (roles, permisos, sucursales).
4. Verificar con `iniciar-erp-local.ps1 -SoloVerificar`.

Volver atrás exige restaurar el respaldo del paso 1 (sobrescribe la base; requiere autorización) y ejecutar el código anterior.
`respaldo-local.ps1` no incluye user-secrets. Los respaldos contienen datos sensibles y no quedan cifrados.

Las herramientas de EF (`dotnet ef`) también están protegidas: contra `TheBuryProjectDb` exigen `Database__AllowMigrations=true`.

## Trabajar con ramas y revisar cambios

```powershell
git fetch origin
git worktree add -b task/<tema> ..\theburyproject1-<tema> origin/main
```

Para revisar un PR sin tocar el checkout principal: worktree propio, **base propia** (copia restaurada), otro puerto y un nombre de host propio
(`http://<tema>.localhost:<puerto>`): las cookies no distinguen puertos. Los worktrees comparten user-secrets: indicar siempre la base de prueba.
No copiar `keys\` a una copia de revisión: así no puede leer los tokens cifrados de integraciones externas.

## Relación con `ejecutar-erp-local.ps1`

`scripts\ejecutar-erp-local.ps1` es un lanzador anterior para un **worktree de ejecución dedicado** (deja el checkout en `origin/main` sin rama y
compila en `bin\app`). Lo usan los procedimientos de CSP ([csp-enforce-activacion.md](csp-enforce-activacion.md)). **No comprueba migraciones.**
Para el desarrollo diario usar `iniciar-erp-local.ps1`. En cualquiera de los dos casos, ejecutar siempre desde la misma carpeta: `keys\`,
`wwwroot\uploads\` y `App_Data\` viven en el directorio de ejecución.

## Pruebas

```powershell
dotnet test TheBuryProyect.Tests     # unitarios e integración
npx playwright test                  # E2E (ver ci-playwright-e2e.md)
```
