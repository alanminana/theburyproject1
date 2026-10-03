<#
.SYNOPSIS
  Instala TheBuryProject en un servidor Windows 10/11 (Docker Desktop + WSL2) de forma automatica.

.DESCRIPTION
  Ejecutar en PowerShell COMO ADMINISTRADOR, con el repositorio ya clonado DENTRO de WSL (no en C:\).
  Hace, en orden:
    1. Verifica WSL2, la distro, Docker Desktop y su integracion con la distro.
    2. Ejecuta scripts/install/install.sh dentro de WSL (genera .env, construye la imagen, despliega, extrae la CA de Caddy).
    3. Regla de Firewall de Windows: 443 TCP/UDP solo desde las redes indicadas en -AllowFrom.
    4. Tareas programadas de backup (full diario, log cada N min, prueba de restore semanal) que llaman a los mismos
       scripts por wsl.exe, y una tarea al iniciar sesion que arranca Docker Desktop y WSL.
    5. Perfil de energia de servidor (sin suspender ni hibernar, tapa cerrada sin efecto estando enchufada).
    6. Opcional: confiar en la CA de Caddy en este equipo (-TrustCaHere) y agregar 'IP dominio' al archivo hosts (-HostsIp).
  Es idempotente: se puede volver a ejecutar. -DryRun solo muestra lo que haria (no modifica nada ni requiere administrador).
  -Uninstall quita las tareas programadas y la regla de firewall (no toca datos, volumenes ni la instalacion Docker).

  Docker Desktop corre con la sesion de usuario: para que el ERP arranque solo tras un corte de luz hay que configurar el inicio
  de sesion automatico de Windows (ver docs/instalacion-servidor.md). Este script no lo hace porque guarda una contrasena.

.EXAMPLE
  .\install-windows.ps1 -AdminEmail soporte@example.com -DryRun

.EXAMPLE
  .\install-windows.ps1 -AdminEmail soporte@example.com -AllowFrom LocalSubnet,10.8.0.0/24
