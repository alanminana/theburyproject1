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

Add-Type -Namespace BuryObs -Name Win -UsingNamespace System.Text -MemberDefinition @'
private delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
[DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
public static bool HasVisibleWindow(uint processId, string title) {
    bool found = false;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
        uint pid; GetWindowThreadProcessId(h, out pid);
        if (pid != processId || !IsWindowVisible(h)) return true;
        var s = new StringBuilder(512); GetWindowText(h, s, 512);
        if (s.ToString() == title) { found = true; return false; }
        return true;
    }, IntPtr.Zero);
    return found;
}
'@

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
    # SIN ventana de consola (CreateNoWindow), NO con "-WindowStyle Hidden": ese estado oculto se aplica tambien a la primera ventana
    # del proceso y el aviso nunca aparece (medido en Windows 11: el proceso terminaba sin mostrar nada).
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $PSHOME 'powershell.exe'
    $psi.Arguments = "-NoProfile -STA -EncodedCommand $enc"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    # La prueba de que se mostro es HABER VISTO la ventana visible (cerrarla rapido sigue siendo un aviso entregado). Si el proceso
    # termina, o pasan 20 s, sin que se la haya visto, se usa msg.exe y se deja registro.
    $deadline = (Get-Date).AddSeconds(20)
    $seen = $false
    while (-not $seen -and (Get-Date) -lt $deadline) {
        $ended = $proc.HasExited
        $seen = [BuryObs.Win]::HasVisibleWindow([uint32]$proc.Id, [string]$env:BURY_OBS_TITLE)
        if ($ended) { break }
        if (-not $seen) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $seen) {
        if (-not $proc.HasExited) { try { $proc.Kill() } catch {} }
        Write-ObsLog "AVISO NO MOSTRADO en ventana ($($ev.code)): se intenta con msg.exe"
        try { & (Join-Path $env:SystemRoot 'System32\msg.exe') $env:USERNAME '/TIME:86400' "$($ev.code): $($ev.text)" } catch { Write-ObsLog 'msg.exe tampoco pudo mostrar el aviso' }
        return $false
    }
    return $true
}

function Test-ObsTcp([string]$ip, [int]$port) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $t = $c.ConnectAsync($ip, $port)
        return ($t.Wait(4000) -and $c.Connected)
    } catch { return $false } finally { $c.Close() }
}

# Ejecuta un programa con un limite de tiempo TOTAL (una sesion colgada no puede frenar al observador). Devuelve la 1a linea o ''.
function Invoke-ObsBounded([string]$exe, [string[]]$arguments, [int]$timeoutSeconds) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = ($arguments | ForEach-Object { '"' + ($_ -replace '"', '') + '"' }) -join ' '
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    try {
        $proc.StandardInput.Close()
        $out = $proc.StandardOutput.ReadToEndAsync()
        $null = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit($timeoutSeconds * 1000)) {
            try { $proc.Kill() } catch {}
            return ''
        }
        if (-not $out.Wait(2000)) { return '' }
        return ("$($out.Result)" -split "`r?`n")[0]
    } finally { $proc.Dispose() }
}

# Mide el destino de respaldos local. Solo lee nombres, fechas y tamanos (el contenido esta cifrado y no se abre).
function Get-ObsDestino {
    $c = $cfg.destino
    $r = [pscustomobject]@{ ok = $false; reason = ''; logAgeMin = $null; fileAgeHours = $null; usedGB = 0.0; capGB = [double]$c.topeGB; freeGB = 0.0 }
    $svc = Get-Service -Name $c.servicio -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne 'Running') { $r.reason = "servicio $($c.servicio) detenido"; return $r }
    if (-not (Test-ObsTcp '127.0.0.1' ([int]$c.puerto))) { $r.reason = "el puerto $($c.puerto) no responde"; return $r }
    try {
        if (-not (Test-Path -LiteralPath $c.ruta)) { $r.reason = 'carpeta de destino inexistente o unidad desconectada'; return $r }
        $all = @(Get-ChildItem -LiteralPath $c.ruta -Recurse -File -Force -ErrorAction Stop)
        $r.usedGB = ($all | Measure-Object -Property Length -Sum).Sum / 1GB
        $r.freeGB = ([System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($c.ruta))).AvailableFreeSpace / 1GB
    } catch { $r.reason = 'carpeta de destino ilegible'; return $r }
    # La fecha de modificacion la fija rclone con la del archivo de origen: mide la antiguedad del RESPALDO, no la de la subida.
    $now = [DateTime]::UtcNow
    foreach ($pair in @(@('sql', 'logAgeMin'), @('files', 'fileAgeHours'))) {
        $prefix = (Join-Path $c.ruta $pair[0]) + '\'
        $newest = $all | Where-Object { $_.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($newest) {
            $span = $now - $newest.LastWriteTimeUtc
            if ($pair[1] -eq 'logAgeMin') { $r.logAgeMin = $span.TotalMinutes } else { $r.fileAgeHours = $span.TotalHours }
        }
    }
    $r.ok = $true
    return $r
}

