# DESTINO DE RESPALDOS EN UNA PC WINDOWS (SFTP con el OpenSSH Server de Windows). La copia la CIFRA el servidor (rclone crypt, ver
# docs/backup-restore.md seccion 6): esta PC solo guarda datos ilegibles. Ejecutar como ADMINISTRADOR en la PC que va a recibir las copias.
#
#   destino-pc-windows.ps1 -ClienteIp 192.168.50.223 -ClavePublica .\servidor.pub -Raiz E:\bury-respaldo-pc -TopeGB 50
#   destino-pc-windows.ps1 -CortePruebaMinutos 4      # detiene el servicio N minutos y lo vuelve a iniciar (prueba de alertas)
#   destino-pc-windows.ps1 -Quitar                    # cierra el acceso (servicio, firewall, usuario). NO borra las copias
#
# Deja: usuario local SIN privilegios (solo SFTP encerrado en -Raiz, solo clave y solo desde -ClienteIp), regla de firewall unica para
# esa IP, cuota NTFS del usuario como tope real de espacio, servicio sshd automatico. La contrasena del usuario es aleatoria y NO se
# guarda ni se muestra (no se usa: el acceso es solo por clave). Informe en -Informe (sin secretos). Solo ASCII (PowerShell 5.1).
param(
    [string]$ClienteIp,
    [string]$ClavePublica,
    [string]$Raiz = 'E:\bury-respaldo-pc',
    [int]$TopeGB = 50,
    [int]$Puerto = 2022,
    [string]$Usuario = 'bury-backup',
    [string]$Lector = "$env:USERDOMAIN\$env:USERNAME",
    [string]$Informe = (Join-Path $env:PUBLIC 'bury-destino-informe.txt'),
    [int]$CortePruebaMinutos = 0,
    [switch]$Quitar
)
$ErrorActionPreference = 'Stop'
$Regla = 'Bury-SFTP-respaldos'
$SshData = Join-Path $env:ProgramData 'ssh'
$SshBin = Join-Path $env:SystemRoot 'System32\OpenSSH'
$Marca = '# bury-destino-respaldos (generado por destino-pc-windows.ps1)'
# SID fijos: los nombres de los grupos cambian con el idioma de Windows
$SidSystem = '*S-1-5-18'; $SidAdmins = '*S-1-5-32-544'; $SidAutenticados = '*S-1-5-11'

function Log([string]$t) {
    $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $t
    Write-Output $line
    Add-Content -LiteralPath $Informe -Value $line -Encoding UTF8
}
function Invoke-Native([string]$exe, [string[]]$arguments) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $out = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE; $ErrorActionPreference = $prev
    if ($code -ne 0) { throw "$(Split-Path $exe -Leaf) fallo (codigo $code): $($out -join ' | ')" }
    return $out
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecutar como administrador.' }
Set-Content -LiteralPath $Informe -Value "bury-destino $(Get-Date -Format s)" -Encoding UTF8

