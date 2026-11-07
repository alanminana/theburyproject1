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
    }
}
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

# 20. Salida -Json parseable
$s = New-Base; $script:Sim = $s; $s.LiveCode = '200'
Invoke-HealthCheck
$Json = [switch]$true
$salida = @(Show-Resultados)
$Json = [switch]$false
$txt = ($salida | Where-Object { $_ -is [string] }) -join "`n"
try {
    $parsed = $txt | ConvertFrom-Json
    if ($parsed.codigo -eq 0 -and $parsed.resultados.Count -gt 5) { Write-Host '[PASA ] salida -Json parseable' -ForegroundColor Green } else { throw 'contenido inesperado' }
} catch { $script:Fallos++; Write-Host "[FALLA] salida -Json: $($_.Exception.Message)" -ForegroundColor Red }

Write-Host ''
if ($script:Fallos -eq 0) { Write-Host 'Todos los escenarios simulados pasaron.' -ForegroundColor Green; exit 0 }
Write-Host "$($script:Fallos) escenario(s) fallaron." -ForegroundColor Red; exit 1
