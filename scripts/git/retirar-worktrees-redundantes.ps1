<#
Retira (solo el worktree) los worktrees redundantes listados en docs/agentes/worktrees-historicos.md.
- Por defecto es DRY-RUN: no cambia nada. Para ejecutar: -Ejecutar.
- Conserva SIEMPRE la rama y sus commits (no hace git branch -D).
- Cada worktree se retira solo si, en el momento de ejecutar: esta limpio, sin stash propio
  y sin commits cuyo parche no este en origin/main (git cherry). Si algo falla, lo salta y lo informa.
- Nunca usa --force ni borra archivos fuera de `git worktree remove`.
#>
param([switch]$Ejecutar, [string]$Repo = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))

$candidatos = 'com-s-005','fin-s-006','integracion','integracion-v2','mora-cfg-fix','mora-clientes','prd-p0','prd-p1',
  'qa-fin-conc','qa-fin-conc-p0','qa-fin-tx','qa-prd-tx','qa-v3','sec-s-003','seg-p0'
$base = Split-Path -Parent $Repo
git -C $Repo fetch -q origin
foreach ($n in $candidatos) {
  $ruta = Join-Path $base "theburyproject1-$n"
  if (-not (Test-Path $ruta)) { Write-Host "SALTA  $n : no existe"; continue }
  $rama = (git -C $ruta rev-parse --abbrev-ref HEAD).Trim()
  $sucio = @(git -C $ruta status --porcelain).Count
  $unicos = @(git -C $ruta cherry origin/main HEAD | Where-Object { $_ -like '+*' }).Count
  if ($sucio -ne 0 -or $unicos -ne 0) { Write-Host "SALTA  $n ($rama): sucio=$sucio commits_unicos=$unicos"; continue }
  if (-not $Ejecutar) { Write-Host "RETIRARIA $n ($rama) - la rama se conserva"; continue }
  git -C $Repo worktree remove $ruta
  if ($LASTEXITCODE -eq 0) { Write-Host "RETIRADO $n - rama '$rama' conservada" } else { Write-Host "ERROR  $n : no se retiro" }
}
