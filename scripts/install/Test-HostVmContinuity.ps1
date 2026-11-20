<#
.SYNOPSIS
  Auditoria de SOLO LECTURA de la continuidad de la VM del servidor sobre un anfitrion Windows con VMware Workstation (P-04/P-06).
.DESCRIPTION
  No cambia nada: no apaga, no reinicia, no registra tareas, no toca la VM ni sus discos. Responde con hechos comprobables:
    H01 espacio libre del volumen que aloja los discos de la VM frente a su tamano MAXIMO (discos dispersos que crecen)
    H02 la carpeta de la VM NO esta bajo OneDrive ni es un punto de reanalisis (sincronizar un .vmdk en uso corrompe y llena el disco)
    H03 la VM vuelve sola tras reiniciar Windows (servicio de autostart de VMware o tarea programada con vmrun)
    H04 politica de reinicio automatico de Windows Update y suspension en corriente alterna
    H05 existe un medio EXTERNO con capacidad (disco USB / extraible) conectado al anfitrion
    H06 los respaldos de la VM no dependen de un volumen del mismo anfitrion (informativo: el unico medio visible son discos internos)
  Exit: 0 sin FAIL | 12 con al menos un FAIL. WARN = requiere decision u accion humana. Un anfitrion no puede probar desde dentro que
  haya energia ininterrumpida, el valor "restaurar tras perdida de CA" del BIOS ni una copia fuera del sitio: eso es FISICO.
.PARAMETER VmxPath
  Ruta del .vmx. Si se omite se toma la primera VM encendida segun vmrun list.
#>
[CmdletBinding()]
param([string]$VmxPath)

$ErrorActionPreference = 'Continue'
$script:Fail = 0; $script:Warn = 0
function Ok($id, $msg)   { Write-Host ("OK   {0} {1}" -f $id, $msg) }
function Warn($id, $msg) { $script:Warn++; Write-Host ("WARN {0} {1}" -f $id, $msg) }
function Fail($id, $msg) { $script:Fail++; Write-Host ("FAIL {0} {1}" -f $id, $msg) }

# --- localizar vmrun y la VM
$vmrun = (Get-Command vmrun -ErrorAction SilentlyContinue).Source
if (-not $vmrun) {
  foreach ($base in 'C:\Program Files (x86)\VMware\VMware Workstation', 'C:\Program Files\VMware\VMware Workstation') {
    if (Test-Path (Join-Path $base 'vmrun.exe')) { $vmrun = Join-Path $base 'vmrun.exe'; break }
  }
}
if (-not $VmxPath -and $vmrun) {
  $VmxPath = (& $vmrun list 2>$null | Where-Object { $_ -match '\.vmx$' } | Select-Object -First 1)
}
if (-not $VmxPath -or -not (Test-Path -LiteralPath $VmxPath)) {
  Fail 'H00' 'no se encontro la VM (VMware Workstation/vmrun o -VmxPath): no se puede auditar'
  exit 12
}
$vmDir = Split-Path -Parent $VmxPath
Write-Host ("VM: {0}" -f $VmxPath)

