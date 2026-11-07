<#
.SYNOPSIS
  Verificacion SOLO LECTURA del equipo Windows antes de instalar TheBuryProject (Docker Desktop + WSL2 + Tailscale).

.DESCRIPTION
  No instala, no modifica y no arranca nada: no toca registro, firewall, tareas, servicios, Docker ni WSL.
  Solo consulta el estado actual y lo informa como OK / ATENCION / FALLA / INFO.
  Sirve para saber, ANTES de ejecutar install-windows.ps1, que falta preparar (P-07).

  Codigo de salida: 0 si no hay FALLA; 1 si hay al menos una FALLA. Las ATENCION no cambian el codigo.

.PARAMETER Dominio
  Nombre interno del ERP (ERP_DOMAIN). Por defecto 'tbp'. Se verifica si el archivo hosts lo resuelve.

.PARAMETER RamMinGB
  RAM minima recomendada (docs/instalacion-servidor.md seccion 2). Por defecto 8.

.PARAMETER DiscoMinGB
  Espacio libre minimo en el disco del sistema. Por defecto 20.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\preflight-windows.ps1
#>
[CmdletBinding()]
param(
    [string]$Dominio = 'tbp',
    [int]$RamMinGB = 8,
    [int]$DiscoMinGB = 20
)

$ErrorActionPreference = 'Continue'
$script:Resultados = New-Object System.Collections.Generic.List[object]

function Add-R([string]$Estado, [string]$Item, [string]$Detalle) {
    $script:Resultados.Add([pscustomobject]@{ Estado = $Estado; Item = $Item; Detalle = $Detalle })
}
function Safe([scriptblock]$b) { try { & $b } catch { $null } }

# --- Sistema -----------------------------------------------------------------
$os = Safe { Get-CimInstance Win32_OperatingSystem }
if ($os) {
    $ok = [int]($os.BuildNumber) -ge 19041
    Add-R ($(if ($ok) { 'OK' } else { 'FALLA' })) 'Windows (WSL2 requiere build >= 19041)' "$($os.Caption) build $($os.BuildNumber)"
    $ramGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    Add-R ($(if ($ramGB -ge $RamMinGB) { 'OK' } else { 'ATENCION' })) "RAM >= $RamMinGB GB" "$ramGB GB"
}
$sysDrive = ($env:SystemDrive).TrimEnd(':')
$disk = Safe { Get-PSDrive -Name $sysDrive }
if ($disk) {
    $libre = [math]::Round($disk.Free / 1GB, 1)
    Add-R ($(if ($libre -ge $DiscoMinGB) { 'OK' } else { 'ATENCION' })) "Disco $sysDrive`: libre >= $DiscoMinGB GB" "$libre GB libres"
}
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Add-R 'INFO' 'Sesion como administrador' $(if ($admin) { 'si (install-windows.ps1 lo exige)' } else { 'no (este chequeo no lo necesita; la instalacion si)' })

# --- WSL2 --------------------------------------------------------------------
$wslExe = Get-Command wsl.exe -ErrorAction SilentlyContinue
if (-not $wslExe) {
    Add-R 'FALLA' 'WSL2' 'wsl.exe no encontrado (wsl --install -d Ubuntu)'
} else {
    $raw = Safe { (& wsl.exe -l -v 2>$null) -join "`n" }
    $txt = if ($raw) { $raw -replace "`0", '' } else { '' }
    $distros = @($txt -split "`n" | Where-Object { $_ -match '\s(1|2)\s*$' })
    $v2Ubuntu = @($distros | Where-Object { $_ -match 'Ubuntu' -and $_ -match '\s2\s*$' })
    if ($v2Ubuntu.Count -gt 0) { Add-R 'OK' 'WSL2: distro Ubuntu en version 2' (($v2Ubuntu | ForEach-Object { $_.Trim() }) -join ' | ') }
    elseif ($distros.Count -gt 0) { Add-R 'ATENCION' 'WSL2: sin Ubuntu en version 2' (($distros | ForEach-Object { $_.Trim() }) -join ' | ') }
    else { Add-R 'FALLA' 'WSL2: sin distros' 'instalar Ubuntu: wsl --install -d Ubuntu' }
}
$wslCfg = Join-Path $env:USERPROFILE '.wslconfig'
if (Test-Path $wslCfg) {
    $mem = (Select-String -Path $wslCfg -Pattern '^\s*memory\s*=' -ErrorAction SilentlyContinue | Select-Object -First 1)
    Add-R 'OK' '.wslconfig presente' $(if ($mem) { $mem.Line.Trim() } else { 'sin limite de memoria definido' })
} else {
    Add-R 'ATENCION' '.wslconfig' 'ausente: WSL2 puede consumir hasta la mitad de la RAM (recomendado memory=6GB, docs/instalacion-servidor.md seccion 3.1)'
}

