<#
.SYNOPSIS
  Delegacion Codex -> Claude Code (headless, acceptEdits, herramientas acotadas).

.DESCRIPTION
  Unico punto de entrada autorizado para que Codex ejecute Claude Code fuera del sandbox
  (regla ~/.codex/rules/bury-delegate.rules). Copia instalada y protegida (sin escritura para el
  sandbox) en D:\git\_agent-coord\theburyproject1\delegate\bin\. Esta copia del repo es solo la fuente.

  Controles: valida ruta de tarea, un worktree propio por tarea, bloqueo por tarea, anti-recursion,
  timeout con kill del arbol propio, presupuesto, maximo 2 rondas de correccion, evidencia git
  independiente. Nunca: commit, push, PR, merge, bypassPermissions.
#>
[CmdletBinding()]
param(
  [string]$TaskFile,
  [ValidateSet('docs', 'code')][string]$Perfil = 'docs',
  [string]$BaseRef = 'origin/main',
  [ValidateRange(0.1, 5)][double]$MaxBudgetUsd = 1.5,
  [ValidateRange(30, 1800)][int]$TimeoutSec = 600,
  [string]$ResumeSession,
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot          # ...\delegate
$mainRepo = 'D:\git\theburyproject1'
$maxCorrecciones = 2

function Fail([int]$code, [string]$msg) {
  [Console]::Error.WriteLine("delegate-claude: $msg")
  exit $code
}
function Q([string]$s) { '"' + ($s -replace '"', '\"') + '"' }
function GitIn([string]$wt, [string[]]$a) { & git.exe -C $wt @a }

# Anti-recursion: una delegacion nunca puede iniciar otra.
if ($env:BURY_DELEGATION_DEPTH) { Fail 3 'recursion bloqueada (BURY_DELEGATION_DEPTH definido)' }

$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claudeCmd) { Fail 2 'claude no encontrado en PATH' }

if ($SelfTest) {
  $net = (Test-NetConnection api.anthropic.com -Port 443 -WarningAction SilentlyContinue).TcpTestSucceeded
  [ordered]@{
    user = (whoami); claude = $claudeCmd.Source; claudeVersion = (& claude --version)
    apiAnthropic443 = $net; root = $root
  } | ConvertTo-Json
  exit 0
}

# --- Validacion de la tarea -------------------------------------------------------------
if (-not $TaskFile) { Fail 2 '-TaskFile requerido' }
if (-not (Test-Path -LiteralPath $TaskFile -PathType Leaf)) { Fail 2 "TaskFile inexistente: $TaskFile" }
$taskPath = (Resolve-Path -LiteralPath $TaskFile).Path
$rx = '^D:\\git\\theburyproject1(-[a-z0-9-]+)?\\docs\\agentes\\delegations\\([a-z0-9][a-z0-9-]{2,47})\\task\.md$'
if ($taskPath -notmatch $rx) { Fail 2 'TaskFile fuera de docs\agentes\delegations\<id>\task.md de un worktree theburyproject1*' }
$id = $Matches[2]
if ($BaseRef -notmatch '^(?!-)[A-Za-z0-9._/-]{1,80}$') { Fail 2 'BaseRef invalido' }
if ($ResumeSession -and $ResumeSession -notmatch '^[0-9a-f-]{36}$') { Fail 2 'ResumeSession invalido' }

$runDir = Join-Path $root "runs\$id"
New-Item -ItemType Directory -Force $runDir | Out-Null
$roundsFile = Join-Path $runDir 'rounds.json'
$metaFile = Join-Path $runDir 'meta.json'
$rounds = @()
if (Test-Path $roundsFile) { $rounds = @(Get-Content $roundsFile -Raw -Encoding UTF8 | ConvertFrom-Json) }
$correcciones = @($rounds | Where-Object { $_.resume }).Count
if ($ResumeSession) {
  if ($rounds.Count -eq 0) { Fail 2 'no hay ronda inicial para reanudar' }
  if ($correcciones -ge $maxCorrecciones) { Fail 6 "maximo de $maxCorrecciones rondas de correccion alcanzado: escalar al usuario" }
} elseif ($rounds.Count -gt 0) { Fail 2 'task id ya usado: usar -ResumeSession o un id nuevo' }
$round = $rounds.Count