# --- H01 espacio frente al tamano maximo de los discos dispersos
$maxBytes = 0L; $usedBytes = 0L
foreach ($d in Get-ChildItem -LiteralPath $vmDir -Filter '*.vmdk' -File -ErrorAction SilentlyContinue) {
  if ($d.Length -lt 4096) {   # descriptor de texto: sumar sus extents (sectores de 512 B)
    foreach ($line in Get-Content -LiteralPath $d.FullName -ErrorAction SilentlyContinue) {
      if ($line -match '^(RW|RDONLY)\s+(\d+)\s+') { $maxBytes += [int64]$Matches[2] * 512 }
    }
  } else { $usedBytes += $d.Length }
}
$vmem = (Get-ChildItem -LiteralPath $vmDir -Filter '*.vmem' -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
$drive = (Get-Item -LiteralPath $vmDir).PSDrive
$volume = Get-Volume -DriveLetter $drive.Name -ErrorAction SilentlyContinue
if ($maxBytes -gt 0 -and $volume) {
  $growth = [math]::Max(0L, $maxBytes - $usedBytes)
  $freeGB = [math]::Round($volume.SizeRemaining / 1GB, 1); $needGB = [math]::Round($growth / 1GB, 1)
  $msg = "volumen {0}: libres {1} GB; discos de la VM: usados {2} GB de {3} GB maximos (puede crecer {4} GB mas); RAM de la VM en disco {5} GB" -f `
    $drive.Name, $freeGB, [math]::Round($usedBytes / 1GB, 1), [math]::Round($maxBytes / 1GB, 1), $needGB, [math]::Round(([int64]$vmem) / 1GB, 1)
  if ($growth -le $volume.SizeRemaining * 0.8) { Ok 'H01' $msg }
  elseif ($growth -le $volume.SizeRemaining) { Warn 'H01' ($msg + ' - la VM podria dejar el volumen casi lleno') }
  else { Fail 'H01' ($msg + ' - si el invitado llena su disco, el volumen del anfitrion se llena y la VM se pausa o se corrompe') }
} else { Warn 'H01' 'no se pudo calcular el tamano maximo de los discos de la VM' }

# --- H02 OneDrive / reanalisis
$bad = @()
$cur = $vmDir
while ($cur) {
  $it = Get-Item -LiteralPath $cur -Force -ErrorAction SilentlyContinue
  if ($it) {
    if ($cur -match 'OneDrive') { $bad += "ruta bajo OneDrive: $cur" }
    if ($it.Attributes -band [IO.FileAttributes]::ReparsePoint) { $bad += "punto de reanalisis: $cur" }
  }
  $parent = Split-Path -Parent $cur
  if ($parent -eq $cur -or -not $parent) { break }
  $cur = $parent
}
$bad = @($bad | Select-Object -Unique | Select-Object -First 2)   # evitar repetir cada carpeta padre
$odRunning = [bool](Get-Process -Name OneDrive -ErrorAction SilentlyContinue)
if ($bad.Count -eq 0) { Ok 'H02' 'la carpeta de la VM no esta bajo OneDrive ni reanalisis' }
elseif ($odRunning) { Fail 'H02' ('OneDrive EN EJECUCION y la VM esta bajo su arbol (' + ($bad -join '; ') + '): sincronizaria .vmdk/.vmem en uso') }
else { Warn 'H02' ('la VM esta bajo una ruta con OneDrive/reanalisis (' + ($bad -join '; ') + '); hoy OneDrive no corre, pero reinstalarlo/iniciar sesion la sincronizaria: mover la VM a una ruta fuera del perfil') }

# --- H03 autostart tras reinicio de Windows
$svc = Get-Service -Name VmwareAutostartService -ErrorAction SilentlyContinue
$auto = $svc -and $svc.StartType -eq 'Automatic'
$tareas = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
  $_.State -ne 'Disabled' -and ($_.Actions | Where-Object { $_.Execute -match 'vmrun|vmware' -or $_.Arguments -match 'vmrun|\.vmx' })
})
$vmAutoStartXml = Test-Path 'C:\ProgramData\VMware\hostd\vmAutoStart.xml'
if ($auto -and $vmAutoStartXml) { Ok 'H03' 'servicio de autostart de VMware automatico y vmAutoStart.xml presente' }
elseif ($tareas.Count -gt 0) { Ok 'H03' ("tarea programada que inicia la VM: " + (($tareas | Select-Object -ExpandProperty TaskName) -join ', ') + ' (verificar con un reinicio autorizado)') }
else { Fail 'H03' 'ninguna via de autostart: tras un reinicio de Windows (actualizacion, corte de luz) la VM NO arranca hasta que alguien inicie sesion y la encienda' }

# --- H04 reinicios de Windows Update y suspension
$au = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue
$ux = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings' -ErrorAction SilentlyContinue
if ($au -and ($au.NoAutoUpdate -eq 1 -or $au.AUOptions -in 2, 3 -or $au.NoAutoRebootWithLoggedOnUsers -eq 1)) { Ok 'H04a' 'hay politica que limita el reinicio automatico de Windows Update' }
else { Warn 'H04a' ('sin politica de reinicio de Windows Update (horas activas: ' + $(if ($ux) { "$($ux.ActiveHoursStart)-$($ux.ActiveHoursEnd), habilitadas=$($ux.IsActiveHoursEnabled)" } else { 'sin dato' }) + '): un reinicio automatico apagaria la VM; ver doc, decision del propietario') }
$sleep = (powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null | Select-String '0x[0-9a-f]{8}' | Select-Object -First 1)
if ($sleep -and $sleep.ToString() -match '0x00000000') { Ok 'H04b' 'sin suspension por inactividad en corriente alterna' }
else { Warn 'H04b' 'la suspension en corriente alterna no esta desactivada (o no se pudo leer): suspender el PC detiene la VM' }

# --- H05 medio externo con capacidad
$externos = @(Get-Disk -ErrorAction SilentlyContinue | Where-Object { $_.BusType -eq 'USB' -and $_.Size -gt 1GB })
$vols = @(Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveType -eq 'Removable' -and $_.Size -gt 1GB })
if ($externos.Count -gt 0 -or $vols.Count -gt 0) {
  Ok 'H05' ("medio externo conectado: " + (($externos | ForEach-Object { "{0} {1} GB" -f $_.FriendlyName, [math]::Round($_.Size / 1GB) }) -join '; ') + '. Falta confirmar que es un destino fuera del equipo y verificarlo con check-external-destination.sh')
} else {
  Fail 'H05' 'no hay ningun disco USB/extraible con capacidad conectado al anfitrion: no existe destino externo (lectores vacios y discos internos no cuentan)'
}

# --- H06 informativo: volumenes del propio anfitrion
$internos = (Get-Disk -ErrorAction SilentlyContinue | Where-Object { $_.BusType -ne 'USB' -and $_.Size -gt 0 } | ForEach-Object { "{0} ({1})" -f $_.FriendlyName, $_.BusType }) -join '; '
Warn 'H06' ("los unicos medios visibles son discos INTERNOS del mismo anfitrion: $internos. Otro disco del mismo PC, WSL2 o una carpeta compartida NO protegen de perder el equipo")

Write-Host '---'
Write-Host 'FISICO (no verificable por software): UPS/energia, BIOS "Restore on AC Power Loss", copia fuera del sitio, custodia de claves.'
if ($script:Fail -gt 0) { Write-Host ("Test-HostVmContinuity: {0} FAIL, {1} WARN" -f $script:Fail, $script:Warn); exit 12 }
Write-Host ("Test-HostVmContinuity: sin FAIL ({0} WARN)" -f $script:Warn); exit 0