# --- Docker Desktop ----------------------------------------------------------
$dockerExe = Get-Command docker.exe -ErrorAction SilentlyContinue
if (-not $dockerExe) {
    Add-R 'FALLA' 'Docker CLI' 'docker.exe no encontrado: instalar Docker Desktop (backend WSL2)'
} else {
    $sv = Safe { (& docker.exe version --format '{{.Server.Version}}' 2>$null) }
    if ($LASTEXITCODE -eq 0 -and $sv) {
        Add-R 'OK' 'Docker daemon' "responde (servidor $sv)"
        $cv = Safe { (& docker.exe compose version --short 2>$null) }
        Add-R ($(if ($cv) { 'OK' } else { 'FALLA' })) 'Docker Compose v2' $(if ($cv) { $cv } else { 'docker compose no disponible' })
    } else {
        Add-R 'FALLA' 'Docker daemon' 'docker.exe existe pero el daemon no responde (Docker Desktop apagado o sin backend WSL2)'
    }
}
$ddRun = Safe { Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction Stop }
$ddAuto = $ddRun -and (($ddRun.PSObject.Properties | Where-Object { $_.Name -match 'Docker Desktop' }) -ne $null)
Add-R ($(if ($ddAuto) { 'OK' } else { 'ATENCION' })) 'Docker Desktop arranca al iniciar sesion' $(if ($ddAuto) { 'entrada Run presente' } else { 'no detectado (Settings > General > Start Docker Desktop when you sign in)' })

# --- Tailscale ---------------------------------------------------------------
$ts = Get-Command tailscale.exe -ErrorAction SilentlyContinue
if (-not $ts -and (Test-Path 'C:\Program Files\Tailscale\tailscale.exe')) { $ts = [pscustomobject]@{ Source = 'C:\Program Files\Tailscale\tailscale.exe' } }
if (-not $ts) {
    Add-R 'FALLA' 'Tailscale' 'no instalado (decision del propietario 2026-11-07: acceso remoto por Tailscale)'
} else {
    $tsExe = $ts.Source
    $svc = Safe { Get-CimInstance Win32_Service -Filter "Name='Tailscale'" }
    if ($svc) {
        $okSvc = ($svc.State -eq 'Running' -and $svc.StartMode -eq 'Auto')
        Add-R ($(if ($okSvc) { 'OK' } else { 'ATENCION' })) 'Servicio Tailscale' "estado=$($svc.State) inicio=$($svc.StartMode) (debe ser Running/Auto para conectar antes de iniciar sesion)"
    } else { Add-R 'ATENCION' 'Servicio Tailscale' 'servicio no encontrado' }
    $ip4 = Safe { (& $tsExe ip -4 2>$null | Select-Object -First 1) }
    if ($ip4 -match '^100\.') {
        Add-R 'OK' 'IP de Tailscale (IPv4)' "$ip4 (rango CGNAT 100.64.0.0/10; usar -AllowFrom LocalSubnet,100.64.0.0/10 y/o ERP_BIND_ADDRESS)"
    } else {
        Add-R 'ATENCION' 'IP de Tailscale (IPv4)' 'sin IP: Tailscale no esta conectado/autenticado'
    }
    $st = Safe { (& $tsExe status --json 2>$null) | ConvertFrom-Json }
    if ($st) {
        Add-R 'INFO' 'Tailscale BackendState' "$($st.BackendState)"
        $dns = $st.Self.DNSName
        if ($dns) { Add-R 'INFO' 'Nombre MagicDNS del servidor' ($dns.TrimEnd('.')) }
        if ($st.Self.KeyExpiry) { Add-R 'ATENCION' 'Expiracion de clave del nodo' "vence $($st.Self.KeyExpiry): para un servidor conviene deshabilitar la expiracion en la consola de administracion" }
    }
}

