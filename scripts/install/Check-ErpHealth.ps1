<#
.SYNOPSIS
  Chequeo de salud SOLO LECTURA de un servidor TheBuryProject en Windows 11 + Docker Desktop (WSL2) + Tailscale (P-08).

.DESCRIPTION
  Pensado para correr a mano o desde el Programador de tareas DESPUES de instalar (para antes de instalar usar
  preflight-windows.ps1). No arranca, detiene ni repara nada: no escribe archivos, no toca registro, firewall, tareas,
  servicios, Docker, WSL ni la base de datos. Solo consulta y reporta OK / ATENCION / FALLA / INFO.

  Que mira:
    1. Equipo: disco libre, ultimo arranque, reinicio pendiente.
    2. Docker Desktop: daemon y contenedores del proyecto (db, app, caddy en ejecucion y sanos; migrate/db-init terminados con exito).
    3. ERP por HTTPS local: /health/live y /health/ready (cuerpo 'Healthy'); confianza del certificado de Caddy.
    4. Backups: monitor-status/*.json que escribe scripts/backup (edad y 'verified' de full/log/files, ultima restore-test, offsite).
    5. Tareas programadas 'TheBury*': habilitadas, ultimo resultado, edad de la ultima corrida.
    6. Tailscale: servicio, estado del backend, IP, expiracion de clave.

  Codigo de salida (apto para el Programador de tareas / un observador externo):
    0 = todo OK (puede haber INFO)      1 = hay ATENCION pero ninguna FALLA      2 = hay al menos una FALLA

  Limites honestos: un chequeo local no avisa si el servidor entero esta caido (para eso hace falta un observador en OTRO sitio),
  y que la copia offsite sea recuperable solo lo prueba un restore real. El chequeo de backups lee el directorio dentro de WSL
  (-BackupDir) o un directorio accesible desde Windows (-StatusDir).

.PARAMETER Dominio
  ERP_DOMAIN (nombre con el que Caddy sirve el sitio). Por defecto 'tbp'. Se usa como SNI/Host contra 127.0.0.1.

.PARAMETER Puerto
  ERP_HTTPS_PORT publicado en el host. Por defecto 443.

.PARAMETER Proyecto
  Nombre del proyecto de Docker Compose. Por defecto 'theburyproject'.

.PARAMETER Distro
  Distro de WSL donde viven los backups. Vacio = distro predeterminada de WSL.

.PARAMETER BackupDir
  Directorio de backups DENTRO de WSL (el mismo -BackupDir de install-windows.ps1). Por defecto /srv/bury-backups.

.PARAMETER StatusDir
  Alternativa: directorio de Windows (o UNC \\wsl$\...) que contiene los .json de monitor-status. Si se indica, no se invoca wsl.exe.

.PARAMETER VmHost
  Modo Windows Server (Hyper-V): IPv4 de la VM Linux que corre el ERP. Docker, backups, systemd y Tailscale se consultan por SSH
  (solo lectura) y el HTTPS se prueba contra esa IP. Sin -VmHost el modo es Windows 11 + Docker Desktop (WSL2). Ver docs/instalacion-windows-server.md.

.PARAMETER VmName
  Nombre de la VM en Hyper-V (por defecto TheBury-ERP): se verifica estado, latido, arranque automatico y checkpoints (requiere el modulo Hyper-V).

.PARAMETER VmUser
  Usuario SSH de la VM (por defecto bury).

.PARAMETER VmKey
  Clave privada SSH (la que crea install-windows-server.ps1 en C:\ProgramData\TheBury\server\bury_vm_ed25519).

.PARAMETER VmKnownHosts
  Archivo known_hosts dedicado (C:\ProgramData\TheBury\server\known_hosts).

.PARAMETER Json
  Emite el resultado como JSON por la salida estandar (en lugar de la tabla de texto).

.PARAMETER DiscoMinGB
  Espacio libre minimo del disco del sistema (por debajo: FALLA; por debajo de 2x: ATENCION). Por defecto 20.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Check-ErpHealth.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Check-ErpHealth.ps1 -Json
#>
[CmdletBinding()]
param(
    [string]$Dominio = 'tbp',
    [int]$Puerto = 443,
    [string]$Proyecto = 'theburyproject',
    [string]$Distro = '',
    [string]$BackupDir = '/srv/bury-backups',
    [string]$StatusDir = '',
    [string]$VmHost = '',
    [string]$VmName = 'TheBury-ERP',
    [string]$VmUser = 'bury',
    [string]$VmKey = (Join-Path $env:ProgramData 'TheBury\server\bury_vm_ed25519'),
    [string]$VmKnownHosts = (Join-Path $env:ProgramData 'TheBury\server\known_hosts'),
    [switch]$Json,
    [int]$DiscoMinGB = 20
)

if ($VmHost -and $VmHost -notmatch '^(\d{1,3}\.){3}\d{1,3}$') { Write-Error '-VmHost invalido: debe ser la IPv4 de la VM (curl --resolve exige una IP)'; exit 3 }

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Continue'

# Umbrales (segundos) de frescura de los backups. Coinciden con el calendario de install-windows.ps1
# (full diario 02:30, log cada 15 min, restore-test semanal); el margen cubre una corrida omitida.
$script:MaxEdad = @{ 'full' = 30 * 3600; 'files' = 30 * 3600; 'log' = 45 * 60; 'restore-test' = 8 * 86400 }
# Umbral previo a FALLA (coincide con scripts/monitoring/config.example.json: log 30 min aviso / 45 min critico).
$script:AvisoEdad = @{ 'log' = 30 * 60 }
$script:Codigo = 0
$script:VmMode = [bool]$VmHost
$script:Resultados = New-Object System.Collections.Generic.List[object]

function Add-R([string]$Estado, [string]$Item, [string]$Detalle) {
    $script:Resultados.Add([pscustomobject]@{ Estado = $Estado; Item = $Item; Detalle = $Detalle })
}
function Safe([scriptblock]$b) { try { & $b } catch { $null } }

# --- Acceso al sistema (funciones finas: se reemplazan en las pruebas simuladas) ----------------------------------
# Ejecuta un binario nativo con tiempo maximo; devuelve @{ Found; ExitCode; Output }. Nunca lanza.
function Invoke-Native([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec = 20) {
    $cmd = Get-Command $Exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $cmd) { return [pscustomobject]@{ Found = $false; ExitCode = -1; Output = '' } }
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $cmd.Source
        $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $outTask = $p.StandardOutput.ReadToEndAsync()
        $errTask = $p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit($TimeoutSec * 1000)) {
            try { $p.Kill() } catch { }
            return [pscustomobject]@{ Found = $true; ExitCode = -2; Output = 'timeout' }
        }
        $p.WaitForExit()
        return [pscustomobject]@{ Found = $true; ExitCode = $p.ExitCode; Output = ($outTask.Result + $errTask.Result) }
    } catch {
        return [pscustomobject]@{ Found = $true; ExitCode = -3; Output = $_.Exception.Message }
    }
}
function Get-DiskInfo { $d = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($env:SystemDrive)'"; [pscustomobject]@{ Drive = $d.DeviceID; FreeGB = [math]::Round($d.FreeSpace / 1GB, 1); SizeGB = [math]::Round($d.Size / 1GB, 1) } }
function Get-BootTime { (Get-CimInstance Win32_OperatingSystem).LastBootUpTime }
function Get-PendingRebootInfo {
    $p = @()
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $p += 'Windows Update' }
    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $p += 'CBS' }
    $p
}
function Get-ServiceInfo([string]$Name) { Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue }
function Get-TaskInfoList {
    foreach ($t in @(Get-ScheduledTask -TaskName 'TheBury*' -ErrorAction Stop)) {
        $i = Get-ScheduledTaskInfo -TaskName $t.TaskName -TaskPath $t.TaskPath -ErrorAction SilentlyContinue
        [pscustomobject]@{ Name = $t.TaskName; State = "$($t.State)"; LastRun = $i.LastRunTime; LastResult = $i.LastTaskResult; NextRun = $i.NextRunTime }
    }
}
function Get-NowUtc { [datetime]::UtcNow }
function Get-VmInfo {
    if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) { return $null }
    $v = Get-VM -Name $VmName -ErrorAction Stop
    # Por Id (84EAAE65... = Heartbeat): el nombre del servicio y su descripcion estan localizados segun el idioma de Windows.
    $hb = @(Get-VMIntegrationService -VM $v -ErrorAction SilentlyContinue | Where-Object { "$($_.Id)" -like '*84EAAE65-2F2E-45F5-9BB5-0E857DC8EB47*' }) | Select-Object -First 1
    [pscustomobject]@{
        State = "$($v.State)"; Uptime = $v.Uptime; Heartbeat = $(if ($hb) { "$($hb.PrimaryOperationalStatus)" } else { '' })
        AutoStart = "$($v.AutomaticStartAction)"; AutoStop = "$($v.AutomaticStopAction)"; Checkpoints = "$($v.CheckpointType)"
    }
}

# Comillas de bash para pasar argumentos por SSH.
function Quote-Sh([string]$s) { return "'" + ($s -replace "'", "'\''") + "'" }
# Ejecuta una herramienta del servidor: local (Docker Desktop/WSL) o, en modo VM, por SSH de solo lectura dentro de la VM.
function Invoke-Tool([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec = 20) {
    if (-not $script:VmMode) { return (Invoke-Native $Exe $Arguments $TimeoutSec) }
    $remote = (@($Exe) + @($Arguments) | ForEach-Object { Quote-Sh $_ }) -join ' '
    $ssh = @('-i', $VmKey, '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', "UserKnownHostsFile=$VmKnownHosts", '-o', 'ConnectTimeout=8', '-o', 'LogLevel=ERROR', "$VmUser@$VmHost", $remote)
    return (Invoke-Native 'ssh' $ssh $TimeoutSec)
}

# --- 1. Equipo -----------------------------------------------------------------------------------------------------
function Test-Equipo {
    $disk = Safe { Get-DiskInfo }
    if ($disk) {
        $est = if ($disk.FreeGB -lt $DiscoMinGB) { 'FALLA' } elseif ($disk.FreeGB -lt (2 * $DiscoMinGB)) { 'ATENCION' } else { 'OK' }
        Add-R $est "Disco $($disk.Drive) libre" "$($disk.FreeGB) GB de $($disk.SizeGB) GB (minimo $DiscoMinGB GB; los volumenes de Docker y los backups consumen este disco)"
    } else { Add-R 'ATENCION' 'Disco del sistema' 'no se pudo consultar' }

    $boot = Safe { Get-BootTime }
    if ($boot) {
        $dias = [math]::Round(((Get-Date) - $boot).TotalDays, 1)
        Add-R 'INFO' 'Ultimo arranque de Windows' "$($boot.ToString('yyyy-MM-dd HH:mm')) (hace $dias dias)"
    }
    $pend = @(Safe { Get-PendingRebootInfo })
    if ($pend.Count -gt 0) { Add-R 'ATENCION' 'Reinicio de Windows pendiente' "($($pend -join ', ')): un reinicio fuera de horario corta el ERP; programarlo" }
    else { Add-R 'OK' 'Sin reinicio de Windows pendiente' 'ninguna marca de reinicio' }
}

# --- 2. Docker -----------------------------------------------------------------------------------------------------
function Test-Docker {
    $v = Invoke-Tool 'docker' @('version', '--format', '{{.Server.Version}}')
    if (-not $v.Found) { Add-R 'FALLA' 'Docker CLI' $(if ($script:VmMode) { 'ssh.exe no encontrado en el PATH' } else { 'docker no encontrado en el PATH' }); return $false }
    if ($v.ExitCode -ne 0 -or -not $v.Output.Trim()) { Add-R 'FALLA' 'Docker daemon' $(if ($script:VmMode) { 'no responde (VM apagada/sin SSH, servicio docker detenido o clave SSH incorrecta)' } else { 'no responde (Docker Desktop apagado, sin sesion iniciada o backend WSL2 caido)' }); return $false }
    Add-R 'OK' 'Docker daemon' "responde (servidor $($v.Output.Trim()))"

    $ps = Invoke-Tool 'docker' @('ps', '-a', '--filter', "label=com.docker.compose.project=$Proyecto", '--format', '{{.Label "com.docker.compose.service"}}|{{.State}}|{{.Status}}')
    if ($ps.ExitCode -ne 0) { Add-R 'FALLA' 'Contenedores del proyecto' "docker ps fallo: $($ps.Output.Trim())"; return $false }
    $filas = @($ps.Output -split "`r?`n" | Where-Object { $_ -match '\|' } | ForEach-Object {
            $c = $_ -split '\|', 3
            [pscustomobject]@{ Servicio = $c[0]; Estado = $c[1]; Detalle = $c[2] }
        })
    if ($filas.Count -eq 0) { Add-R 'FALLA' "Proyecto '$Proyecto'" 'sin contenedores: el stack no esta levantado (o -Proyecto no coincide)'; return $false }

    foreach ($svc in 'db', 'app', 'caddy') {
        $f = @($filas | Where-Object Servicio -eq $svc | Select-Object -First 1)
        if ($f.Count -eq 0) { Add-R 'FALLA' "Contenedor $svc" 'no existe'; continue }
        $f = $f[0]
        if ($f.Estado -ne 'running') { Add-R 'FALLA' "Contenedor $svc" "estado=$($f.Estado) ($($f.Detalle))"; continue }
        if ($f.Detalle -match '\(unhealthy\)') { Add-R 'FALLA' "Contenedor $svc" "en ejecucion pero UNHEALTHY ($($f.Detalle))" }
        elseif ($f.Detalle -match '\(health: starting\)') { Add-R 'ATENCION' "Contenedor $svc" "iniciando ($($f.Detalle))" }
        elseif ($f.Detalle -match '\(restarting\)|Restarting') { Add-R 'FALLA' "Contenedor $svc" "reiniciandose en bucle ($($f.Detalle))" }
        else { Add-R 'OK' "Contenedor $svc" $f.Detalle }
    }
    foreach ($svc in 'db-init', 'migrate') {
        $f = @($filas | Where-Object Servicio -eq $svc | Select-Object -First 1)
        if ($f.Count -eq 0) { continue }
        $f = $f[0]
        if ($f.Estado -eq 'exited' -and $f.Detalle -match 'Exited \(0\)') { Add-R 'OK' "Tarea unica $svc" 'termino con exito' }
        elseif ($f.Estado -eq 'exited') { Add-R 'FALLA' "Tarea unica $svc" "termino con error ($($f.Detalle))" }
        else { Add-R 'ATENCION' "Tarea unica $svc" "estado=$($f.Estado) ($($f.Detalle))" }
    }
    return $true
}

# --- 3. HTTPS local ------------------------------------------------------------------------------------------------
function Test-Http {
    $ipObjetivo = if ($script:VmMode) { $VmHost } else { '127.0.0.1' }
    $resolve = "$($Dominio):$($Puerto):$ipObjetivo"
    foreach ($ruta in '/health/live', '/health/ready') {
        $url = "https://$($Dominio):$($Puerto)$ruta"
        # -k: el certificado es de la CA interna de Caddy y puede no estar instalado en el servidor; se verifica aparte.
        $r = Invoke-Native 'curl.exe' @('-sk', '--max-time', '10', '--resolve', $resolve, '-o', '-', '-w', '|%{http_code}|%{time_total}', $url)
        if (-not $r.Found) { Add-R 'ATENCION' "HTTPS $ruta" 'curl.exe no disponible: no se pudo probar'; continue }
        if ($r.ExitCode -ne 0) { Add-R 'FALLA' "HTTPS $ruta" "sin respuesta en $url (curl exit $($r.ExitCode); Caddy/app caidos o puerto/dominio incorrectos)"; continue }
        $m = [regex]::Match($r.Output, '^(?<body>.*)\|(?<code>\d{3})\|(?<t>[\d.,]+)\s*$', 'Singleline')
        if (-not $m.Success) { Add-R 'ATENCION' "HTTPS $ruta" 'respuesta no interpretable'; continue }
        $body = $m.Groups['body'].Value.Trim(); $code = $m.Groups['code'].Value; $t = $m.Groups['t'].Value
        if ($code -eq '200' -and $body -eq 'Healthy') { Add-R 'OK' "HTTPS $ruta" "200 Healthy en ${t}s" }
        elseif ($code -eq '503' -or $body -eq 'Unhealthy') { Add-R 'FALLA' "HTTPS $ruta" "HTTP $code '$body' (la app reporta fallo; /health/ready incluye la conexion a SQL Server)" }
        else { Add-R 'FALLA' "HTTPS $ruta" "HTTP $code inesperado" }
    }
    # Certificado: sin -k, lo acepta este equipo? (solo informativo: los clientes necesitan instalar la CA de Caddy)
    $c = Invoke-Native 'curl.exe' @('-s', '--max-time', '10', '--resolve', $resolve, '-o', 'NUL', "https://$($Dominio):$($Puerto)/health/live")
    if ($c.Found -and $c.ExitCode -eq 0) { Add-R 'OK' 'Certificado de Caddy confiable en este equipo' 'la CA interna esta instalada' }
    elseif ($c.Found) { Add-R 'INFO' 'Certificado de Caddy no confiable en este equipo' "curl exit $($c.ExitCode): instalar la CA donde se use el navegador (docs/red-privada-lan-vpn.md)" }
}

# --- 4. Backups ----------------------------------------------------------------------------------------------------
# Devuelve @{ ok; error; archivos = @{ nombre = json-texto } }
function Get-BackupStatusFiles {
    if ($StatusDir) {
        if (-not (Test-Path -LiteralPath $StatusDir)) { return @{ ok = $false; error = "no existe $StatusDir"; archivos = @{} } }
        $h = @{}
        foreach ($f in Get-ChildItem -LiteralPath $StatusDir -Filter *.json -File -ErrorAction SilentlyContinue) { $h[$f.BaseName] = (Get-Content -LiteralPath $f.FullName -Raw) }
        return @{ ok = $true; error = ''; archivos = $h }
    }
    if ($BackupDir -notmatch '^/[A-Za-z0-9._/-]+$') { return @{ ok = $false; error = '-BackupDir invalido'; archivos = @{} } }
    $snippet = ('[ -d {0}/monitor-status ] && cd {0}/monitor-status && for f in *.json; do echo @@$f; cat $f; echo; done; exit 0' -f $BackupDir)
    if ($script:VmMode) {
        $r = Invoke-Tool 'sh' @('-c', $snippet) 30
        if (-not $r.Found) { return @{ ok = $false; error = 'ssh.exe no encontrado'; archivos = @{} } }
        if ($r.ExitCode -ne 0) { return @{ ok = $false; error = "ssh a la VM fallo (exit $($r.ExitCode))"; archivos = @{} } }
        return (ConvertFrom-StatusDump $r.Output)
    }
    if ($Distro -and $Distro -notmatch '^[A-Za-z0-9._-]+$') { return @{ ok = $false; error = '-Distro invalido'; archivos = @{} } }
    $wslArgs = @(); if ($Distro) { $wslArgs += @('-d', $Distro) }
    # Solo lectura: cat de los .json (sin comillas: -BackupDir ya se valido contra una lista de caracteres seguros).
    $wslArgs += @('--', 'sh', '-c', $snippet)
    $r = Invoke-Native 'wsl.exe' $wslArgs 30
    if (-not $r.Found) { return @{ ok = $false; error = 'wsl.exe no encontrado'; archivos = @{} } }
    if ($r.ExitCode -ne 0) { return @{ ok = $false; error = "wsl.exe fallo (exit $($r.ExitCode))"; archivos = @{} } }
    return (ConvertFrom-StatusDump $r.Output)
}
function ConvertFrom-StatusDump([string]$Texto) {
    $h = @{}; $actual = $null
    foreach ($l in ($Texto -replace "`0", '') -split "`r?`n") {
        if ($l -match '^@@(.+)\.json$' -and $Matches[1] -ne '*') { $actual = $Matches[1]; $h[$actual] = ''; continue }
        if ($actual -and $l.Trim()) { $h[$actual] += $l }
    }
    return @{ ok = $true; error = ''; archivos = $h }
}

function Format-Edad([double]$seg) {
    if ($seg -lt 3600) { return "$([math]::Round($seg / 60)) min" }
    if ($seg -lt 172800) { return "$([math]::Round($seg / 3600, 1)) h" }
    return "$([math]::Round($seg / 86400, 1)) dias"
}

function Test-Backups {
    $s = Get-BackupStatusFiles
    if (-not $s.ok) { Add-R 'ATENCION' 'Estado de backups' "no se pudo leer monitor-status: $($s.error)"; return }
    if ($s.archivos.Count -eq 0) { Add-R 'FALLA' 'Estado de backups' "monitor-status vacio o inexistente: los backups nunca corrieron (scheduler no instalado: docs/instalacion-servidor.md)"; return }
    $ahora = [int64](([datetimeoffset]::new((Get-NowUtc), [timespan]::Zero)).ToUnixTimeSeconds())
    $datos = @{}
    foreach ($k in $s.archivos.Keys) { try { $datos[$k] = $s.archivos[$k] | ConvertFrom-Json } catch { Add-R 'ATENCION' "Backup $k" 'JSON ilegible' } }

    foreach ($k in 'full', 'log', 'files') {
        if (-not $datos.ContainsKey($k)) { Add-R 'FALLA' "Backup $k" 'sin registro de ninguna corrida exitosa'; continue }
        $d = $datos[$k]; $edad = $ahora - [int64]$d.time
        if ($edad -lt -300) { Add-R 'ATENCION' "Backup $k" 'fecha en el futuro (reloj desfasado)'; continue }
        if ([int]$d.exit -ne 0 -or -not $d.verified) { Add-R 'FALLA' "Backup $k" "ultima corrida con exit=$($d.exit) verified=$($d.verified), hace $(Format-Edad $edad)" }
        elseif ($edad -gt $script:MaxEdad[$k]) { Add-R 'FALLA' "Backup $k" "verificado pero desactualizado: hace $(Format-Edad $edad) (maximo $(Format-Edad $script:MaxEdad[$k]))" }
        elseif ($script:AvisoEdad.ContainsKey($k) -and $edad -gt $script:AvisoEdad[$k]) { Add-R 'ATENCION' "Backup $k" "verificado hace $(Format-Edad $edad) (se espera cada 15 min)" }
        else { Add-R 'OK' "Backup $k" "verificado hace $(Format-Edad $edad)" }
    }
    if ($datos.ContainsKey('restore-test')) {
        $d = $datos['restore-test']
        $ok = $null -ne $d.PSObject.Properties['last_success'] -and $null -ne $d.last_success
        if (-not $ok) { Add-R 'ATENCION' 'Prueba de restore' 'sin ningun restore verificado todavia' }
        else {
            $edad = $ahora - [int64]$d.last_success
            if ($edad -gt $script:MaxEdad['restore-test']) { Add-R 'ATENCION' 'Prueba de restore' "ultimo restore verificado hace $(Format-Edad $edad) (se espera semanal)" }
            else { Add-R 'OK' 'Prueba de restore' "ultimo restore verificado hace $(Format-Edad $edad)" }
            if ([int]$d.exit -ne 0) { Add-R 'ATENCION' 'Prueba de restore (ultimo intento)' "fallo: $($d.message)" }
        }
    } else { Add-R 'ATENCION' 'Prueba de restore' 'sin registro (la tarea semanal no corrio todavia)' }

    $offs = @($datos.Keys | Where-Object { $_ -like 'offsite-*' })
    if ($offs.Count -eq 0) { Add-R 'ATENCION' 'Copia offsite' 'sin registro: no hay copia externa configurada o nunca corrio (decision SFTP pendiente de destino real)' }
    foreach ($k in $offs) {
        $d = $datos[$k]; $edad = $ahora - [int64]$d.time
        if ([int]$d.exit -ne 0) { Add-R 'FALLA' "Copia $k" "ultima corrida con exit=$($d.exit), hace $(Format-Edad $edad)" }
        elseif ($edad -gt $script:MaxEdad['full']) { Add-R 'FALLA' "Copia $k" "ultima subida hace $(Format-Edad $edad)" }
        else { Add-R 'OK' "Copia $k" "verificada hace $(Format-Edad $edad)" }
    }
    # run-<modo> solo se escribe cuando una corrida fallo: si es mas nueva que el ultimo exito del mismo modo, sigue fallando.
    foreach ($k in @($datos.Keys | Where-Object { $_ -like 'run-*' })) {
        $modo = $k.Substring(4); $d = $datos[$k]
        $ult = if ($datos.ContainsKey($modo)) { [int64]$datos[$modo].time } else { 0 }
        if ([int]$d.exit -ne 0 -and [int64]$d.time -gt $ult) { Add-R 'FALLA' "Corrida de backup '$modo'" "fallo (exit=$($d.exit)) hace $(Format-Edad ($ahora - [int64]$d.time)), posterior al ultimo exito" }
    }
}

# --- 5. Tareas programadas -----------------------------------------------------------------------------------------
function Test-Tareas {
    $tareas = @(Safe { Get-TaskInfoList })
    if ($tareas.Count -eq 0) { Add-R 'ATENCION' 'Tareas programadas TheBury*' 'ninguna encontrada (install-windows.ps1 las crea; o se consulto sin los permisos de su usuario)'; return }
    $ahora = Get-Date
    foreach ($t in $tareas) {
        if ($t.State -eq 'Disabled') { Add-R 'FALLA' "Tarea '$($t.Name)'" 'deshabilitada'; continue }
        if (-not $t.LastRun -or $t.LastRun.Year -lt 2000 -or $t.LastResult -eq 267011) { Add-R 'ATENCION' "Tarea '$($t.Name)'" 'todavia no corrio'; continue }
        $edad = ($ahora - $t.LastRun).TotalSeconds
        $clave = if ($t.Name -match 'Restore') { 'restore-test' } elseif ($t.Name -match 'Log') { 'log' } elseif ($t.Name -match 'Full') { 'full' } else { $null }
        if ($clave -and $edad -gt $script:MaxEdad[$clave]) { Add-R 'FALLA' "Tarea '$($t.Name)'" "sin correr hace $(Format-Edad $edad) (maximo $(Format-Edad $script:MaxEdad[$clave])): el programador no la esta ejecutando"; continue }
        if (-not $t.NextRun -and $t.State -ne 'Running') { Add-R 'ATENCION' "Tarea '$($t.Name)'" 'sin proxima ejecucion programada'; continue }
        if ($t.LastResult -ne 0 -and $t.LastResult -ne 267009) { Add-R 'FALLA' "Tarea '$($t.Name)'" ("ultimo resultado 0x{0:X} hace {1}" -f [uint32]$t.LastResult, (Format-Edad $edad)) }
        else { Add-R 'OK' "Tarea '$($t.Name)'" "ultima corrida hace $(Format-Edad $edad), estado $($t.State)" }
    }
}

# --- 6. Tailscale --------------------------------------------------------------------------------------------------
function Test-Tailscale {
    $exe = 'tailscale'
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue) -and (Test-Path 'C:\Program Files\Tailscale\tailscale.exe')) { $exe = 'C:\Program Files\Tailscale\tailscale.exe' }
    if ($script:VmMode) {
        # Dentro de la VM: tailscaled es un servicio systemd. Exit 127 = el comando no existe (no se instalo con -Tailscale).
        $r0 = Invoke-Tool 'tailscale' @('version')
        if ($r0.Found -and $r0.ExitCode -eq 127) { Add-R 'ATENCION' 'Tailscale (VM)' 'no instalado en la VM (usar -Tailscale en el instalador y luego: sudo tailscale up)'; return }
        $act = Invoke-Tool 'systemctl' @('is-active', 'tailscaled')
        if ($act.Output.Trim() -ne 'active') { Add-R 'FALLA' 'Servicio tailscaled (VM)' "estado=$($act.Output.Trim())"; return }
        $r = Invoke-Tool 'tailscale' @('status', '--json')
    } else {
        $svc = Safe { Get-ServiceInfo 'Tailscale' }
        if (-not $svc) { Add-R 'ATENCION' 'Tailscale' 'servicio no instalado (el acceso remoto previsto es por Tailscale)'; return }
        if ($svc.State -ne 'Running') { Add-R 'FALLA' 'Servicio Tailscale' "estado=$($svc.State)"; return }
        $r = Invoke-Native $exe @('status', '--json')
    }
    if (-not $r.Found -or $r.ExitCode -ne 0) { Add-R 'ATENCION' 'Tailscale' 'servicio activo pero tailscale status no responde'; return }
    $st = Safe { $r.Output | ConvertFrom-Json }
    if (-not $st) { Add-R 'ATENCION' 'Tailscale' 'estado no interpretable'; return }
    if ($st.BackendState -eq 'Running') { Add-R 'OK' 'Tailscale conectado' "BackendState=Running" }
    else { Add-R 'FALLA' 'Tailscale desconectado' "BackendState=$($st.BackendState) (reautenticar; la clave pudo vencer)" }
    $self = $st.PSObject.Properties['Self']
    if ($self -and $self.Value) {
        $ke = $self.Value.PSObject.Properties['KeyExpiry']
        if ($ke -and $ke.Value) {
            $venc = [datetime]::Parse("$($ke.Value)", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
            $dias = [math]::Floor(($venc.ToUniversalTime() - (Get-NowUtc)).TotalDays)
            if ($dias -lt 0) { Add-R 'FALLA' 'Clave del nodo Tailscale' 'VENCIDA' }
            elseif ($dias -lt 30) { Add-R 'ATENCION' 'Clave del nodo Tailscale' "vence en $dias dias: deshabilitar la expiracion para un servidor (consola de Tailscale)" }
            else { Add-R 'OK' 'Clave del nodo Tailscale' "vence en $dias dias" }
        }
    }
}

# --- 7. VM Hyper-V y servicios del invitado (solo modo -VmHost) ----------------------------------------------------
function Test-Vm {
    $v = $null
    try { $v = Get-VmInfo } catch { Add-R 'ATENCION' "VM Hyper-V '$VmName'" "no se pudo consultar: $($_.Exception.Message)"; return }
    if (-not $v) { Add-R 'INFO' "VM Hyper-V '$VmName'" 'modulo Hyper-V no disponible en este equipo: estado de la VM no verificado (se verifica por SSH)'; return }
    if ($v.State -ne 'Running') { Add-R 'FALLA' "VM '$VmName'" "estado=$($v.State)"; return }
    if (-not $v.Heartbeat) { Add-R 'ATENCION' "Latido de la VM '$VmName'" 'no se pudo leer el servicio de integracion Heartbeat (Integration Services del invitado no instalados?)' }
    elseif ($v.Heartbeat -ne 'Ok') { Add-R 'FALLA' "Latido de la VM '$VmName'" "estado=$($v.Heartbeat) (el invitado no responde)" }
    else { Add-R 'OK' "VM '$VmName'" "en ejecucion con latido, uptime $([math]::Round($v.Uptime.TotalHours, 1)) h" }
    if ($v.AutoStart -ne 'Start') { Add-R 'FALLA' 'Arranque automatico de la VM' "AutomaticStartAction=$($v.AutoStart): tras reiniciar el host el ERP no volveria solo" }
    else { Add-R 'OK' 'Arranque automatico de la VM' 'Start' }
    if ($v.AutoStop -ne 'ShutDown') { Add-R 'ATENCION' 'Apagado de la VM con el host' "AutomaticStopAction=$($v.AutoStop): usar ShutDown (apagado ordenado; SQL Server cierra limpio)" }
    else { Add-R 'OK' 'Apagado de la VM con el host' 'ShutDown (ordenado)' }
    if ($v.Checkpoints -ne 'Disabled') { Add-R 'ATENCION' 'Checkpoints de la VM' "CheckpointType=$($v.Checkpoints): revertir un checkpoint con SQL Server puede corromper datos; deshabilitar" }
    else { Add-R 'OK' 'Checkpoints de la VM' 'deshabilitados' }
}

function Test-ServiciosVm {
    $units = @('docker', 'cron', 'bury-firewall')
    foreach ($modo in 'is-active', 'is-enabled') {
        $r = Invoke-Tool 'systemctl' (@($modo) + $units)
        if (-not $r.Found) { Add-R 'FALLA' 'SSH a la VM' 'ssh.exe no encontrado'; return }
        if ($r.ExitCode -eq -2 -or $r.ExitCode -eq 255) { Add-R 'FALLA' 'SSH a la VM' "sin conexion con $VmUser@$VmHost (VM apagada, IP/clave/known_hosts incorrectos; exit $($r.ExitCode))"; return }
        $lineas = @($r.Output -split "`r?`n" | Where-Object { $_.Trim() })
        for ($i = 0; $i -lt $units.Count; $i++) {
            $est = if ($i -lt $lineas.Count) { $lineas[$i].Trim() } else { '?' }
            $bien = if ($modo -eq 'is-active') { $est -eq 'active' } else { $est -match '^(enabled|enabled-runtime|static|alias)$' }
            $quien = if ($modo -eq 'is-active') { 'en ejecucion' } else { 'habilitado al arrancar' }
            if ($bien) { Add-R 'OK' "Servicio $($units[$i]) ($quien)" $est }
            else { Add-R 'FALLA' "Servicio $($units[$i]) ($quien)" "estado=$est" }
        }
    }
    $f = Invoke-Tool 'test' @('-f', '/etc/cron.d/bury-backup')
    if ($f.ExitCode -eq 0) { Add-R 'OK' 'Cron de backups' '/etc/cron.d/bury-backup instalado' }
    else { Add-R 'FALLA' 'Cron de backups' 'falta /etc/cron.d/bury-backup: los backups no corren (install.sh --schedule)' }
    $d = Invoke-Tool 'df' @('-BG', '--output=avail', '/')
    $m = [regex]::Match($d.Output, '(\d+)G')
    if ($d.ExitCode -eq 0 -and $m.Success) {
        $gb = [int]$m.Groups[1].Value
        $est = if ($gb -lt $DiscoMinGB) { 'FALLA' } elseif ($gb -lt (2 * $DiscoMinGB)) { 'ATENCION' } else { 'OK' }
        Add-R $est 'Disco de la VM libre' "$gb GB (minimo $DiscoMinGB GB; volumenes de Docker, imagenes y backups)"
    } else { Add-R 'ATENCION' 'Disco de la VM' 'no se pudo consultar' }
}

# --- Orquestacion --------------------------------------------------------------------------------------------------
function Invoke-HealthCheck {
    $script:Resultados.Clear()
    Test-Equipo
    if ($script:VmMode) { Test-Vm; Test-ServiciosVm }
    $dockerOk = Test-Docker
    if ($dockerOk) { Test-Http } else { Add-R 'INFO' 'HTTPS local' 'omitido: Docker no esta sano' }
    Test-Backups
    if (-not $script:VmMode) { Test-Tareas }   # en modo VM los backups los programa cron dentro de la VM (Test-ServiciosVm)
    Test-Tailscale
}

function Show-Resultados {
    $f = @($script:Resultados | Where-Object Estado -eq 'FALLA').Count
    $a = @($script:Resultados | Where-Object Estado -eq 'ATENCION').Count
    $o = @($script:Resultados | Where-Object Estado -eq 'OK').Count
    $codigo = if ($f -gt 0) { 2 } elseif ($a -gt 0) { 1 } else { 0 }
    if ($Json) {
        [pscustomobject]@{
            generado = (Get-Date).ToString('o'); equipo = $env:COMPUTERNAME; codigo = $codigo
            resumen = [pscustomobject]@{ falla = $f; atencion = $a; ok = $o }
            resultados = $script:Resultados.ToArray()
        } | ConvertTo-Json -Depth 5
    } else {
        $orden = @{ 'FALLA' = 0; 'ATENCION' = 1; 'OK' = 2; 'INFO' = 3 }
        Write-Host ''
        Write-Host "Salud del ERP - TheBuryProject ($(Get-Date -Format 'yyyy-MM-dd HH:mm'), solo lectura)"
        Write-Host ('=' * 78)
        foreach ($r in ($script:Resultados | Sort-Object { $orden[$_.Estado] })) {
            $color = switch ($r.Estado) { 'OK' { 'Green' } 'ATENCION' { 'Yellow' } 'FALLA' { 'Red' } default { 'Gray' } }
            Write-Host ('[{0,-8}] {1}' -f $r.Estado, $r.Item) -ForegroundColor $color
            Write-Host ('           {0}' -f $r.Detalle)
        }
        Write-Host ('=' * 78)
        Write-Host "FALLA=$f  ATENCION=$a  OK=$o  (codigo de salida $codigo)"
    }
    $script:Codigo = $codigo
}

# Permite cargar las funciones con '. .\Check-ErpHealth.ps1' (pruebas) sin ejecutar nada.
if ($MyInvocation.InvocationName -eq '.') { return }

Invoke-HealthCheck
Show-Resultados
exit $script:Codigo
