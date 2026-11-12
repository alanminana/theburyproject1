<#
.SYNOPSIS
  Pruebas SIMULADAS de Check-ErpHealth.ps1 (no tocan Docker, WSL, Tailscale, tareas ni red reales).

.DESCRIPTION
  Carga Check-ErpHealth.ps1 con dot-source y reemplaza las funciones de acceso al sistema (Invoke-Native, Get-DiskInfo,
  Get-TaskInfoList, ...) por escenarios sinteticos. Verifica estado y codigo de salida de cada escenario.
  Compatible con Windows PowerShell 5.1. Codigo de salida: 0 si todos los escenarios pasan, 1 si alguno falla.
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'Check-ErpHealth.ps1')

$script:Fallos = 0
$script:Sim = $null

# --- Escenario base: todo sano --------------------------------------------------------------------------------------
$ahoraUnix = [int64](([datetimeoffset]::new([datetime]::UtcNow, [timespan]::Zero)).ToUnixTimeSeconds())
function New-Base {
    @{
        NoDocker = $false; DockerDown = $false; NoCurl = $false; WslFail = $false
        DockerVersion = '27.0.1'
        Ps = @("db|running|Up 3 days (healthy)", "app|running|Up 3 days (healthy)", "caddy|running|Up 3 days",
               "db-init|exited|Exited (0) 3 days ago", "migrate|exited|Exited (0) 3 days ago")
        Live = '|200|0.011'; LiveBody = 'Healthy'; Ready = 'Healthy'; ReadyCode = '200'
        CurlTrusted = $true; CurlExit = 0
        Disk = [pscustomobject]@{ Drive = 'C:'; FreeGB = 180.0; SizeGB = 500.0 }
        Reboot = @()
        Status = @{
            'full'         = (@{ time = $ahoraUnix - 4 * 3600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
            'log'          = (@{ time = $ahoraUnix - 600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
            'files'        = (@{ time = $ahoraUnix - 4 * 3600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
            'restore-test' = (@{ time = $ahoraUnix - 2 * 86400; exit = 0; verified = $true; last_success = $ahoraUnix - 2 * 86400; message = 'restore-test OK' } | ConvertTo-Json -Compress)
            'offsite-sql'  = (@{ time = $ahoraUnix - 4 * 3600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
        }
        StatusOk = $true
        Tasks = @(
            [pscustomobject]@{ Name = 'TheBury Backup Full'; State = 'Ready'; LastRun = (Get-Date).AddHours(-4); LastResult = 0; NextRun = (Get-Date).AddHours(20) },
            [pscustomobject]@{ Name = 'TheBury Backup Log'; State = 'Ready'; LastRun = (Get-Date).AddMinutes(-10); LastResult = 0; NextRun = (Get-Date).AddMinutes(5) })
        TsService = [pscustomobject]@{ State = 'Running' }
        TsJson = (@{ BackendState = 'Running'; Self = @{ KeyExpiry = ([datetime]::UtcNow.AddDays(120).ToString('o')) } } | ConvertTo-Json -Depth 4)
    }
}

# --- Dobles de las funciones de acceso al sistema -------------------------------------------------------------------
function Invoke-Native([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec = 20) {
    $s = $script:Sim
    function New-Res($found, $code, $out) { [pscustomobject]@{ Found = $found; ExitCode = $code; Output = $out } }
    switch -Regex ($Exe) {
        '^docker$' {
            if ($s.NoDocker) { return (New-Res $false -1 '') }
            if ($s.DockerDown) { return (New-Res $true 1 'error during connect') }
            if ($Arguments[0] -eq 'version') { return (New-Res $true 0 $s.DockerVersion) }
            return (New-Res $true 0 ($s.Ps -join "`n"))
        }
        '^curl' {
            if ($s.NoCurl) { return (New-Res $false -1 '') }
            $script:LastCurl = $Arguments -join ' '
            if ($Arguments -contains '-k' -or $Arguments -contains '-sk') {
                if ($s.CurlExit -ne 0) { return (New-Res $true $s.CurlExit '') }
                $url = $Arguments[-1]
                if ($url -like '*live') { return (New-Res $true 0 ($s.LiveBody + '|' + $s.LiveCode + '|0.011')) }
                return (New-Res $true 0 ($s.Ready + '|' + $s.ReadyCode + '|0.250'))
            }
            return (New-Res $true $(if ($s.CurlTrusted) { 0 } else { 60 }) '')
        }
        '^wsl' {
            if ($s.WslFail) { return (New-Res $true 1 '') }
            $o = ''; foreach ($k in $s.Status.Keys) { $o += "@@$k.json`n" + $s.Status[$k] + "`n`n" }
            return (New-Res $true 0 $o)
        }
        'tailscale' {
            if ($s.TsJson -eq $null) { return (New-Res $false -1 '') }
            return (New-Res $true 0 $s.TsJson)
        }
        '^ssh$' {
            # Modo VM: el ultimo argumento es el comando remoto ya entrecomillado por Invoke-Tool.
            $remote = $Arguments[-1]
            $script:SshCalls += $remote
            if ($s.SshDown) { return (New-Res $true 255 'ssh: connect to host timed out') }
            switch -Regex ($remote) {
                "^'docker' 'version'" { if ($s.DockerDown) { return (New-Res $true 1 'Cannot connect to the Docker daemon') }; return (New-Res $true 0 $s.DockerVersion) }
                "^'docker' 'ps'" { return (New-Res $true 0 ($s.Ps -join "`n")) }
                "^'systemctl' 'is-active' 'docker'" { return (New-Res $true 0 ($s.VmActive -join "`n")) }
                "^'systemctl' 'is-enabled' 'docker'" { return (New-Res $true 0 ($s.VmEnabled -join "`n")) }
                "^'test' '-f'" { return (New-Res $true $(if ($s.VmCron) { 0 } else { 1 }) '') }
                "^'df'" { return (New-Res $true 0 ("Avail`n " + $s.VmDiskGB + 'G')) }
                "^'sh' '-c'" {
                    if ($s.WslFail) { return (New-Res $true 1 '') }
                    $o = ''; foreach ($k in $s.Status.Keys) { $o += "@@$k.json`n" + $s.Status[$k] + "`n`n" }
                    return (New-Res $true 0 $o)
                }
                "^'tailscale' 'version'" { return (New-Res $true $(if ($s.VmNoTs) { 127 } else { 0 }) '') }
                "^'systemctl' 'is-active' 'tailscaled'" { return (New-Res $true 0 $s.VmTsActive) }
                "^'tailscale' 'status'" { return (New-Res $true 0 $s.TsJson) }
                default { return (New-Res $true 1 "comando remoto no simulado: $remote") }
            }
        }
    }
}
function Get-VmInfo { if ($script:Sim.VmInfo -eq 'NOMOD') { return $null }; if ($script:Sim.VmInfo -eq 'ERROR') { throw 'The operation failed' }; $script:Sim.VmInfo }
function Get-DiskInfo { $script:Sim.Disk }
function Get-BootTime { (Get-Date).AddDays(-9) }
function Get-PendingRebootInfo { $script:Sim.Reboot }
function Get-ServiceInfo([string]$Name) { $script:Sim.TsService }
function Get-TaskInfoList { $script:Sim.Tasks }

function Invoke-Escenario([string]$Nombre, [hashtable]$Sim, [int]$CodigoEsperado, [string[]]$Esperados) {
    $script:Sim = $Sim
    if (-not $Sim.ContainsKey('LiveCode')) { $Sim.LiveCode = '200' }
    $script:BackupDir = '/srv/bury-backups'
    Invoke-HealthCheck
    $f = @($script:Resultados | Where-Object Estado -eq 'FALLA').Count
    $a = @($script:Resultados | Where-Object Estado -eq 'ATENCION').Count
    $codigo = if ($f -gt 0) { 2 } elseif ($a -gt 0) { 1 } else { 0 }
    $errores = @()
    if ($codigo -ne $CodigoEsperado) { $errores += "codigo=$codigo esperado=$CodigoEsperado" }
    foreach ($e in $Esperados) {
        # formato 'ESTADO|item-regex'
        $est, $rx = $e -split '\|', 2
        if (-not ($script:Resultados | Where-Object { $_.Estado -eq $est -and $_.Item -match $rx })) { $errores += "falta [$est] $rx" }
    }
    if ($errores.Count -eq 0) { Write-Host "[PASA ] $Nombre (codigo $codigo)" -ForegroundColor Green }
    else {
        $script:Fallos++
        Write-Host "[FALLA] $Nombre -> $($errores -join '; ')" -ForegroundColor Red
        $script:Resultados | Where-Object { $_.Estado -in 'FALLA', 'ATENCION' } | ForEach-Object { Write-Host "         $($_.Estado) $($_.Item): $($_.Detalle)" }
    }
}

# 1. Todo sano
$s = New-Base
Invoke-Escenario 'Todo sano' $s 0 @('OK|Docker daemon', 'OK|Contenedor app', 'OK|HTTPS /health/ready', 'OK|Backup full', 'OK|Prueba de restore', 'OK|Tailscale conectado', "OK|Tarea 'TheBury Backup Log'")

# 2. Docker apagado
$s = New-Base; $s.DockerDown = $true
Invoke-Escenario 'Docker Desktop apagado' $s 2 @('FALLA|Docker daemon', 'INFO|HTTPS local')

# 3. Docker no instalado
$s = New-Base; $s.NoDocker = $true
Invoke-Escenario 'docker.exe ausente' $s 2 @('FALLA|Docker CLI')

# 4. Contenedor app unhealthy y caddy caido
$s = New-Base; $s.Ps = @("db|running|Up 3 days (healthy)", "app|running|Up 3 days (unhealthy)", "caddy|exited|Exited (1) 2 minutes ago", "migrate|exited|Exited (0) 3 days ago")
Invoke-Escenario 'app unhealthy + caddy caido' $s 2 @('FALLA|Contenedor app', 'FALLA|Contenedor caddy', 'OK|Contenedor db')

# 5. migrate fallo
$s = New-Base; $s.Ps = @("db|running|Up 1 hour (healthy)", "app|running|Up 1 hour (health: starting)", "caddy|running|Up 1 hour", "migrate|exited|Exited (1) 1 hour ago")
Invoke-Escenario 'migrate fallido, app iniciando' $s 2 @('FALLA|Tarea unica migrate', 'ATENCION|Contenedor app')

# 6. Stack sin contenedores
$s = New-Base; $s.Ps = @()
Invoke-Escenario 'proyecto sin contenedores' $s 2 @('FALLA|Proyecto')

# 7. /health/ready 503
$s = New-Base; $s.Ready = 'Unhealthy'; $s.ReadyCode = '503'
Invoke-Escenario 'ready 503 (SQL caido)' $s 2 @('OK|HTTPS /health/live', 'FALLA|HTTPS /health/ready')

# 8. HTTPS sin respuesta
$s = New-Base; $s.CurlExit = 7
Invoke-Escenario 'HTTPS sin respuesta' $s 2 @('FALLA|HTTPS /health/live')

# 9. CA no confiable (solo informativo)
$s = New-Base; $s.CurlTrusted = $false
Invoke-Escenario 'CA de Caddy no instalada' $s 0 @('INFO|Certificado de Caddy no confiable')

# 10. Backup full viejo + log fallido
$s = New-Base
$s.Status['full'] = (@{ time = $ahoraUnix - 50 * 3600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
$s.Status['log'] = (@{ time = $ahoraUnix - 300; exit = 6; verified = $false } | ConvertTo-Json -Compress)
Invoke-Escenario 'full desactualizado y log fallido' $s 2 @('FALLA|Backup full', 'FALLA|Backup log', 'OK|Backup files')

# 10b. log atrasado: 40 min = atencion, 50 min = falla
$s = New-Base; $s.Status['log'] = (@{ time = $ahoraUnix - 40 * 60; exit = 0; verified = $true } | ConvertTo-Json -Compress)
Invoke-Escenario 'log verificado hace 40 min' $s 1 @('ATENCION|Backup log')
$s = New-Base; $s.Status['log'] = (@{ time = $ahoraUnix - 50 * 60; exit = 0; verified = $true } | ConvertTo-Json -Compress)
Invoke-Escenario 'log verificado hace 50 min' $s 2 @('FALLA|Backup log')

# 10c. offsite viejo (>30 h) = falla; tarea Ready con ultima corrida vieja y sin proxima = falla
$s = New-Base; $s.Status['offsite-sql'] = (@{ time = $ahoraUnix - 40 * 3600; exit = 0; verified = $true } | ConvertTo-Json -Compress)
Invoke-Escenario 'offsite desactualizado' $s 2 @('FALLA|Copia offsite-sql')
$s = New-Base; $s.Tasks = @([pscustomobject]@{ Name = 'TheBury Backup Log'; State = 'Ready'; LastRun = (Get-Date).AddDays(-60); LastResult = 0; NextRun = $null })
Invoke-Escenario 'tarea Ready sin correr hace meses' $s 2 @("FALLA|Backup Log")
$s = New-Base; $s.Tasks = @([pscustomobject]@{ Name = 'TheBury Backup Full'; State = 'Ready'; LastRun = (Get-Date).AddHours(-3); LastResult = 0; NextRun = $null })
Invoke-Escenario 'tarea sin proxima ejecucion' $s 1 @("ATENCION|Backup Full")

# 11. Sin monitor-status (scheduler nunca instalado)
$s = New-Base; $s.Status = @{}
Invoke-Escenario 'backups nunca corrieron' $s 2 @('FALLA|Estado de backups')

# 12. WSL no responde
$s = New-Base; $s.WslFail = $true
Invoke-Escenario 'wsl.exe falla al leer backups' $s 1 @('ATENCION|Estado de backups')

# 13. Restore-test viejo, sin offsite, corrida run-all fallida posterior
$s = New-Base
$s.Status['restore-test'] = (@{ time = $ahoraUnix - 20 * 86400; exit = 0; verified = $true; last_success = $ahoraUnix - 20 * 86400; message = 'ok' } | ConvertTo-Json -Compress)
$s.Status.Remove('offsite-sql')
$s.Status['run-full'] = (@{ time = $ahoraUnix - 600; exit = 6; verified = $false } | ConvertTo-Json -Compress)
Invoke-Escenario 'restore viejo, sin offsite, run-full posterior fallido' $s 2 @('ATENCION|Prueba de restore', 'ATENCION|Copia offsite', "FALLA|Corrida de backup 'full'")

# 14. Offsite fallo
$s = New-Base
$s.Status['offsite-sql'] = (@{ time = $ahoraUnix - 600; exit = 7; verified = $false } | ConvertTo-Json -Compress)
Invoke-Escenario 'offsite fallido' $s 2 @('FALLA|Copia offsite-sql')

# 15. Disco bajo y reinicio pendiente
$s = New-Base; $s.Disk = [pscustomobject]@{ Drive = 'C:'; FreeGB = 12.0; SizeGB = 256.0 }; $s.Reboot = @('Windows Update')
Invoke-Escenario 'disco bajo + reinicio pendiente' $s 2 @('FALLA|Disco', 'ATENCION|Reinicio')

# 16. Disco justo (atencion)
$s = New-Base; $s.Disk = [pscustomobject]@{ Drive = 'C:'; FreeGB = 30.0; SizeGB = 256.0 }
Invoke-Escenario 'disco en zona de atencion' $s 1 @('ATENCION|Disco')

# 17. Tareas: deshabilitada, fallida, nunca corrio
$s = New-Base
$s.Tasks = @(
    [pscustomobject]@{ Name = 'TheBury Backup Full'; State = 'Disabled'; LastRun = (Get-Date).AddDays(-3); LastResult = 0; NextRun = $null },
    [pscustomobject]@{ Name = 'TheBury Backup Log'; State = 'Ready'; LastRun = (Get-Date).AddMinutes(-3); LastResult = 1; NextRun = (Get-Date).AddMinutes(12) },
    [pscustomobject]@{ Name = 'TheBury Restore Test'; State = 'Ready'; LastRun = [datetime]'1999-11-30'; LastResult = 267011; NextRun = (Get-Date).AddDays(1) })
Invoke-Escenario 'tareas deshabilitada / fallida / sin correr' $s 2 @("FALLA|Backup Full", "FALLA|Backup Log", "ATENCION|Restore Test")

# 18. Sin tareas
$s = New-Base; $s.Tasks = @()
Invoke-Escenario 'sin tareas programadas' $s 1 @('ATENCION|Tareas programadas')

# 19. Tailscale: detenido / desconectado / clave por vencer / no instalado
$s = New-Base; $s.TsService = [pscustomobject]@{ State = 'Stopped' }
Invoke-Escenario 'servicio Tailscale detenido' $s 2 @('FALLA|Servicio Tailscale')
$s = New-Base; $s.TsJson = (@{ BackendState = 'NeedsLogin'; Self = @{ KeyExpiry = ([datetime]::UtcNow.AddDays(-2).ToString('o')) } } | ConvertTo-Json -Depth 4)
Invoke-Escenario 'Tailscale NeedsLogin + clave vencida' $s 2 @('FALLA|Tailscale desconectado', 'FALLA|Clave del nodo')
$s = New-Base; $s.TsJson = (@{ BackendState = 'Running'; Self = @{ KeyExpiry = ([datetime]::UtcNow.AddDays(10).ToString('o')) } } | ConvertTo-Json -Depth 4)
Invoke-Escenario 'clave Tailscale vence en 10 dias' $s 1 @('ATENCION|Clave del nodo')
$s = New-Base; $s.TsService = $null
Invoke-Escenario 'Tailscale no instalado' $s 1 @('ATENCION|Tailscale')

# ====================== Modo Windows Server (-VmHost): Docker/backups/systemd por SSH, VM por Hyper-V ======================
$script:SshCalls = @()
function New-BaseVm {
    $b = New-Base
    $b.VmActive = @('active', 'active', 'active'); $b.VmEnabled = @('enabled', 'enabled', 'enabled'); $b.VmCron = $true; $b.VmDiskGB = 62
    $b.VmNoTs = $false; $b.VmTsActive = 'active'; $b.SshDown = $false
    $b.VmInfo = [pscustomobject]@{ State = 'Running'; Uptime = [timespan]::FromHours(30); Heartbeat = 'Ok'; AutoStart = 'Start'; AutoStop = 'ShutDown'; Checkpoints = 'Disabled' }
    $b
}
$VmHost = '192.168.1.50'; $VmUser = 'bury'; $VmKey = 'C:\k\id'; $VmKnownHosts = 'C:\k\known_hosts'
$script:VmMode = $true

$s = New-BaseVm
Invoke-Escenario 'VM: todo sano' $s 0 @('OK|^VM ', 'OK|Arranque automatico', 'OK|Apagado de la VM', 'OK|Checkpoints', 'OK|Servicio docker \(en ejecucion\)', 'OK|Servicio bury-firewall \(habilitado', 'OK|Cron de backups', 'OK|Disco de la VM', 'OK|Docker daemon', 'OK|HTTPS /health/ready', 'OK|Backup full', 'OK|Tailscale conectado')
if ($script:LastCurl -notmatch 'tbp:443:192\.168\.1\.50') { $script:Fallos++; Write-Host "[FALLA] VM: el HTTPS debe resolver a la IP de la VM (curl: $script:LastCurl)" -ForegroundColor Red } else { Write-Host '[PASA ] VM: HTTPS apunta a la IP de la VM' -ForegroundColor Green }
if (@($script:Resultados | Where-Object Item -match "^Tarea '").Count -ne 0) { $script:Fallos++; Write-Host '[FALLA] VM: no deben evaluarse tareas programadas de Windows' -ForegroundColor Red } else { Write-Host '[PASA ] VM: sin chequeo de tareas de Windows' -ForegroundColor Green }
if (-not (@($script:SshCalls) | Where-Object { $_ -match "^'docker' 'ps'" })) { $script:Fallos++; Write-Host '[FALLA] VM: docker debe consultarse por SSH' -ForegroundColor Red } else { Write-Host '[PASA ] VM: docker consultado por SSH' -ForegroundColor Green }
if (@($script:SshCalls | Where-Object { $_ -match "rm |restart|stop|start|up |down " }).Count -ne 0) { $script:Fallos++; Write-Host '[FALLA] VM: se ejecuto un comando que no es de solo lectura' -ForegroundColor Red } else { Write-Host '[PASA ] VM: todos los comandos SSH son de solo lectura' -ForegroundColor Green }

$s = New-BaseVm; $s.SshDown = $true
Invoke-Escenario 'VM: SSH sin conexion (VM apagada o clave mala)' $s 2 @('FALLA|SSH a la VM', 'FALLA|Docker daemon')

$s = New-BaseVm; $s.VmInfo = [pscustomobject]@{ State = 'Off'; Uptime = [timespan]::Zero; Heartbeat = ''; AutoStart = 'Start'; AutoStop = 'ShutDown'; Checkpoints = 'Disabled' }
Invoke-Escenario 'VM: apagada en Hyper-V' $s 2 @("FALLA|^VM ")

$s = New-BaseVm; $s.VmInfo = [pscustomobject]@{ State = 'Running'; Uptime = [timespan]::FromHours(2); Heartbeat = 'LostCommunication'; AutoStart = 'Nothing'; AutoStop = 'Save'; Checkpoints = 'Standard' }
Invoke-Escenario 'VM: sin latido, sin autoarranque, Save y checkpoints' $s 2 @('FALLA|Latido', 'FALLA|Arranque automatico', 'ATENCION|Apagado de la VM', 'ATENCION|Checkpoints')

$s = New-BaseVm; $s.VmInfo = [pscustomobject]@{ State = 'Running'; Uptime = [timespan]::FromHours(5); Heartbeat = ''; AutoStart = 'Start'; AutoStop = 'ShutDown'; Checkpoints = 'Disabled' }
Invoke-Escenario 'VM: servicio Heartbeat ilegible' $s 1 @('ATENCION|Latido')
$s = New-BaseVm; $s.VmInfo = 'NOMOD'
Invoke-Escenario 'VM: modulo Hyper-V ausente (chequeo desde otro equipo)' $s 0 @('INFO|VM Hyper-V', 'OK|Docker daemon')
$s = New-BaseVm; $s.VmInfo = 'ERROR'
Invoke-Escenario 'VM: Get-VM falla' $s 1 @('ATENCION|VM Hyper-V')

$s = New-BaseVm; $s.VmActive = @('active', 'inactive', 'active'); $s.VmEnabled = @('enabled', 'enabled', 'disabled'); $s.VmCron = $false
Invoke-Escenario 'VM: cron detenido, firewall sin habilitar, sin cron.d' $s 2 @('FALLA|Servicio cron \(en ejecucion\)', 'FALLA|Servicio bury-firewall \(habilitado', 'FALLA|Cron de backups')

$s = New-BaseVm; $s.VmDiskGB = 15
Invoke-Escenario 'VM: disco del invitado bajo' $s 2 @('FALLA|Disco de la VM')
$s = New-BaseVm; $s.VmDiskGB = 30
Invoke-Escenario 'VM: disco del invitado en atencion' $s 1 @('ATENCION|Disco de la VM')

$s = New-BaseVm; $s.DockerDown = $true
Invoke-Escenario 'VM: docker detenido dentro de la VM' $s 2 @('FALLA|Docker daemon', 'INFO|HTTPS local')

$s = New-BaseVm; $s.Status = @{}
Invoke-Escenario 'VM: backups nunca corrieron' $s 2 @('FALLA|Estado de backups')
$s = New-BaseVm; $s.WslFail = $true
Invoke-Escenario 'VM: no se pueden leer los backups por SSH' $s 1 @('ATENCION|Estado de backups')

$s = New-BaseVm; $s.VmNoTs = $true
Invoke-Escenario 'VM: Tailscale no instalado en la VM' $s 1 @('ATENCION|Tailscale \(VM\)')
$s = New-BaseVm; $s.VmTsActive = 'inactive'
Invoke-Escenario 'VM: tailscaled detenido' $s 2 @('FALLA|Servicio tailscaled')
$s = New-BaseVm; $s.TsJson = (@{ BackendState = 'NeedsLogin' } | ConvertTo-Json)
Invoke-Escenario 'VM: Tailscale sin autenticar' $s 2 @('FALLA|Tailscale desconectado')

# entrada peligrosa en -VmHost: el script real la rechaza (proceso hijo)
$hijo = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'Check-ErpHealth.ps1') -VmHost '1.2.3.4; calc' 2>&1 | Out-String
if ($LASTEXITCODE -eq 3) { Write-Host '[PASA ] -VmHost con caracteres peligrosos se rechaza (exit 3)' -ForegroundColor Green } else { $script:Fallos++; Write-Host "[FALLA] -VmHost peligroso no rechazado (exit $LASTEXITCODE)" -ForegroundColor Red }

# vuelta al modo Windows 11 para el escenario siguiente
$script:VmMode = $false

# 20. Salida -Json parseable
$s = New-Base; $script:Sim = $s; $s.LiveCode = '200'
Invoke-HealthCheck
$Json = [switch]$true
Show-Resultados | Out-Null
$salida = @(Show-Resultados)
if ($script:Codigo -ne 0) { $script:Fallos++; Write-Host '[FALLA] Show-Resultados no dejo codigo 0' -ForegroundColor Red }
$Json = [switch]$false
$txt = ($salida | Where-Object { $_ -is [string] }) -join "`n"
try {
    $parsed = $txt | ConvertFrom-Json
    if ($parsed.codigo -eq 0 -and $parsed.resultados.Count -gt 5) { Write-Host '[PASA ] salida -Json parseable' -ForegroundColor Green } else { throw 'contenido inesperado' }
} catch { $script:Fallos++; Write-Host "[FALLA] salida -Json: $($_.Exception.Message)" -ForegroundColor Red }

Write-Host ''
if ($script:Fallos -eq 0) { Write-Host 'Todos los escenarios simulados pasaron.' -ForegroundColor Green; exit 0 }
Write-Host "$($script:Fallos) escenario(s) fallaron." -ForegroundColor Red; exit 1