function Get-ObsSample {
    $s = @{ netOk = $false; hostOk = $false; erpOk = $false; power = $null }
    $s.netOk = [bool](Test-Connection -ComputerName $cfg.gateway -Count 2 -Quiet -ErrorAction SilentlyContinue)
    $s.hostOk = Test-ObsTcp $cfg.ip 22
    if (-not $s.hostOk) { $s.hostOk = Test-ObsTcp $cfg.ip ([int]$cfg.httpsPort) }
    if ($s.hostOk) { $s.netOk = $true }   # si la notebook responde, hay red aunque el router no conteste ping
    if ($s.hostOk) {
        $url = "https://$($cfg.domain):$($cfg.httpsPort)/health/ready"
        $code = Invoke-ObsBounded (Join-Path $env:SystemRoot 'System32\curl.exe') @('-s', '-o', 'NUL', '-w', '%{http_code}', '--max-time', '10', '--ssl-no-revoke', '--cacert', (Join-Path $Dir 'ca.crt'), '--resolve', "$($cfg.domain):$($cfg.httpsPort):$($cfg.ip)", $url) 15
        $s.erpOk = ("$code" -eq '200')
        $line = Invoke-ObsBounded (Join-Path $env:SystemRoot 'System32\OpenSSH\ssh.exe') @('-F', 'NUL', '-i', (Join-Path $Dir 'obs_ed25519'), '-o', 'BatchMode=yes', '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
            '-o', 'ConnectTimeout=8', '-o', 'ServerAliveInterval=5', '-o', 'ServerAliveCountMax=2', '-o', 'StrictHostKeyChecking=yes',
            '-o', "UserKnownHostsFile=$(Join-Path $Dir 'known_hosts')", "$($cfg.sshUser)@$($cfg.ip)", 'estado') 20
        $s.power = ConvertFrom-PowerLine "$line"
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

if ($ProbarAviso) {
    if (Show-ObsAlert (New-ObsEvent 'PRUEBA' 'aviso' 'Aviso de prueba del observador. No hay ningun incidente.')) { Write-Output 'Aviso mostrado en una ventana.'; exit 0 }
    Write-Output 'El aviso NO se mostro en una ventana.'; exit 1
}

if ($Once) {
    $sample = Get-ObsSample
    $p = $sample.power
    $detalle = 'sin lectura'
    if ($p) { $detalle = "ac=$($p.ac) bateria=$($p.cap)% $($p.status) monitor=$($p.monitor)" }
    Write-Output "red=$($sample.netOk) equipo=$($sample.hostOk) erp=$($sample.erpOk) energia: $detalle"
    if ($cfg.destino) {
        $dd = Get-ObsDestino
        Write-Output ("destino: disponible={0} {1} copiaBaseMin={2} copiaDiariaHoras={3} usadoGB={4} topeGB={5} libreGB={6}" -f $dd.ok, $dd.reason,
            $(if ($null -ne $dd.logAgeMin) { [int]$dd.logAgeMin } else { 'ninguna' }), $(if ($null -ne $dd.fileAgeHours) { [math]::Round($dd.fileAgeHours, 1) } else { 'ninguna' }),
            [math]::Round($dd.usedGB, 2), $dd.capGB, [int]$dd.freeGB)
    }
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
            $found = @(Update-ObsState $state $sample)
            if ($cfg.destino) {
                try {
                    $limits = @{ maxMinutosLogs = [int]$cfg.destino.maxMinutosLogs; maxHorasArchivos = [int]$cfg.destino.maxHorasArchivos; muestrasAtraso = 3 }
                    $found += @(Update-ObsDestState $state (Get-ObsDestino) ([bool]$sample.hostOk) $limits)
                } catch { Write-ObsLog "error al revisar el destino de respaldos: $($_.Exception.Message)" }
            }
            foreach ($ev in $found) {
                if ($null -eq $ev) { continue }
                Write-ObsLog "[$($ev.level)] $($ev.code) - $($ev.text)"
                $null = Show-ObsAlert $ev
            }
            $out = [ordered]@{}
            foreach ($k in $state.Keys) { $out[$k] = $state[$k] }
            $out['ultimaMuestra'] = (Get-Date).ToString('s')
            ($out | ConvertTo-Json) | Set-Content -LiteralPath $statePath -Encoding UTF8
        } catch { Write-ObsLog "error del observador: $($_.Exception.Message)" }
        Start-Sleep -Seconds ([int]$cfg.intervalSeconds)
    }
} finally { $mutex.ReleaseMutex() }
