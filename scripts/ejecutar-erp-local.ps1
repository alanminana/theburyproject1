<#
  Ejecuta TheBuryProject desde un checkout limpio de origin/main (nunca desde una rama vieja ni con cambios locales).

  Uso (desde un worktree LIMPIO dedicado a ejecutar, p. ej. D:\git\theburyproject1-run):
    pwsh -File scripts\ejecutar-erp-local.ps1                      # base real (user-secrets), http://127.0.0.1:18787
    pwsh -File scripts\ejecutar-erp-local.ps1 -Puerto 5205 -ConnectionString "Server=(localdb)\MSSQLLocalDB;Database=TheBuryProjectDb_qa;Trusted_Connection=True;TrustServerCertificate=True"

  Que hace: verifica que el arbol este limpio, lo lleva a origin/main (detached), compila en bin\app, comprueba que el puerto
  este libre y arranca con Database:InitializeOnStartup=false (sin inicializadores ni migraciones automaticas). No toca la base.
  Para detener: Stop-Process -Id <PID impreso>.
#>
param(
    [int]$Puerto = 18787,
    [string]$ConnectionString = "",
    [switch]$NoActualizar
)
$ErrorActionPreference = 'Stop'
$raiz = (git rev-parse --show-toplevel).Trim()
Set-Location $raiz

if (git status --porcelain) { throw "El arbol de trabajo tiene cambios locales; ejecutar desde un worktree limpio (no se descarta nada)." }
if (-not $NoActualizar) {
    git fetch origin --prune | Out-Null
    git checkout --detach origin/main | Out-Null
}
$commit = (git rev-parse --short HEAD).Trim()
$main = (git rev-parse --short origin/main).Trim()
if ($commit -ne $main) { Write-Warning "HEAD ($commit) no es origin/main ($main)." }

if (Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue) { throw "El puerto $Puerto ya esta en uso; no se arranca otra instancia (una sola instancia de la app)." }

dotnet build TheBuryProyect.csproj -c Release -o bin/app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion." }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Database__InitializeOnStartup = 'false'
if ($ConnectionString) { $env:ConnectionStrings__DefaultConnection = $ConnectionString }
$p = Start-Process dotnet -ArgumentList 'exec', 'bin/app/TheBuryProyect.dll', '--contentRoot', $raiz, '--urls', "http://127.0.0.1:$Puerto" -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $raiz 'bin\app.log') -RedirectStandardError (Join-Path $raiz 'bin\app.err')
"TheBuryProject $commit en http://127.0.0.1:$Puerto (PID $($p.Id)). Logs: bin\app.log / bin\app.err"
