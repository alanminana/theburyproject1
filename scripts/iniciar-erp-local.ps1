<#
  Arranque seguro del ERP local para desarrollo en Windows (SQL Server LocalDB o local).
  Inicia SOLO si el checkout esta en main, igual a origin/main, sin cambios de codigo y con la base al dia con las migraciones.
  No descarta cambios, no cambia de rama y NUNCA aplica migraciones. No imprime la cadena de conexion.

    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\iniciar-erp-local.ps1                  # verifica e inicia en 18787
      -Actualizar       avanza main hasta origin/main (solo fast-forward) antes de verificar
      -SoloVerificar    verifica y termina sin iniciar
      -Base NOMBRE      usa esa base (autenticacion de Windows) en lugar de la de user-secrets
      -Servidor NOMBRE  servidor para -Base (default (localdb)\MSSQLLocalDB)
      -Puerto N         puerto local (default 18787)

  Codigos de salida: 0 ok | 1 no se inicia (el motivo se imprime). Documentacion: docs/desarrollo-local.md
#>
param(
    [switch]$Actualizar,
    [switch]$SoloVerificar,
    [string]$Base = '',
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    [ValidateRange(1, 65535)][int]$Puerto = 18787
)
$ErrorActionPreference = 'Continue'   # git y dotnet escriben en stderr sin fallar; se controla $LASTEXITCODE

function Stop-Arranque([string]$Motivo) { Write-Host "NO SE INICIA: $Motivo" -ForegroundColor Red; exit 1 }
function Test-ServidorLocal([string]$s) { return $s -match '^(?i)(\(localdb\)\\.+|(\.|localhost|127\.0\.0\.1|\(local\))(\\[^,]+)?(,\d+)?)$' }

$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
foreach ($c in 'git', 'dotnet', 'sqlcmd') { if (-not (Get-Command $c -ErrorAction SilentlyContinue)) { Stop-Arranque "falta el comando '$c' en el PATH." } }

# 1. Version del codigo
$rama = "$(git branch --show-current)"
if ($rama -ne 'main') { Stop-Arranque "el checkout esta en '$rama', no en main. El trabajo en curso va en un worktree aparte." }

git fetch --quiet origin main
if ($LASTEXITCODE -ne 0) { Stop-Arranque "no se pudo consultar origin: sin eso no se puede asegurar que la version sea la actual." }

# Bloquean: cualquier archivo versionado modificado (salvo la configuracion local del agente) y los archivos sin versionar
# que entran en la compilacion o se sirven. Las carpetas de herramientas de agentes y las notas sueltas no afectan al ERP.
$cambios = @(git -c core.quotepath=off status --porcelain --untracked-files=all | Where-Object {
        $estado = $_.Substring(0, 2); $ruta = $_.Substring(3).Trim('"')
        if ($ruta -match '^(\.agents|\.claude|\.codex)/') { return $false }
        if ($estado -eq '??') { return $ruta -match '\.(cs|cshtml|csproj|razor|resx|props|targets|json|js|css)$' }
        return $true
    })
if ($cambios.Count -gt 0) { $cambios | ForEach-Object { Write-Host "  $_" }; Stop-Arranque "hay cambios locales en el proyecto. Guardarlos en una rama (en un worktree) antes de ejecutar." }

if ($Actualizar) {
    git merge --ff-only --quiet origin/main
    if ($LASTEXITCODE -ne 0) { Stop-Arranque "no se pudo avanzar con fast-forward. No se hizo merge ni reset." }
}
$head = "$(git rev-parse HEAD)"
if ($head -ne "$(git rev-parse origin/main)") {
    $d = "$(git rev-list --left-right --count origin/main...HEAD)" -split '\s+'
    Stop-Arranque "version desactualizada o distinta: $($d[0]) commits detras y $($d[1]) delante de origin/main. Usar -Actualizar."
}

# Lo que la compilacion incluye de verdad: un archivo ignorado por Git (p. ej. en tmp/) no aparece en git status
# pero MSBuild lo compila. Se evalua el proyecto y se compara con lo versionado. wwwroot/uploads son datos del usuario.
$evaluacion = dotnet msbuild TheBuryProyect.csproj -nologo -getItem:Compile -getItem:Content -getItem:EmbeddedResource
if ($LASTEXITCODE -ne 0) { Stop-Arranque "no se pudo evaluar el proyecto con MSBuild para comprobar que se compila." }
try { $items = ($evaluacion -join "`n" | ConvertFrom-Json -ErrorAction Stop).Items } catch { Stop-Arranque "no se pudo interpretar la evaluacion de MSBuild." }
$versionados = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
git -c core.quotepath=off ls-files | ForEach-Object { [void]$versionados.Add($_) }
$candidatos = @($items.Compile | ForEach-Object { $_.Identity }) + @($items.EmbeddedResource | ForEach-Object { $_.Identity }) +
    @($items.Content | ForEach-Object { $_.Identity } | Where-Object { $_ -match '\.(cshtml|razor)$' -or ($_ -match '^wwwroot[\\/].+\.(js|css)$' -and $_ -notmatch '^wwwroot[\\/]uploads[\\/]') }) +
    @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Build.rsp', 'Directory.Packages.props' | Where-Object { Test-Path (Join-Path $raiz $_) })
$extra = @($candidatos | Where-Object { $_ } | ForEach-Object { $_ -replace '\\', '/' } | Where-Object { $_ -notmatch '^(bin|obj)/' -and -not $versionados.Contains($_) } | Sort-Object -Unique)
if ($extra.Count -gt 0) { $extra | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }; Stop-Arranque "hay $($extra.Count) archivo(s) no versionados que entran en la compilacion (aunque Git los ignore). Moverlos fuera del proyecto." }