#>
[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu',
    [string]$RepoPath = '~/theburyproject1',
    [string]$Domain = 'tbp',
    [string]$AdminEmail = '',
    [string]$AdminUser = 'admin',
    [ValidateSet('community', 'commercial')][string]$License = 'community',
    [string]$AutomapperKey = '',
    [string]$BackupDir = '/srv/bury-backups',
    [string[]]$AllowFrom = @('LocalSubnet'),
    [string]$FullBackupAt = '02:30',
    [ValidateSet(5, 10, 15, 20, 30)][int]$LogEveryMinutes = 15,
    [string]$HostsIp = '',
    [switch]$TrustCaHere,
    [switch]$SkipInstall,
    [switch]$SkipFirewall,
    [switch]$SkipTasks,
    [switch]$SkipPower,
    [switch]$Uninstall,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$TaskPrefix = 'TheBury'
$FirewallName = 'TheBury ERP 443'

function Write-Step([string]$m) { Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok([string]$m) { Write-Host "    OK   $m" -ForegroundColor Green }
function Write-Warn2([string]$m) { Write-Host "    AVISO $m" -ForegroundColor Yellow }
function Write-Dry([string]$m) { Write-Host "    [dry-run] $m" -ForegroundColor DarkGray }
function Fail([string]$m) { Write-Host "    ERROR $m" -ForegroundColor Red; exit 1 }
# En -DryRun un prerequisito que falta se informa pero no corta el recorrido.
function Fail-Prereq([string]$m) { if ($DryRun) { Write-Warn2 "(dry-run) $m" } else { Fail $m } }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Ejecuta un comando bash dentro de la distro. Devuelve la salida; $LASTEXITCODE queda con el codigo de bash.
function Invoke-Wsl([string]$bashCommand) {
    & wsl.exe -d $Distro -- bash -lc $bashCommand
}

function Quote-Bash([string]$s) { return "'" + ($s -replace "'", "'\''") + "'" }

if (-not $DryRun -and -not (Test-Admin)) { Fail 'Ejecutar PowerShell como Administrador (o usar -DryRun para ver los pasos sin cambiar nada).' }

# ----------------------------------------------------------------------------------------------------- desinstalar
if ($Uninstall) {
    Write-Step 'Quitando tareas programadas y regla de firewall'
    foreach ($t in @("$TaskPrefix Backup Full", "$TaskPrefix Backup Log", "$TaskPrefix Restore Test", "$TaskPrefix Docker Start")) {
        if ($DryRun) { Write-Dry "Unregister-ScheduledTask '$t'"; continue }
        if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) { Unregister-ScheduledTask -TaskName $t -Confirm:$false; Write-Ok "tarea '$t' eliminada" }
    }
    if ($DryRun) { Write-Dry "Remove-NetFirewallRule '$FirewallName'" }
    elseif (Get-NetFirewallRule -DisplayName $FirewallName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -DisplayName $FirewallName; Write-Ok 'regla de firewall eliminada' }
    Write-Host ''; Write-Host 'Listo. No se tocaron datos, volumenes Docker ni backups.'
    exit 0
}

if (-not $SkipInstall -and [string]::IsNullOrWhiteSpace($AdminEmail)) { Fail 'Falta -AdminEmail (correo del administrador inicial).' }
if ($FullBackupAt -notmatch '^([01][0-9]|2[0-3]):[0-5][0-9]$') { Fail '-FullBackupAt debe ser HH:MM' }
if ($RepoPath -notmatch "^[A-Za-z0-9_./~-]+$") { Fail '-RepoPath tiene caracteres no soportados' }
if ($Distro -notmatch '^[A-Za-z0-9._-]+$') { Fail '-Distro invalido' }

# ----------------------------------------------------------------------------------------------------- 1. prerequisitos
Write-Step 'Verificando WSL2, la distro y Docker Desktop'
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { Fail-Prereq 'wsl.exe no existe. Instalar WSL2: wsl --install -d Ubuntu (requiere reiniciar).' }
$distros = (& wsl.exe -l -q) -join "`n"
$distros = $distros -replace "`0", ''
if ($distros -notmatch "(?m)^\s*$([regex]::Escape($Distro))\s*$") { Fail-Prereq "La distro '$Distro' no esta instalada (wsl -l -q). Instalar: wsl --install -d $Distro" }
$ver = ((& wsl.exe -l -v) -join "`n") -replace "`0", ''
if ($ver -notmatch "$([regex]::Escape($Distro))\s+\w+\s+2") { Fail-Prereq "La distro '$Distro' no es WSL2 (convertir: wsl --set-version $Distro 2)." }
Write-Ok "WSL2 con la distro '$Distro'"

$null = Invoke-Wsl 'docker info >/dev/null 2>&1'
if ($LASTEXITCODE -ne 0) {
    Fail-Prereq "Docker no responde dentro de '$Distro'. Abrir Docker Desktop y activar Settings > Resources > WSL integration > $Distro."
}
else { Write-Ok 'Docker responde dentro de WSL' }

$repoQ = Quote-Bash $RepoPath
# ~ debe expandirse: se usa sin comillas simples para el prefijo ~/
$repoExpr = if ($RepoPath.StartsWith('~/')) { '~/' + (Quote-Bash $RepoPath.Substring(2)) } else { $repoQ }
$null = Invoke-Wsl "test -f $repoExpr/scripts/install/install.sh"
if ($LASTEXITCODE -ne 0) {
    Fail-Prereq "No se encontro el repositorio en WSL ($RepoPath). Clonarlo DENTRO de WSL: wsl -d $Distro -- git clone <url> $RepoPath"
}
else { Write-Ok "repositorio en WSL: $RepoPath" }
$wslUser = ((Invoke-Wsl 'id -un') -join '').Trim()

# ----------------------------------------------------------------------------------------------------- 2. instalacion
if ($SkipInstall) {
    Write-Step 'Instalacion del ERP omitida (-SkipInstall)'
}
else {
    Write-Step 'Instalando el ERP dentro de WSL (generar .env, construir imagen, desplegar)'
    $installArgs = @('--domain', $Domain, '--admin-email', $AdminEmail, '--admin-user', $AdminUser, '--license', $License, '--backup-dir', $BackupDir, '--yes')
    if ($License -eq 'commercial') { $installArgs += @('--automapper-key', $AutomapperKey) }
    $argStr = ($installArgs | ForEach-Object { Quote-Bash $_ }) -join ' '
    $cmd = "cd $repoExpr && bash scripts/install/install.sh $argStr"
    if ($DryRun) { Write-Dry "wsl -d $Distro -- bash -lc `"cd $RepoPath && bash scripts/install/install.sh ...`"" }
    else {
        Invoke-Wsl $cmd
        if ($LASTEXITCODE -ne 0) { Fail "install.sh fallo (codigo $LASTEXITCODE). Revisar los mensajes anteriores; es seguro volver a ejecutar." }
        Write-Ok 'ERP instalado'
    }
}

# ----------------------------------------------------------------------------------------------------- 3. firewall
if (-not $SkipFirewall) {
    Write-Step "Regla de Firewall: 443 TCP/UDP solo desde: $($AllowFrom -join ', ')"
    if ($DryRun) { Write-Dry "New-NetFirewallRule -DisplayName '$FirewallName' -Direction Inbound -Protocol TCP/UDP -LocalPort 443 -RemoteAddress $($AllowFrom -join ',')" }
    else {
        if (Get-NetFirewallRule -DisplayName $FirewallName -ErrorAction SilentlyContinue) { Remove-NetFirewallRule -DisplayName $FirewallName }
        foreach ($proto in @('TCP', 'UDP')) {
            New-NetFirewallRule -DisplayName $FirewallName -Direction Inbound -Action Allow -Protocol $proto -LocalPort 443 -RemoteAddress $AllowFrom -Profile Any | Out-Null
        }
        Write-Ok 'regla creada (no se publican 8080 ni 1433)'
    }
}

# ----------------------------------------------------------------------------------------------------- 4. tareas programadas
if (-not $SkipTasks) {
    Write-Step 'Tareas programadas de backup y arranque'
    $errLog = '$HOME/bury-backup-errors.log'
    function New-WslAction([string]$script) {
        $inner = "cd $repoExpr && /bin/bash $script >/dev/null 2>>$errLog"
        $arg = "-d $Distro -- bash -lc " + '"' + ($inner -replace '"', '\"') + '"'
        return New-ScheduledTaskAction -Execute 'wsl.exe' -Argument $arg
    }
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 3)
    $tasks = @(
        @{ Name = "$TaskPrefix Backup Full"; Script = 'scripts/backup/backup.sh all'; Trigger = (New-ScheduledTaskTrigger -Daily -At $FullBackupAt) },
        @{ Name = "$TaskPrefix Backup Log"; Script = 'scripts/backup/backup.sh log'; Trigger = (New-ScheduledTaskTrigger -Once -At (Get-Date).Date -RepetitionInterval (New-TimeSpan -Minutes $LogEveryMinutes) -RepetitionDuration (New-TimeSpan -Days 3650)) },
        @{ Name = "$TaskPrefix Restore Test"; Script = 'scripts/backup/restore-test.sh'; Trigger = (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At '04:30') }
    )
    foreach ($t in $tasks) {
        if ($DryRun) { Write-Dry "tarea '$($t.Name)': wsl.exe -d $Distro -- bash -lc 'cd $RepoPath && /bin/bash $($t.Script)'"; continue }
        Register-ScheduledTask -TaskName $t.Name -Action (New-WslAction $t.Script) -Trigger $t.Trigger -Principal $principal -Settings $settings -Force | Out-Null
        Write-Ok "tarea '$($t.Name)' registrada"
    }
    $dockerExe = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    $startCmd = "if (-not (Get-Process 'Docker Desktop' -ErrorAction SilentlyContinue)) { Start-Process '$dockerExe' }; wsl.exe -d $Distro -- true"
    if ($DryRun) { Write-Dry "tarea '$TaskPrefix Docker Start' al iniciar sesion: arranca Docker Desktop y WSL" }
    else {
        $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -Command `"$startCmd`""
        Register-ScheduledTask -TaskName "$TaskPrefix Docker Start" -Action $act -Trigger (New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME") -Principal $principal -Settings $settings -Force | Out-Null
        Write-Ok "tarea '$TaskPrefix Docker Start' registrada"
    }
    Write-Warn2 "Los backups usan la sesion de '$env:USERNAME': configurar el inicio de sesion automatico (docs/instalacion-servidor.md)."
}

# ----------------------------------------------------------------------------------------------------- 5. energia
if (-not $SkipPower) {
    Write-Step 'Perfil de energia de servidor (sin suspender/hibernar; tapa cerrada sin efecto con corriente)'
    if ($DryRun) { Write-Dry 'powercfg: standby/hibernate/monitor timeouts en AC = 0; LIDACTION AC = 0 (no hacer nada)' }
    else {
        powercfg /change standby-timeout-ac 0 | Out-Null
        powercfg /change hibernate-timeout-ac 0 | Out-Null
        powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 0 | Out-Null
        powercfg /setactive SCHEME_CURRENT | Out-Null
        Write-Ok 'sin suspension ni hibernacion con corriente; tapa cerrada = no hacer nada'
    }
}

# ----------------------------------------------------------------------------------------------------- 6. opcionales
if ($TrustCaHere) {
    Write-Step 'Confiar en la CA de Caddy en este equipo'
    $unc = "\\wsl.localhost\$Distro" + ((Invoke-Wsl "readlink -f $repoExpr/caddy-root-ca.crt") -join '').Trim() -replace '/', '\'
    if ($DryRun) { Write-Dry "Import-Certificate $unc -> Cert:\LocalMachine\Root" }
    elseif (-not (Test-Path $unc)) { Write-Warn2 "No se encontro $unc (instalar primero)." }
    else { Import-Certificate -FilePath $unc -CertStoreLocation Cert:\LocalMachine\Root | Out-Null; Write-Ok 'CA instalada en Entidades de certificacion raiz de confianza' }
}
if (-not [string]::IsNullOrWhiteSpace($HostsIp)) {
    Write-Step "Archivo hosts: $HostsIp $Domain"
    if ($HostsIp -notmatch '^\d{1,3}(\.\d{1,3}){3}$') { Fail '-HostsIp invalida' }
    $hostsFile = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
    $entry = "$HostsIp $Domain"
    if ($DryRun) { Write-Dry "agregar '$entry' a $hostsFile" }
    else {
        $lines = @(Get-Content $hostsFile | Where-Object { $_ -notmatch "(^|\s)$([regex]::Escape($Domain))\s*$" })
        $lines += $entry
        Set-Content -Path $hostsFile -Value $lines -Encoding ASCII
        Write-Ok "hosts actualizado: $entry"
    }
}

Write-Host ''
Write-Host "Listo. ERP: https://$Domain  | Guia: docs/instalacion-servidor.md" -ForegroundColor Green
if ($DryRun) { Write-Host '(dry-run: no se modifico nada)' -ForegroundColor DarkGray }
