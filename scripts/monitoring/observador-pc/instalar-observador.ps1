# Instala el observador externo en la PC administradora, para el usuario actual (sin permisos de administrador).
#   powershell -NoProfile -ExecutionPolicy Bypass -File instalar-observador.ps1 -Dir D:\bury-observador -Ip 192.168.50.223 -CaCert <caddy-root-ca.crt> `
#       -HostKeyFingerprint SHA256:...   # huella leida EN el servidor: ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
# Copia los scripts, escribe config.json, genera una clave SSH DEDICADA sin passphrase (solo si no existe) e imprime su parte PUBLICA
# para agregarla en el servidor con comando forzado. Con -RegistrarTarea crea la tarea programada que lo mantiene corriendo.
# Quitar:  Unregister-ScheduledTask -TaskName BuryObservador   (y borrar la linea de la clave en authorized_keys del servidor).
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Ip, [string]$CaCert, [string]$Domain = 'tbp-prov', [int]$HttpsPort = 8443,
    [string]$SshUser = 'snowsh', [string]$Gateway = '192.168.50.1', [int]$IntervalSeconds = 60,
    [string]$HostKeyFingerprint,
    # destino de respaldos en esta PC (opcional): carpeta que contiene sql\ y files\
    [string]$DestinoRuta, [string]$DestinoServicio = 'sshd', [int]$DestinoPuerto = 2022, [int]$DestinoTopeGB = 50,
    [switch]$RegistrarTarea
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Dir | Out-Null
foreach ($f in 'observador-pc.ps1', 'observador-logica.ps1', 'test-observador.ps1') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $f) -Destination (Join-Path $Dir $f) -Force
}
if ($Ip) {
    [ordered]@{ ip = $Ip; domain = $Domain; httpsPort = $HttpsPort; sshUser = $SshUser; gateway = $Gateway; intervalSeconds = $IntervalSeconds } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Dir 'config.json') -Encoding ASCII
}
if ($DestinoRuta) {
    $cfgPath = Join-Path $Dir 'config.json'
    if (-not (Test-Path -LiteralPath $cfgPath)) { throw '-DestinoRuta necesita un config.json existente (o -Ip)' }
    $json = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    $dest = [pscustomobject]@{ ruta = $DestinoRuta; servicio = $DestinoServicio; puerto = $DestinoPuerto; topeGB = $DestinoTopeGB; maxMinutosLogs = 45; maxHorasArchivos = 27 }
    $json | Add-Member -NotePropertyName destino -NotePropertyValue $dest -Force
    $json | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $cfgPath -Encoding ASCII
}
if ($CaCert) { Copy-Item -LiteralPath $CaCert -Destination (Join-Path $Dir 'ca.crt') -Force }
$key = Join-Path $Dir 'obs_ed25519'
if (-not (Test-Path -LiteralPath $key)) {
    & cmd.exe /c "ssh-keygen -q -t ed25519 -N `"`" -C bury-observador-pc -f `"$key`""
    if ($LASTEXITCODE -ne 0) { throw 'ssh-keygen fallo' }
}
# la clave no tiene passphrase: solo el usuario actual puede leerla (ssh ademas rechaza claves con permisos abiertos)
& icacls.exe $key /inheritance:r /grant:r "$($env:USERDOMAIN)\$($env:USERNAME):(R,W)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'icacls fallo' }
# Identidad del servidor: se registra SOLO si la huella obtenida por la red coincide con la leida en el propio servidor.
if ($HostKeyFingerprint) {
    if (-not $Ip) { throw '-HostKeyFingerprint necesita -Ip' }
    $ErrorActionPreference = 'Continue'
    $scan = @(& ssh-keyscan.exe -T 8 -t ed25519 $Ip 2>$null | Where-Object { $_ -and $_ -notmatch '^#' })
    $ErrorActionPreference = 'Stop'
    if ($scan.Count -ne 1) { throw 'no se pudo leer la clave de host del servidor' }
    $tmp = Join-Path $Dir 'known_hosts.nuevo'
    Set-Content -LiteralPath $tmp -Value $scan[0] -Encoding ASCII
    $found = ((& ssh-keygen.exe -lf $tmp) -split ' ')[1]
    if ($found -cne $HostKeyFingerprint) {
        Remove-Item -LiteralPath $tmp -Force
        throw "la huella del servidor ($found) NO coincide con la esperada: no se registra"
    }
    Move-Item -LiteralPath $tmp -Destination (Join-Path $Dir 'known_hosts') -Force
    Write-Output 'Identidad del servidor verificada y registrada.'
}
if (-not (Test-Path -LiteralPath (Join-Path $Dir 'known_hosts'))) { Write-Warning 'Falta known_hosts: ejecutar con -HostKeyFingerprint o no habra lectura de energia.' }
Write-Output 'Clave PUBLICA del observador:'
Get-Content -LiteralPath "$key.pub"
if ($RegistrarTarea) {
    $script = Join-Path $Dir 'observador-pc.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`" -Dir `"$Dir`""
    # cada 5 minutos intenta arrancar; si ya corre, se ignora (asi se recupera solo si el proceso muere o tras iniciar sesion)
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 400)
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName 'BuryObservador' -Action $action -Trigger $trigger -Settings $settings -Description 'Observador externo del servidor TheBury' -Force | Out-Null
    Write-Output 'Tarea programada BuryObservador registrada.'
}