# 2. Base de datos de destino
# Ninguna variable heredada puede decidir la conexion: .NET lee tambien SQLCONNSTR_/CUSTOMCONNSTR_/... y les daria
# prioridad sobre user-secrets, y la aplicacion usaria una base distinta de la validada.
$heredadas = @(Get-ChildItem Env: | Where-Object { $_.Name -match '(?i)(CONNSTR_|ConnectionStrings(__|:))DefaultConnection$' } | ForEach-Object { $_.Name })
if ($heredadas.Count -gt 0) { Stop-Arranque "hay variable de entorno que reemplaza la conexion: $($heredadas -join ', '). Quitarla de la terminal (para otra base local usar -Base)." }
$cadena = ''
if ($Base) {
    if ($Base -notmatch '^[A-Za-z0-9_]+$') { Stop-Arranque "nombre de base invalido." }
    $servidorDb = $Servidor; $baseDb = $Base
    $cadena = "Server=$Servidor;Database=$Base;Trusted_Connection=True;TrustServerCertificate=True"
}
else {
    $id = [regex]::Match((Get-Content (Join-Path $raiz 'TheBuryProyect.csproj') -Raw), '<UserSecretsId>(.*?)</UserSecretsId>').Groups[1].Value
    if (-not $id) { Stop-Arranque "el proyecto no declara UserSecretsId." }
    $secretos = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
    if (-not (Test-Path $secretos)) { Stop-Arranque "no hay user-secrets. Definir ConnectionStrings:DefaultConnection (docs/desarrollo-local.md) o usar -Base." }
    $cs = (Get-Content $secretos -Raw | ConvertFrom-Json).'ConnectionStrings:DefaultConnection'
    if (-not $cs) { Stop-Arranque "user-secrets no define ConnectionStrings:DefaultConnection (usar la clave plana) ni se indico -Base." }
    try { $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cs } catch { Stop-Arranque "la cadena de user-secrets no se pudo interpretar." }
    if (-not $b.IntegratedSecurity) { Stop-Arranque "este lanzador solo admite autenticacion de Windows contra un SQL Server local." }
    $servidorDb = $b.DataSource; $baseDb = $b.InitialCatalog
    $cadena = $cs   # se fija en el proceso de la aplicacion: la misma conexion que se valida abajo
    if (-not $baseDb) { Stop-Arranque "la cadena de user-secrets no indica la base." }
}
if (-not (Test-ServidorLocal $servidorDb)) { Stop-Arranque "el servidor de la base no es local. Este lanzador es solo para desarrollo." }

# 3. Migraciones: la base y el codigo deben coincidir exactamente
$aplicadas = sqlcmd -S $servidorDb -E -C -b -h -1 -W -d $baseDb -Q "SET NOCOUNT ON; SELECT MigrationId FROM __EFMigrationsHistory"
if ($LASTEXITCODE -ne 0) { Stop-Arranque "no se pudo leer el historial de migraciones de '$baseDb' (base inexistente, vacia o inaccesible). Ver docs/desarrollo-local.md." }
$aplicadas = @($aplicadas | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
$enCodigo = @(git ls-files Migrations | Where-Object { $_ -match '^Migrations/\d{14}_[^.]+\.cs$' } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
if ($enCodigo.Count -eq 0) { Stop-Arranque "no se encontraron migraciones en el codigo." }
$pendientes = @($enCodigo | Where-Object { $aplicadas -notcontains $_ })
$sobrantes = @($aplicadas | Where-Object { $enCodigo -notcontains $_ })

Write-Host "Codigo : main $($head.Substring(0,8)) = origin/main ($($enCodigo.Count) migraciones)"
Write-Host "Base   : $baseDb en $servidorDb ($($aplicadas.Count) aplicadas)"
if ($sobrantes.Count -gt 0) { $sobrantes | ForEach-Object { Write-Host "  solo en la base: $_" }; Stop-Arranque "la base tiene $($sobrantes.Count) migraciones que este codigo no conoce (codigo mas viejo que la base)." }
if ($pendientes.Count -gt 0) { $pendientes | ForEach-Object { Write-Host "  pendiente: $_" }; Stop-Arranque "faltan $($pendientes.Count) migraciones en '$baseDb'. Ver docs/desarrollo-local.md, 'Aplicar migraciones'." }

# 4. Puerto
if (Get-NetTCPConnection -State Listen -LocalPort $Puerto -ErrorAction SilentlyContinue) { Stop-Arranque "el puerto $Puerto ya esta en uso: hay otra instancia. Ver docs/desarrollo-local.md." }
if ($SoloVerificar) { Write-Host "VERIFICACION OK (no se inicio nada)." -ForegroundColor Green; exit 0 }

$previo = @{ A = $env:ASPNETCORE_ENVIRONMENT; I = $env:Database__InitializeOnStartup }
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__InitializeOnStartup = 'false'
$env:ConnectionStrings__DefaultConnection = $cadena
try {
    Write-Host "Iniciando en http://127.0.0.1:$Puerto  (Ctrl+C para detener)" -ForegroundColor Green
    dotnet run --project TheBuryProyect.csproj --no-launch-profile -- --urls "http://127.0.0.1:$Puerto"
    exit $LASTEXITCODE
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previo.A; $env:Database__InitializeOnStartup = $previo.I
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
}
