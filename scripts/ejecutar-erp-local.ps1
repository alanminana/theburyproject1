<#
  Ejecuta TheBuryProject desde un checkout limpio de origin/main (nunca desde una rama vieja ni con cambios locales).

  Uso (desde un worktree LIMPIO dedicado a ejecutar, p. ej. D:\git\theburyproject1-run):
    powershell -NoProfile -File scripts\ejecutar-erp-local.ps1                      # base real (user-secrets), http://127.0.0.1:18787
    powershell -NoProfile -File scripts\ejecutar-erp-local.ps1 -Puerto 5205 -ConnectionString "Server=(localdb)\MSSQLLocalDB;Database=TheBuryProjectDb_qa;Trusted_Connection=True;TrustServerCertificate=True"

  Que hace: exige arbol limpio, lleva el checkout a origin/main (detached) y exige HEAD == origin/main (hash completo), compila en
  bin\app, comprueba puerto libre y que no haya OTRA instancia de TheBuryProyect.dll corriendo, arranca y verifica que responda.
  Arranca con Database:InitializeOnStartup=false (sin inicializadores ni migraciones automaticas).

  LIMITES CONOCIDOS (ver docs/preparacion-operativa-2026-11-05.md):
   - InitializeOnStartup=false NO detiene los servicios en segundo plano (mora, acuerdos, alertas de stock, cotizaciones, documentos,
     Mercado Libre): actuan solos mientras la app este viva.
   - El entorno por defecto es Development (LocalDB con Windows Auth, sin ErpDb:*): habilita DiagnosticoController (solo SuperAdmin).
   - Las claves de proteccion de datos y los archivos subidos viven bajo el directorio de ejecucion: usar SIEMPRE el mismo worktree.
  Para detener: Stop-Process -Id <PID impreso>.
#>
param(
    [int]$Puerto = 18787,
    [string]$ConnectionString = "",
    [ValidateSet('Development', 'Production')][string]$Entorno = 'Development',
    # D-05: modo de la CSP (Csp__Mode). Vacio = no se define (la app usa Off salvo que haya una Csp__Mode heredada del entorno).
    # Solo admite los tres nombres exactos: un valor invalido (typo) ni siquiera llega a la app. Ver docs/csp-enforce-activacion.md.
    [ValidateSet('', 'Off', 'ReportOnly', 'Enforce')][string]$CspMode = '',
    [switch]$NoActualizar
)
$ErrorActionPreference = 'Stop'
function Git-Ok { if ($LASTEXITCODE -ne 0) { throw "Fallo un comando git (codigo $LASTEXITCODE)." } }

$raiz = (git rev-parse --show-toplevel).Trim(); Git-Ok
Set-Location $raiz
if (git status --porcelain) { throw "El arbol de trabajo tiene cambios locales; ejecutar desde un worktree limpio (no se descarta nada)." }
if (-not $NoActualizar) {
    git fetch origin --prune | Out-Null; Git-Ok
    git checkout --detach origin/main | Out-Null; Git-Ok
}
$head = (git rev-parse HEAD).Trim(); Git-Ok
$main = (git rev-parse origin/main).Trim(); Git-Ok
if ($head -ne $main) { throw "HEAD ($($head.Substring(0,8))) no es origin/main ($($main.Substring(0,8))). No se arranca." }
if (git status --porcelain) { throw "El arbol quedo con cambios tras actualizar; no se arranca." }

if (Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue) { throw "El puerto $Puerto ya esta en uso." }
$otras = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -match 'TheBuryProyect\.dll' }
if ($otras) { throw "Ya hay otra instancia de TheBuryProyect.dll en ejecucion (PID $($otras.ProcessId -join ',')); una sola instancia por base." }

dotnet build TheBuryProyect.csproj -c Release -o bin/app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion." }

$env:ASPNETCORE_ENVIRONMENT = $Entorno
$env:Database__InitializeOnStartup = 'false'
if ($CspMode) { $env:Csp__Mode = $CspMode }
elseif ($env:Csp__Mode) { Write-Warning "Hay Csp__Mode='$($env:Csp__Mode)' heredada del entorno; se usara esa (use -CspMode para fijarla de forma explicita)." }
if ($ConnectionString) { $env:ConnectionStrings__DefaultConnection = $ConnectionString }
elseif ($env:ConnectionStrings__DefaultConnection) { Write-Warning "Hay ConnectionStrings__DefaultConnection heredada del entorno; se usara esa base." }
$args2 = @('exec', 'bin/app/TheBuryProyect.dll', '--contentRoot', "`"$raiz`"", '--urls', "http://127.0.0.1:$Puerto")
$p = Start-Process dotnet -ArgumentList $args2 -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $raiz 'bin\app.log') -RedirectStandardError (Join-Path $raiz 'bin\app.err')

$ok = $false
for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
    Start-Sleep -Milliseconds 750
    if ($p.HasExited) { throw "La aplicacion termino al arrancar (codigo $($p.ExitCode)). Ver bin\app.err." }
    try { $ok = (Invoke-WebRequest "http://127.0.0.1:$Puerto/Identity/Account/Login" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { }
}
if (-not $ok) { Stop-Process -Id $p.Id -Force; throw "La aplicacion no respondio a tiempo; se detuvo." }
"TheBuryProject $($head.Substring(0,8)) ($Entorno) en http://127.0.0.1:$Puerto (PID $($p.Id)). CSP: $(if ($env:Csp__Mode) { $env:Csp__Mode } else { 'Off (sin definir)' }). Logs: bin\app.log / bin\app.err"