# --- Bloqueo por tarea -------------------------------------------------------------------
$lockFile = Join-Path $runDir '.lock'
if (Test-Path $lockFile) {
  $oldPid = [int](Get-Content $lockFile -ErrorAction SilentlyContinue | Select-Object -First 1)
  if ($oldPid -and (Get-Process -Id $oldPid -ErrorAction SilentlyContinue)) { Fail 4 "tarea bloqueada por PID $oldPid" }
  Remove-Item $lockFile -Force
}
try { $fs = [IO.File]::Open($lockFile, 'CreateNew', 'Write', 'None') } catch { Fail 4 ("no se pudo tomar el bloqueo: " + $_.Exception.Message) }
$sw = New-Object IO.StreamWriter($fs); $sw.WriteLine($PID); $sw.Flush()

try {
  # --- Worktree propio de la tarea --------------------------------------------------------
  $wt = "D:\git\theburyproject1-t-$id"
  $branch = "task/$id"
  if ($ResumeSession) {
    if (-not (Test-Path $wt)) { Fail 2 "worktree de la tarea inexistente: $wt" }
    $meta = Get-Content $metaFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $baseSha = $meta.baseSha
  } else {
    if (Test-Path $wt) { Fail 2 "ya existe $wt" }
    $baseSha = (& git.exe -C $mainRepo rev-parse "$BaseRef^{commit}").Trim()
    & git.exe -C $mainRepo worktree add -b $branch $wt $baseSha | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 1 'git worktree add fallo' }
    [ordered]@{ id = $id; worktree = $wt; branch = $branch; baseRef = $BaseRef; baseSha = $baseSha; perfil = $Perfil } |
      ConvertTo-Json | Set-Content $metaFile -Encoding UTF8
  }
  Copy-Item -LiteralPath $taskPath -Destination (Join-Path $runDir "task-r$round.md") -Force
  $taskHash = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash

  # --- Prompt, herramientas y esquema ------------------------------------------------------
  $reglas = 'Sos Claude Code ejecutando una delegacion de Codex (coordinador) en un worktree aislado. ' +
    'Reglas: editas solo dentro del directorio actual; no hagas commit, push, PR, merge, rebase ni git add; ' +
    'no invoques codex ni delegate-claude ni PowerShell; no toques .claude, .codex, AGENTS.md ni CLAUDE.md salvo que la tarea lo pida; ' +
    'no inventes resultados: reporta solo lo que ejecutaste o leiste; si algo te bloquea, status=blocked con el motivo literal.'
  $prompt = "Tarea id=$id ronda=$round`n`n" + (Get-Content -LiteralPath $taskPath -Raw -Encoding UTF8)
  $promptFile = Join-Path $runDir "prompt-r$round.txt"
  [IO.File]::WriteAllText($promptFile, $prompt, (New-Object Text.UTF8Encoding($false)))

  $allowed = 'Read,Glob,Grep,Edit,Write,Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(git ls-files:*)'
  if ($Perfil -eq 'code') { $allowed += ',Bash(dotnet build:*),Bash(dotnet test:*),Bash(dotnet restore:*),Bash(npx.cmd playwright test:*)' }
  $denied = 'Bash(git add:*),Bash(git commit:*),Bash(git push:*),Bash(git merge:*),Bash(git rebase:*),Bash(git reset:*),Bash(git checkout:*),Bash(git clean:*),Bash(git stash:*),Bash(git worktree:*),Bash(gh:*),Bash(codex:*),Bash(powershell:*),Bash(pwsh:*),Bash(cmd:*),Bash(*delegate-claude*),WebFetch,WebSearch,Agent'
  $schema = '{"type":"object","properties":{"status":{"type":"string","enum":["done","partial","blocked"]},"summary":{"type":"string"},"files_changed":{"type":"array","items":{"type":"object","properties":{"path":{"type":"string"},"change":{"type":"string"}},"required":["path","change"]}},"tests_run":{"type":"array","items":{"type":"object","properties":{"command":{"type":"string"},"result":{"type":"string"}},"required":["command","result"]}},"blockers":{"type":"array","items":{"type":"string"}}},"required":["status","summary","files_changed","tests_run","blockers"]}'

  $argv = @('-p', '--output-format', 'stream-json', '--verbose', '--permission-mode', 'acceptEdits',
    '--allowedTools', (Q $allowed), '--disallowedTools', (Q $denied),
    '--max-budget-usd', ([string]$MaxBudgetUsd), '--name', (Q "delegation-$id-r$round"),
    '--append-system-prompt', (Q $reglas), '--json-schema', (Q $schema))
  if ($ResumeSession) { $argv += @('--resume', $ResumeSession) }

  $hash0 = & { $x = (GitIn $wt @('rev-parse', 'HEAD')) + (GitIn $wt @('status', '--porcelain=v1') | Out-String); $x }
  $streamFile = Join-Path $runDir "claude-r$round.stream.jsonl"
  $errFile = Join-Path $runDir "claude-r$round.err.txt"
  $env:BURY_DELEGATION_DEPTH = '1'
  $started = Get-Date
  $p = Start-Process -FilePath $claudeCmd.Source -ArgumentList ($argv -join ' ') -WorkingDirectory $wt `
    -RedirectStandardInput $promptFile -RedirectStandardOutput $streamFile -RedirectStandardError $errFile `
    -NoNewWindow -PassThru
  $timedOut = $false
  if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    $timedOut = $true
    & taskkill /PID $p.Id /T /F | Out-Null     # solo el arbol del proceso lanzado por este wrapper
  }
  $p.WaitForExit()
  $exit = if ($timedOut) { 124 } else { $p.ExitCode }
  $elapsedMs = [int]((Get-Date) - $started).TotalMilliseconds

  # --- Resultado de Claude ------------------------------------------------------------------
  $init = $null; $res = $null
  if (Test-Path $streamFile) {
    foreach ($line in Get-Content $streamFile -Encoding UTF8) {
      if (-not $line.StartsWith('{')) { continue }
      try { $o = $line | ConvertFrom-Json } catch { continue }
      if ($o.type -eq 'system' -and $o.subtype -eq 'init') { $init = $o }
      if ($o.type -eq 'result') { $res = $o }
    }
  }

  # --- Evidencia git independiente del wrapper ------------------------------------------------
  $head = (GitIn $wt @('rev-parse', 'HEAD')).Trim()
  $status = @(GitIn $wt @('status', '--porcelain=v1'))
  $numstat = @(GitIn $wt @('diff', '--numstat', 'HEAD'))
  $untracked = @(GitIn $wt @('ls-files', '--others', '--exclude-standard'))
  $diffHash = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash(
        [Text.Encoding]::UTF8.GetBytes(((GitIn $wt @('diff', 'HEAD')) | Out-String)))) -replace '-', '')

  $perm = if ($init) { $init.permissionMode } else { $null }
  $estado = 'error'
  if ($timedOut) { $estado = 'timeout' }
  elseif ($res -and -not $res.is_error -and $res.structured_output) { $estado = [string]$res.structured_output.status }
  elseif ($res -and $res.is_error) { $estado = 'error' }
  if ($head -ne $baseSha -and -not $ResumeSession) { $estado = 'violacion-commit' }

  $out = [ordered]@{
    taskId = $id; round = $round; resume = [bool]$ResumeSession; estado = $estado; exitCode = $exit; timedOut = $timedOut
    elapsedMs = $elapsedMs; taskSha256 = $taskHash; perfil = $Perfil
    permissionModeObserved = $perm; sessionId = $(if ($res) { $res.session_id } else { $null })
    costUsd = $(if ($res) { $res.total_cost_usd } else { $null })
    claudeStructured = $(if ($res) { $res.structured_output } else { $null })
    claudeIsError = $(if ($res) { $res.is_error } else { $null })
    git = [ordered]@{
      worktree = $wt; branch = $branch; baseSha = $baseSha; head = $head; headEqualsBase = ($head -eq $baseSha)
      status = $status; numstat = $numstat; untracked = $untracked; diffSha256 = $diffHash
    }
    artefactos = [ordered]@{ stream = $streamFile; stderr = $errFile; prompt = $promptFile }
  }
  $resultFile = Join-Path $runDir "result-r$round.json"
  $out | ConvertTo-Json -Depth 8 | Set-Content $resultFile -Encoding UTF8
  $rounds += [pscustomobject]@{ round = $round; resume = [bool]$ResumeSession; estado = $estado; at = (Get-Date).ToString('o') }
  ConvertTo-Json -InputObject @($rounds) | Set-Content $roundsFile -Encoding UTF8

  [ordered]@{ taskId = $id; round = $round; estado = $estado; permissionModeObserved = $perm; sessionId = $out.sessionId
    resultFile = $resultFile; worktree = $wt } | ConvertTo-Json
  if ($estado -eq 'done') { exit 0 } else { exit 1 }
}
finally {
  Remove-Item Env:\BURY_DELEGATION_DEPTH -ErrorAction SilentlyContinue
  if ($sw) { $sw.Dispose() }
  if ($fs) { $fs.Dispose() }
  Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
}