# --- Red / puertos / firewall ------------------------------------------------
$l443 = @(Safe { Get-NetTCPConnection -LocalPort 443 -State Listen -ErrorAction Stop })
if ($l443.Count -gt 0) {
    $procs = ($l443 | ForEach-Object { $p = Safe { (Get-Process -Id $_.OwningProcess -ErrorAction Stop).ProcessName }; if (-not $p) { $p = "PID $($_.OwningProcess)" }; "$($_.LocalAddress):443 ($p)" }) -join ', '
    Add-R 'ATENCION' 'Puerto 443 en uso' "$procs - Caddy no podra publicar 443 hasta liberarlo (o ajustar ERP_BIND_ADDRESS)"
} else { Add-R 'OK' 'Puerto 443 libre' 'ningun listener actual' }

$fwTb = @(Safe { Get-NetFirewallRule -DisplayName 'TheBury*' -ErrorAction Stop })
Add-R 'INFO' 'Reglas de firewall TheBury*' $(if ($fwTb.Count) { ($fwTb | ForEach-Object { "$($_.DisplayName)=$($_.Action)/$($_.Enabled)" }) -join '; ' } else { 'ninguna (la crea install-windows.ps1)' })
$fwDocker = @(Safe { Get-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -ErrorAction Stop | Where-Object { $_.DisplayName -match 'docker' } })
if ($fwDocker.Count -gt 0) {
    Add-R 'ATENCION' 'Reglas Allow de Docker entrantes' "$($fwDocker.Count) regla(s) (perfil/direccion amplios pueden anular -AllowFrom; ver docs/red-privada-lan-vpn.md seccion 6)"
} else { Add-R 'OK' 'Sin reglas Allow de Docker entrantes' 'nada que anule el filtro por origen' }

$hostsFile = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$hostEntry = Safe { Select-String -Path $hostsFile -Pattern "^\s*[^#\s]+\s+.*\b$([regex]::Escape($Dominio))\b" | Select-Object -First 1 }
Add-R 'INFO' "hosts resuelve '$Dominio'" $(if ($hostEntry) { $hostEntry.Line.Trim() } else { 'sin entrada (cada cliente necesita IP + nombre en hosts o un DNS; ver docs/red-privada-lan-vpn.md seccion 1)' })

# --- Energia y arranque automatico ------------------------------------------
$pw = Safe { (& powercfg.exe /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null) -join "`n" }
if ($pw -match 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)') {
    $v = [Convert]::ToInt32($Matches[1], 16)
    Add-R ($(if ($v -eq 0) { 'OK' } else { 'ATENCION' })) 'Suspension con corriente' $(if ($v -eq 0) { 'nunca' } else { "$v s (install-windows.ps1 la desactiva)" })
}
$wl = Safe { Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon' -ErrorAction Stop }
$auto = ($wl -and $wl.AutoAdminLogon -eq '1')
Add-R ($(if ($auto) { 'OK' } else { 'ATENCION' })) 'Inicio de sesion automatico' $(if ($auto) { 'activo' } else { 'inactivo: tras un corte de luz Docker y los backups no vuelven solos hasta que alguien inicie sesion (docs/instalacion-servidor.md seccion 3.3). No se lee ninguna clave.' })
Add-R 'INFO' 'UPS / BIOS "Restore on AC Power Loss"' 'no verificable por software: confirmar a mano (hoy: sin UPS)'

# --- Informe -----------------------------------------------------------------
$orden = @{ 'FALLA' = 0; 'ATENCION' = 1; 'OK' = 2; 'INFO' = 3 }
Write-Host ''
Write-Host "Preflight Windows - TheBuryProject ($(Get-Date -Format 'yyyy-MM-dd HH:mm'), solo lectura)"
Write-Host ('=' * 78)
foreach ($r in ($script:Resultados | Sort-Object { $orden[$_.Estado] })) {
    $color = switch ($r.Estado) { 'OK' { 'Green' } 'ATENCION' { 'Yellow' } 'FALLA' { 'Red' } default { 'Gray' } }
    Write-Host ('[{0,-8}] {1}' -f $r.Estado, $r.Item) -ForegroundColor $color
    Write-Host ('           {0}' -f $r.Detalle)
}
$f = @($script:Resultados | Where-Object Estado -eq 'FALLA').Count
$a = @($script:Resultados | Where-Object Estado -eq 'ATENCION').Count
Write-Host ('=' * 78)
Write-Host "FALLA=$f  ATENCION=$a  OK=$(@($script:Resultados | Where-Object Estado -eq 'OK').Count)"
if ($f -gt 0) { exit 1 } else { exit 0 }
