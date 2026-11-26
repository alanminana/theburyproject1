# Observador EXTERNO del servidor desde la PC administradora (detecta lo que el propio servidor no puede avisar: caida completa).
#
#   observador-pc.ps1 -Dir D:\bury-observador            # bucle (cada intervalSeconds); es lo que corre la tarea programada
#   observador-pc.ps1 -Dir D:\bury-observador -Once      # una sola muestra, imprime el resultado y no avisa en pantalla
#   observador-pc.ps1 -Dir D:\bury-observador -ProbarAviso   # muestra un aviso de prueba y termina
#
# <Dir> contiene config.json, ca.crt (CA publica de Caddy), obs_ed25519 (clave dedicada, restringida a leer energia), estado.json y
# eventos.log. No guarda ni usa contrasenas. Solo ASCII (PowerShell 5.1).
param([Parameter(Mandatory = $true)][string]$Dir, [switch]$Once, [switch]$ProbarAviso)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'observador-logica.ps1')
$cfg = Get-Content (Join-Path $Dir 'config.json') -Raw | ConvertFrom-Json
$statePath = Join-Path $Dir 'estado.json'
$logPath = Join-Path $Dir 'eventos.log'

function Write-ObsLog([string]$text) {
    Add-Content -LiteralPath $logPath -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $text) -Encoding UTF8
}

function Show-ObsAlert($ev) {
    # Ventana siempre visible en un proceso aparte: queda hasta que alguien la cierre y no frena el bucle.
    $env:BURY_OBS_TITLE = "TheBury - $($ev.code)"
    $env:BURY_OBS_TEXT = "$(Get-Date -Format 'dd/MM HH:mm')  $($ev.text)"
    $icon = 'Information'
    if ($ev.level -eq 'critico') { $icon = 'Error' } elseif ($ev.level -eq 'aviso') { $icon = 'Warning' }
    $cmd = "Add-Type -AssemblyName System.Windows.Forms; " +
        "`$f = New-Object System.Windows.Forms.Form -Property @{TopMost=`$true}; " +
        "if ('$icon' -eq 'Error') { [console]::beep(900,600); [console]::beep(900,600) }; " +
        "[System.Windows.Forms.MessageBox]::Show(`$f, `$env:BURY_OBS_TEXT, `$env:BURY_OBS_TITLE, 'OK', '$icon') | Out-Null"
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile', '-STA', '-EncodedCommand', $enc | Out-Null
}

function Test-ObsTcp([string]$ip, [int]$port) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $t = $c.ConnectAsync($ip, $port)
        return ($t.Wait(4000) -and $c.Connected)
    } catch { return $false } finally { $c.Close() }
}

function Get-ObsSample {
    $s = @{ netOk = $false; hostOk = $false; erpOk = $false; power = $null }
    $s.netOk = [bool](Test-Connection -ComputerName $cfg.gateway -Count 2 -Quiet -ErrorAction SilentlyContinue)
    $s.hostOk = Test-ObsTcp $cfg.ip 22
    if (-not $s.hostOk) { $s.hostOk = Test-ObsTcp $cfg.ip ([int]$cfg.httpsPort) }
    if ($s.hostOk) { $s.netOk = $true }   # si la notebook responde, hay red aunque el router no conteste ping
    if ($s.hostOk) {
        $ErrorActionPreference = 'Continue'
        $url = "https://$($cfg.domain):$($cfg.httpsPort)/health/ready"
        $code = & curl.exe -s -o NUL -w '%{http_code}' --max-time 10 --ssl-no-revoke --cacert (Join-Path $Dir 'ca.crt') --resolve "$($cfg.domain):$($cfg.httpsPort):$($cfg.ip)" $url 2>$null
        $s.erpOk = ("$code" -eq '200')
        $line = & ssh.exe -i (Join-Path $Dir 'obs_ed25519') -o BatchMode=yes -o IdentitiesOnly=yes -o IdentityAgent=none -o ConnectTimeout=8 -o StrictHostKeyChecking=yes "$($cfg.sshUser)@$($cfg.ip)" estado 2>$null | Select-Object -First 1
        $s.power = ConvertFrom-PowerLine "$line"
        $ErrorActionPreference = 'Stop'
    }
    return $s
}

function Read-ObsState {
    $st = New-ObsState
    if (Test-Path -LiteralPath $statePath) {
        try {
            $saved = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            foreach ($k in @($st.Keys)) { if ($saved.PSObject.Properties.Name -contains $k) { $st[$k] = $saved.$k } }
        } catch { Write-ObsLog 'estado.json ilegible: se reinicia el estado' }
    }
    return $st
}

if ($ProbarAviso) { Show-ObsAlert (New-ObsEvent 'PRUEBA' 'aviso' 'Aviso de prueba del observador. No hay ningun incidente.'); return }

if ($Once) {
    $sample = Get-ObsSample
    $p = $sample.power
    $detalle = 'sin lectura'
    if ($p) { $detalle = "ac=$($p.ac) bateria=$($p.cap)% $($p.status) monitor=$($p.monitor)" }
    Write-Output "red=$($sample.netOk) equipo=$($sample.hostOk) erp=$($sample.erpOk) energia: $detalle"
    return
}

$mutex = New-Object System.Threading.Mutex($false, 'Local\BuryObservadorPC')
if (-not $mutex.WaitOne(0)) { return }   # ya hay un observador corriendo
try {
    Write-ObsLog 'observador iniciado'
    $state = Read-ObsState
    while ($true) {
        try {
            $sample = Get-ObsSample
            foreach ($ev in @(Update-ObsState $state $sample)) {
                if ($null -eq $ev) { continue }
                Write-ObsLog "[$($ev.level)] $($ev.code) - $($ev.text)"
                Show-ObsAlert $ev
            }
            $out = [ordered]@{}
            foreach ($k in $state.Keys) { $out[$k] = $state[$k] }
            $out['ultimaMuestra'] = (Get-Date).ToString('s')
            ($out | ConvertTo-Json) | Set-Content -LiteralPath $statePath -Encoding UTF8
        } catch { Write-ObsLog "error del observador: $($_.Exception.Message)" }
        Start-Sleep -Seconds ([int]$cfg.intervalSeconds)
    }
} finally { $mutex.ReleaseMutex() }
