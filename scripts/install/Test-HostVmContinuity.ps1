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
    H07 historial de cierres NO limpios del anfitrion en 30 dias (Kernel-Power 41, bugcheck): 3 o mas = FAIL (anfitrion inestable)
    H08 la instancia de ESTA VM corre en una sesion interactiva de usuario (no en la sesion 0), haya o no autostart configurado
    H09 presupuesto de RAM: VM + tope de WSL2/Docker de desarrollo + margen de Windows
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

# --- H03 autostart tras reinicio de Windows (vinculado a ESTA VM; nunca OK sin un reinicio comprobado)
$vmxNorm = $VmxPath.ToLowerInvariant().Replace('/', '\')
$svc = Get-Service -Name VmwareAutostartService -ErrorAction SilentlyContinue
$xmlPath = 'C:\ProgramData\VMware\hostd\vmAutoStart.xml'
$xmlOk = $false
if (Test-Path -LiteralPath $xmlPath) { $xmlOk = ((Get-Content -LiteralPath $xmlPath -Raw -ErrorAction SilentlyContinue).ToLowerInvariant().Replace('/', '\') -like "*$vmxNorm*") }
$viaServicio = ($svc -and $svc.StartType -eq 'Automatic' -and $xmlOk)
$tareasVm = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
  $_.State -ne 'Disabled' -and
  @($_.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskBootTrigger' }).Count -gt 0 -and
  @($_.Actions | Where-Object { (("$($_.Execute) $($_.Arguments)").ToLowerInvariant().Replace('/', '\')) -like "*vmrun*" -and (("$($_.Execute) $($_.Arguments)").ToLowerInvariant().Replace('/', '\')) -like "*$vmxNorm*" }).Count -gt 0
})
if ($viaServicio -or $tareasVm.Count -gt 0) {
  $via = @(); if ($viaServicio) { $via += 'servicio de autostart de VMware (automatico) con ESTA VM en vmAutoStart.xml' }; if ($tareasVm.Count -gt 0) { $via += ('tarea de arranque: ' + (($tareasVm | Select-Object -ExpandProperty TaskName) -join ', ')) }
  Warn 'H03' ('configurado para ESTA VM (' + ($via -join '; ') + ') pero NO probado: solo un reinicio autorizado de Windows lo confirma (la VM debe volver sin iniciar sesion)')
} else {
  Fail 'H03' 'ninguna via de autostart vinculada a esta VM (servicio de VMware con la VM en vmAutoStart.xml, o tarea de arranque con vmrun y su .vmx): tras un reinicio de Windows (actualizacion, corte de luz) la VM NO arranca hasta que alguien inicie sesion y la encienda'
}

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

# --- H07 historial de cierres NO limpios del anfitrion (Kernel-Power 41 / bugcheck): evidencia de estabilidad, no de causa
$dias = 30
$k41 = @(); $k41Error = $null
try { $k41 = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 41; StartTime = (Get-Date).AddDays(-$dias) } -ErrorAction Stop) }
catch { if ($_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*') { $k41Error = $_.Exception.Message } }
if ($k41Error) { Warn 'H07' "no se pudo leer el registro System (se requiere permiso de lectura de eventos): sin evidencia sobre el historial de cierres no limpios ($k41Error)" }
elseif ($k41.Count -eq 0) { Ok 'H07' "ningun cierre no limpio del anfitrion en $dias dias (registro System leido sin errores)" }
else {
  $codigos = @($k41 | ForEach-Object {
    $v = ([xml]$_.ToXml()).Event.EventData.Data | Where-Object { $_.Name -eq 'BugcheckCode' } | Select-Object -ExpandProperty '#text'
    if ($v -and [int]$v -ne 0) { '0x{0:X}' -f [int]$v } else { 'sin bugcheck (corte de energia o reinicio forzado)' }
  } | Group-Object | ForEach-Object { "{0} x{1}" -f $_.Name, $_.Count })
  $msg = "{0} cierre(s) no limpio(s) del anfitrion en {1} dias, ultimo {2:yyyy-MM-dd HH:mm}: {3}. Cada uno apaga la VM sin cierre ordenado" -f $k41.Count, $dias, ($k41 | Select-Object -First 1).TimeCreated, ($codigos -join '; ')
  if ($k41.Count -ge 3) { Fail 'H07' ($msg + ': un anfitrion que se cae asi no sirve para operar; diagnosticar (minidumps en C:\Windows\Minidump, BIOS/controladores) antes de la puesta en marcha') }
  else { Warn 'H07' $msg }
}

# --- H08 la instancia de ESTA VM corre en una sesion interactiva (independiente de que haya autostart configurado)
$todos = @(Get-CimInstance Win32_Process -Filter "Name='vmware-vmx.exe'" -ErrorAction SilentlyContinue)
# comparacion por ruta COMPLETA del .vmx extraida de la linea de comandos (no por subcadena: tbp.vmx.other.vmx no es tbp.vmx)
$vmxProcs = @($todos | Where-Object { $_.CommandLine -and (@([regex]::Matches($_.CommandLine, '(?i)[a-z]:[\/][^"]*?\.vmx(?=["\s]|$)') | ForEach-Object { $_.Value.ToLowerInvariant().Replace('/', '\') }) -contains $vmxNorm) })
if ($vmxProcs.Count -eq 0 -and $todos.Count -eq 1 -and $vmrun) {
  # la linea de comandos puede no ser legible (proceso de otro usuario/protegido): con UNA sola VM encendida y la nuestra en vmrun list, es esa
  $encendidas = @(& $vmrun list 2>$null | Where-Object { $_ -match '\.vmx$' })
  if ($encendidas.Count -eq 1 -and $encendidas[0].ToLowerInvariant().Replace('/', '\') -eq $vmxNorm) { $vmxProcs = $todos }
}
if ($vmxProcs.Count -eq 0) { Warn 'H08' 'no se encontro un proceso vmware-vmx de esta VM (apagada, o hay varias VM encendidas y no se pudo atribuir el proceso)' }
else {
  $sesiones = @($vmxProcs | Select-Object -ExpandProperty SessionId -Unique)
  if ($sesiones | Where-Object { $_ -gt 0 }) {
    Warn 'H08' ('esta instancia de la VM corre en una sesion interactiva de usuario (sesion ' + ($sesiones -join ',') + '), aunque haya autostart configurado: tras un reinicio solo arrancara sin sesion si el autostart la lanza (H03); cerrar sesion puede apagarla')
  } else { Ok 'H08' 'esta instancia de la VM corre en la sesion 0 (servicio/tarea), sin depender de un usuario conectado' }
}

# --- H09 presupuesto de RAM del anfitrion: VM + tope de WSL2/Docker Desktop de desarrollo (si existe) + margen para Windows
$ramGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
$vmGB = 0; foreach ($l in Get-Content -LiteralPath $VmxPath -ErrorAction SilentlyContinue) { if ($l -match '^\s*memsize\s*=\s*"?(\d+)"?') { $vmGB = [math]::Round([int]$Matches[1] / 1024, 1) } }
$wslCfg = Join-Path $env:USERPROFILE '.wslconfig'
$wslGB = [math]::Round($ramGB * 0.5, 1); $wslTxt = 'sin .wslconfig: tope por defecto = 50 % de la RAM'; $wslIlegible = $false
if (Test-Path -LiteralPath $wslCfg) {
  $seccion = ''
  foreach ($l in Get-Content -LiteralPath $wslCfg) {
    if ($l -match '^\s*\[([^\]]+)\]') { $seccion = $Matches[1].Trim().ToLowerInvariant(); continue }
    if ($seccion -eq 'wsl2' -and $l -match '^\s*memory\s*=') {
      if ($l -match '^\s*memory\s*=\s*(\d+(?:\.\d+)?)\s*(GB|G|MB|M)?\s*(#.*)?$') {
        $n = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
        $wslGB = if ($Matches[2] -match '^M') { [math]::Round($n / 1024, 1) } else { [math]::Round($n, 1) }
        $wslTxt = '.wslconfig ' + $l.Trim(); $wslIlegible = $false
      } else { $wslIlegible = $true; $wslTxt = ".wslconfig tiene un memory= que no se pudo interpretar ($($l.Trim())): se asume el tope por defecto (50 % de la RAM)" }
    }
  }
}
$margen = 4
$msgR = "RAM {0} GB; VM {1} GB + tope de WSL2/Docker de desarrollo {2} GB ({3}) + margen Windows {4} GB" -f $ramGB, $vmGB, $wslGB, $wslTxt, $margen
if ($vmGB -le 0) { Warn 'H09' 'no se pudo leer memsize del .vmx' }
elseif ($wslIlegible) { Warn 'H09' $msgR }
elseif (($vmGB + $wslGB + $margen) -le $ramGB) { Ok 'H09' $msgR }
else { Warn 'H09' ($msgR + ': con la pila de desarrollo a tope el anfitrion paginaria o terminaria el vmmem; fijar memory= en .wslconfig (requiere reiniciar WSL: ventana)') }

Write-Host '---'
Write-Host 'FISICO (no verificable por software): UPS/energia, BIOS "Restore on AC Power Loss", copia fuera del sitio, custodia de claves.'
if ($script:Fail -gt 0) { Write-Host ("Test-HostVmContinuity: {0} FAIL, {1} WARN" -f $script:Fail, $script:Warn); exit 12 }
Write-Host ("Test-HostVmContinuity: sin FAIL ({0} WARN)" -f $script:Warn); exit 0