try {
    if ($CortePruebaMinutos -gt 0) {
        if ($CortePruebaMinutos -gt 30) { throw 'El corte de prueba admite como maximo 30 minutos.' }
        Log "CORTE DE PRUEBA: se detiene sshd $CortePruebaMinutos minuto(s)"
        try { Stop-Service sshd -Force; Start-Sleep -Seconds (60 * $CortePruebaMinutos) }
        finally { Start-Service sshd; Log "sshd: $((Get-Service sshd).Status)" }
        Log 'RESULTADO=OK'; return
    }

    if ($Quitar) {
        Stop-Service sshd -Force -ErrorAction SilentlyContinue
        Set-Service sshd -StartupType Manual
        Remove-NetFirewallRule -Name $Regla -ErrorAction SilentlyContinue
        if (Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue) { Disable-LocalUser -Name $Usuario }
        Log "Acceso cerrado. Las copias siguen en $Raiz (no se borra nada)."
        Log 'RESULTADO=OK'; return
    }

    $ip = $null
    if (-not [System.Net.IPAddress]::TryParse($ClienteIp, [ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork') { throw '-ClienteIp debe ser una direccion IPv4 unica.' }
    if ($Usuario -cnotmatch '^[a-z][a-z0-9-]{2,19}$') { throw '-Usuario: solo minusculas, digitos y guion (AllowUsers de Windows exige minusculas).' }
    if (-not $ClavePublica -or -not (Test-Path -LiteralPath $ClavePublica)) { throw 'Falta -ClavePublica (archivo .pub del servidor).' }
    $pub = (Get-Content -LiteralPath $ClavePublica -Raw).Trim()
    if ($pub -notmatch '^ssh-ed25519 [A-Za-z0-9+/=]{60,100}( [A-Za-z0-9@._-]+)?$') { throw '-ClavePublica no es UNA clave publica ssh-ed25519.' }
    if ($TopeGB -lt 5) { throw '-TopeGB debe ser al menos 5.' }
    $full = [System.IO.Path]::GetFullPath($Raiz)
    $drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -ne 'Fixed' -or $drive.DriveFormat -ne 'NTFS') { throw "La unidad de $full debe ser fija y NTFS." }
    if ($full.TrimEnd('\').Length -le 3) { throw '-Raiz no puede ser la raiz de una unidad.' }
    if ($drive.AvailableFreeSpace -lt ($TopeGB * 1GB)) { throw "No hay $TopeGB GB libres en $($drive.Name)." }
    if (-not (Test-Path -LiteralPath (Join-Path $SshBin 'sshd.exe'))) { throw 'OpenSSH Server no esta instalado (Caracteristicas opcionales de Windows).' }
    $busy = @(Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue | Where-Object { (Get-Process -Id $_.OwningProcess).ProcessName -ne 'sshd' })
    if ($busy.Count) { throw "El puerto $Puerto ya esta en uso por otro programa." }

    # 1) cerrar la entrada ANTES de tocar el servicio: la regla que trae Windows abre el puerto 22 a cualquiera
    $svc = Get-Service sshd
    if ($svc.Status -eq 'Running') { Stop-Service sshd -Force }
    Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue | Disable-NetFirewallRule
    Remove-NetFirewallRule -Name $Regla -ErrorAction SilentlyContinue
    Log 'Firewall: regla generica de OpenSSH deshabilitada.'

    # 2) usuario dedicado sin privilegios
    if (-not (Get-LocalUser -Name $Usuario -ErrorAction SilentlyContinue)) {
        $bytes = New-Object byte[] 36
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $secure = ConvertTo-SecureString ('aA1!' + [Convert]::ToBase64String($bytes)) -AsPlainText -Force
        New-LocalUser -Name $Usuario -Password $secure -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires -Description 'Solo SFTP de respaldos cifrados' | Out-Null
        $secure = $null; [Array]::Clear($bytes, 0, $bytes.Length)
        Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $Usuario
        Log "Usuario $Usuario creado."
    }
    Enable-LocalUser -Name $Usuario
    $admins = @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { ($_.Name -split '\\')[-1] })
    if ($admins -contains $Usuario) { throw "El usuario $Usuario es administrador: no se continua." }
    $hide = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList'
    if (-not (Test-Path $hide)) { New-Item -Path $hide -Force | Out-Null }
    New-ItemProperty -Path $hide -Name $Usuario -Value 0 -PropertyType DWord -Force | Out-Null
    Log "Usuario $Usuario habilitado, sin privilegios y oculto en la pantalla de inicio."

    # 3) carpetas: la raiz (carcel SFTP) es de solo lectura para el usuario; escribe unicamente en datos\
    $datos = Join-Path $full 'datos'
    New-Item -ItemType Directory -Force -Path $datos | Out-Null
    $userAcc = "$env:COMPUTERNAME\$Usuario"
    Invoke-Native 'icacls.exe' @($full, '/inheritance:r', '/grant:r', "${SidSystem}:(OI)(CI)F", "${SidAdmins}:(OI)(CI)F", "${Lector}:(OI)(CI)RX", "${userAcc}:(RX)") | Out-Null
    Invoke-Native 'icacls.exe' @($datos, '/grant:r', "${userAcc}:(OI)(CI)M") | Out-Null
    Log "Carpetas y permisos listos en $full."

    # 4) tope de espacio REAL: cuota NTFS del usuario (el resto de las cuentas queda sin limite)
    $vol = $drive.Name.TrimEnd('\')
    Invoke-Native 'fsutil.exe' @('quota', 'enforce', $vol) | Out-Null
    $limit = [int64]$TopeGB * 1GB
    $warn = [int64]($limit * 0.9)
    Invoke-Native 'fsutil.exe' @('quota', 'modify', $vol, "$warn", "$limit", $userAcc) | Out-Null
    Log "Cuota NTFS en ${vol}: $TopeGB GB para $Usuario."

    # 5) configuracion de sshd: un unico usuario, desde una unica IP, solo clave, solo SFTP
    if (-not (Test-Path $SshData)) { New-Item -ItemType Directory -Path $SshData | Out-Null }
    Invoke-Native 'icacls.exe' @($SshData, '/inheritance:r', '/grant:r', "${SidSystem}:(OI)(CI)F", "${SidAdmins}:(OI)(CI)F", "${SidAutenticados}:(OI)(CI)RX") | Out-Null
    $hostKey = Join-Path $SshData 'ssh_host_ed25519_key'
    if (-not (Test-Path -LiteralPath $hostKey)) { Invoke-Native (Join-Path $SshBin 'ssh-keygen.exe') @('-A') | Out-Null }
    $keys = Join-Path $SshData "${Usuario}_authorized_keys"
    Set-Content -LiteralPath $keys -Value "from=`"$ClienteIp`",restrict $pub" -Encoding ASCII
    Invoke-Native 'icacls.exe' @($keys, '/inheritance:r', '/grant:r', "${SidSystem}:F", "${SidAdmins}:F") | Out-Null
    $config = Join-Path $SshData 'sshd_config'
    if ((Test-Path -LiteralPath $config) -and -not ((Get-Content -LiteralPath $config -TotalCount 1) -eq $Marca)) {
        $backup = "$config.antes-bury-$(Get-Date -Format yyyyMMddHHmmss)"
        Copy-Item -LiteralPath $config -Destination $backup
        Log "Habia un sshd_config previo: copia en $backup"
    }
    $text = @(
        $Marca,
        "Port $Puerto",
        'AddressFamily inet',
        'HostKey __PROGRAMDATA__/ssh/ssh_host_ed25519_key',
        'PubkeyAuthentication yes',
        'PasswordAuthentication no',
        'KbdInteractiveAuthentication no',
        'PermitEmptyPasswords no',
        'GSSAPIAuthentication no',
        "AllowUsers $Usuario@$ClienteIp",
        "AuthorizedKeysFile __PROGRAMDATA__/ssh/${Usuario}_authorized_keys",
        'AllowTcpForwarding no',
        'AllowAgentForwarding no',
        'MaxAuthTries 3',
        'LogLevel INFO',
        'Subsystem sftp internal-sftp',
        'ForceCommand internal-sftp',
        "ChrootDirectory $($full -replace '\\', '/')"
    )
    $candidate = "$config.nuevo"
    Set-Content -LiteralPath $candidate -Value $text -Encoding ASCII
    Invoke-Native (Join-Path $SshBin 'sshd.exe') @('-t', '-f', $candidate) | Out-Null
    Move-Item -LiteralPath $candidate -Destination $config -Force
    Log 'sshd_config validado e instalado.'

    # 6) servicio + unica regla de entrada
    Set-Service sshd -StartupType Automatic
    Start-Service sshd
    New-NetFirewallRule -Name $Regla -DisplayName 'Bury: SFTP de respaldos (solo el servidor)' -Direction Inbound -Action Allow -Protocol TCP `
        -LocalPort $Puerto -RemoteAddress $ClienteIp -Profile Private | Out-Null
    Start-Sleep -Seconds 3
    if ((Get-Service sshd).Status -ne 'Running') { throw 'sshd no quedo iniciado (ver el Visor de eventos > OpenSSH).' }
    if (-not (Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue)) { throw "sshd no escucha en el puerto $Puerto." }
    $otras = @(Get-NetFirewallRule -Direction Inbound -Enabled True -Action Allow -ErrorAction SilentlyContinue | Where-Object {
            $f = $_ | Get-NetFirewallPortFilter; $_.Name -ne $Regla -and $f.Protocol -eq 'TCP' -and @($f.LocalPort) -contains "$Puerto" })
    foreach ($o in $otras) { Log "AVISO: otra regla de entrada tambien abre el puerto ${Puerto}: $($o.Name)" }
    Log "Servicio sshd: Running (inicio automatico), puerto $Puerto, firewall solo desde $ClienteIp."
    Log "CLAVE_HOST=$((Get-Content -LiteralPath "$hostKey.pub" -Raw).Trim() -replace '\s+\S+@\S+$', '')"
    Log "HUELLA_HOST=$((Invoke-Native (Join-Path $SshBin 'ssh-keygen.exe') @('-lf', "$hostKey.pub")) -join ' ')"
    Log 'RESULTADO=OK'
} catch {
    Log "ERROR: $($_.Exception.Message)"
    Log 'RESULTADO=FALLO'
    exit 1
}
