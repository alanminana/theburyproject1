<#
.SYNOPSIS
  Instala TheBuryProject en Windows Server (o Windows 11 Pro) con Hyper-V: crea una VM Ubuntu y dentro corre el MISMO stack Docker Compose.

.DESCRIPTION
  Ejecutar en PowerShell COMO ADMINISTRADOR en el host con el rol Hyper-V. Hace, en orden:
    0. Preflight (solo lectura): SO, administrador, Hyper-V, RAM/disco/CPU, OpenSSH, git/qemu-img segun corresponda.
    1. Clave SSH dedicada (ed25519) en -StateDir, con ACL solo para Administradores/SYSTEM.
    2. Imagen base: -BaseImage (.vhdx, o .img/.qcow2 que se convierte con qemu-img) o -DownloadImage (Ubuntu 24.04 cloud image,
       verificada contra SHA256SUMS de Canonical).
    3. Disco de la VM (VHDX dinamico), ISO 'cidata' de cloud-init (usuario, clave SSH, hora, red fija o DHCP).
    4. VM Generacion 2 (Secure Boot con plantilla UEFI CA, memoria estatica, SIN checkpoints), conmutador externo, arranque
       automatico con el host y apagado ordenado (el invitado cierra SQL Server limpio).
    5. Dentro de la VM, por SSH: clona/transfiere el repositorio, scripts/install/server-linux-bootstrap.sh (Docker Engine, firewall de
       puertos publicados, ufw, cierre ordenado) y scripts/install/install.sh (el instalador comun de Linux: .env con secretos
       aleatorios, imagen, db-init -> migrate -> app -> Caddy, cron de backups).
    6. Trae a -StateDir la CA publica de Caddy (opcional: -TrustCaHere la instala en este host).
  Reutiliza docker-compose.yml, install.sh, deploy.sh y los scripts de backup SIN cambios: la VM es un Ubuntu comun.
  Es idempotente: si la VM ya existe se reutiliza y se reanudan los pasos de adentro. -PreflightOnly y -DryRun no cambian nada.
  Windows 11 con Docker Desktop (WSL2) sigue usando install-windows.ps1. Guia: docs/instalacion-windows-server.md

.EXAMPLE
  # Solo comprobar el host
  .\install-windows-server.ps1 -PreflightOnly

.EXAMPLE
  # Mostrar lo que haria
  .\install-windows-server.ps1 -DryRun -DownloadImage -AdminEmail soporte@example.com -VmIp 192.168.1.50/24 -Gateway 192.168.1.1 `
      -ExternalAdapter 'Ethernet' -CreateExternalSwitch -RepoUrl https://example.com/theburyproject.git

.EXAMPLE
  # Instalacion real con IP fija, repositorio local (bundle) y Tailscale instalado (sin autenticar)
  .\install-windows-server.ps1 -DownloadImage -AdminEmail soporte@example.com -VmIp 192.168.1.50/24 -Gateway 192.168.1.1 `
      -SwitchName 'TheBury-External' -RepoPath D:\git\theburyproject1 -AllowFrom 192.168.1.0/24,100.64.0.0/10 -AdminFrom 100.64.0.0/10 -Tailscale
  # -AllowFrom = quien usa la WEB (LAN + tailnet). -AdminFrom = quien administra por SSH: solo la tailnet y/o equipos /32; el host Hyper-V se agrega solo (el instalador lo necesita).
#>
[CmdletBinding()]
param(
    [string]$VmName = 'TheBury-ERP',
    [string]$VmRoot = '',
    [ValidateRange(2, 64)][int]$CpuCount = 4,
    [ValidateRange(4, 256)][int]$MemoryGB = 8,
    [ValidateRange(40, 4000)][int]$DiskGB = 80,

    [string]$BaseImage = '',
    [switch]$DownloadImage,
    [string]$ImageUrl = 'https://cloud-images.ubuntu.com/releases/noble/release/ubuntu-24.04-server-cloudimg-amd64.img',

    [string]$SwitchName = '',
    [string]$ExternalAdapter = '',
    [switch]$CreateExternalSwitch,
    [string]$VmIp = '',
    [string]$Gateway = '',
    [string[]]$Dns = @('1.1.1.1', '9.9.9.9'),
    [switch]$UseDhcp,
    [string]$TimeZone = 'America/Argentina/Buenos_Aires',

    [string]$RepoUrl = '',
    [string]$RepoPath = '',
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9._/-]*$')][string]$Ref = '',

    [string]$Domain = 'tbp',
    [string]$AdminEmail = '',
    [string]$AdminUser = 'admin',
    [ValidateSet('community', 'commercial')][string]$License = 'community',
    [string]$AutomapperKey = '',
    [string]$BackupDir = '/srv/bury-backups',
    [string[]]$AllowFrom = @(),
    [string[]]$AdminFrom = @(),
    [switch]$AllowLanAdmin,
    [switch]$Tailscale,

    [string]$StateDir = (Join-Path $env:ProgramData 'TheBury\server'),
    [string]$VmUser = 'bury',
    [switch]$TrustCaHere,
    [switch]$SkipInstall,
    [switch]$PreflightOnly,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$script:PreflightFallas = 0

function Write-Step([string]$m) { Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok([string]$m) { Write-Host "    OK   $m" -ForegroundColor Green }
function Write-Warn2([string]$m) { Write-Host "    AVISO $m" -ForegroundColor Yellow }
function Write-Dry([string]$m) { Write-Host "    [dry-run] $m" -ForegroundColor DarkGray }
function Fail([string]$m) { Write-Host "    ERROR $m" -ForegroundColor Red; exit 1 }

# ===================================================================================================================
# Funciones puras (sin efectos): las usa tambien Test-WindowsServerInstall.ps1
# ===================================================================================================================
function Test-Cidr([string]$Value, [switch]$AllowNoPrefix) {
    if ($Value -notmatch '^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?:/(\d{1,2}))?$') { return $false }
    foreach ($i in 1..4) { if ([int]$Matches[$i] -gt 255) { return $false } }
    if ($Matches[5]) { return ([int]$Matches[5] -le 32) }
    return [bool]$AllowNoPrefix
}

# Origen valido para administrar por SSH (minimo privilegio): un equipo (IPv4 o /32) o la red de Tailscale (dentro de 100.64.0.0/10, prefijo >= 10).
# Una red completa (p. ej. la LAN 192.168.1.0/24) daria administracion a todos los usuarios de la LAN: se rechaza salvo -AllowLanAdmin.
function Test-AdminSource([string]$Value) {
    if (-not (Test-Cidr $Value -AllowNoPrefix)) { return $false }
    $ip, $len = $Value -split '/'
    $o = [int]($ip -split '\.')[1]; $f = [int]($ip -split '\.')[0]
    $isTail = ($f -eq 100 -and $o -ge 64 -and $o -le 127)
    if (-not $len -or [int]$len -eq 32) { return ($isTail -or $f -eq 10 -or ($f -eq 172 -and $o -ge 16 -and $o -le 31) -or ($f -eq 192 -and $o -eq 168)) }   # equipo: privado o de la tailnet (nunca publico ni 0.0.0.0)
    return (($ip -split '\.')[0] -eq '100' -and $o -ge 64 -and $o -le 127 -and [int]$len -ge 10)
}

# IPv4 del host Hyper-V dentro de la red de la VM (el instalador administra la VM por SSH desde ahi). $null si no se puede determinar.
function Get-HostIpOnNetwork([string]$VmCidr) {
    if (-not $VmCidr) { return $null }
    try {
        # Preferir la IP de origen que el host usa realmente para llegar a la VM (puede haber varias IP en la subred).
        $r = @(Find-NetRoute -RemoteIPAddress (($VmCidr -split '/')[0]) -ErrorAction Stop | Where-Object { $_.IPAddress -and $_.IPAddress -notmatch '^(127|169\.254)\.' })
        if ($r.Count -and (Test-SameSubnet $VmCidr $r[0].IPAddress)) { return $r[0].IPAddress }
        $c = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object { $_.IPAddress -notmatch '^(127|169\.254)\.' -and (Test-SameSubnet $VmCidr $_.IPAddress) })
        if ($c.Count) { return $c[0].IPAddress }
    } catch { }
    return $null
}

# Red (CIDR) a la que pertenece una IP con prefijo: 192.168.1.50/24 -> 192.168.1.0/24
function Get-NetworkCidr([string]$IpCidr) {
    $ip, $len = $IpCidr -split '/'
    $len = [int]$len
    $b = [Net.IPAddress]::Parse($ip).GetAddressBytes()
    [Array]::Reverse($b)
    $n = [BitConverter]::ToUInt32($b, 0)
    $mask = if ($len -eq 0) { [uint32]0 } else { [uint32]([uint64]4294967295 -shl (32 - $len) -band 4294967295) }
    $net = $n -band $mask
    $nb = [BitConverter]::GetBytes([uint32]$net)
    [Array]::Reverse($nb)
    return ('{0}/{1}' -f ([Net.IPAddress]::new($nb)).ToString(), $len)
}

function Test-SameSubnet([string]$IpCidr, [string]$OtherIp) {
    $net = Get-NetworkCidr $IpCidr
    $len = [int](($IpCidr -split '/')[1])
    return ((Get-NetworkCidr ("$OtherIp/$len")) -eq $net)
}

function New-CloudInitUserData([string]$HostName, [string]$User, [string]$PublicKey, [string]$Tz) {
    if ($HostName -notmatch '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$') { throw "Nombre de host invalido para Linux: '$HostName' (minusculas, digitos y guiones)" }
    if ($User -notmatch '^[a-z_][a-z0-9_-]{0,31}$') { throw "Usuario Linux invalido: '$User'" }
    if ($PublicKey -notmatch '^ssh-(ed25519|rsa) [A-Za-z0-9+/=]+( [^\r\n]*)?$') { throw 'Clave publica SSH con formato inesperado' }
    if ($Tz -notmatch '^[A-Za-z0-9_+/-]+$') { throw "Zona horaria invalida: '$Tz'" }
    $lines = @(
        '#cloud-config',
        "hostname: $HostName",
        'manage_etc_hosts: true',
        "timezone: $Tz",
        'ssh_pwauth: false',
        'disable_root: true',
        'package_update: true',
        'packages:',
        '  - git',
        '  - linux-cloud-tools-virtual',
        'users:',
        "  - name: $User",
        '    gecos: TheBury administrador',
        '    groups: [sudo]',
        '    shell: /bin/bash',
        '    lock_passwd: true',
        '    sudo: "ALL=(ALL) NOPASSWD:ALL"',
        '    ssh_authorized_keys:',
        "      - $($PublicKey.Trim())"
    )
    return (($lines -join "`n") + "`n")
}

function New-CloudInitNetworkConfig([string]$IpCidr, [string]$Gw, [string[]]$DnsServers) {
    if (-not $IpCidr) {
        return "version: 2`nethernets:`n  nic0:`n    match:`n      driver: hv_netvsc`n    dhcp4: true`n    dhcp6: false`n"
    }
    if (-not (Test-Cidr $IpCidr)) { throw "-VmIp debe ser IPv4/prefijo (p. ej. 192.168.1.50/24): '$IpCidr'" }
    if (-not $Gw -or -not (Test-Cidr $Gw -AllowNoPrefix) -or $Gw.Contains('/')) { throw "-Gateway debe ser una IPv4 (p. ej. 192.168.1.1): '$Gw'" }
    if (-not (Test-SameSubnet $IpCidr $Gw)) { throw "El gateway $Gw no esta en la misma red que $IpCidr" }
    foreach ($d in $DnsServers) { if (-not (Test-Cidr $d -AllowNoPrefix) -or $d.Contains('/')) { throw "DNS invalido: '$d'" } }
    $lines = @(
        'version: 2', 'ethernets:', '  nic0:', '    match:', '      driver: hv_netvsc',
        '    dhcp4: false', '    dhcp6: false',
        '    addresses:', "      - $IpCidr",
        '    routes:', '      - to: default', "        via: $Gw",
        '    nameservers:', '      addresses:'
    )
    foreach ($d in $DnsServers) { $lines += "        - $d" }
    return (($lines -join "`n") + "`n")
}

# Quita comillas simples para pasarlo a bash entre '...'
function ConvertTo-BashQuoted([string]$s) { return "'" + ($s -replace "'", "'\''") + "'" }

# ISO9660+Joliet con etiqueta 'cidata' (datasource NoCloud de cloud-init) mediante IMAPI2 (incluido en Windows; no requiere ADK/oscdimg).
function New-SeedIso([string]$Path, [hashtable]$Files) {
    if (-not ('ISOFile' -as [type])) {
        $src = @'
public class ISOFile {
    public unsafe static void Create(string path, object stream, int blockSize, int totalBlocks) {
        int bytes = 0; byte[] buf = new byte[blockSize];
        System.IntPtr ptr = (System.IntPtr)(&bytes);
        System.IO.FileStream o = System.IO.File.OpenWrite(path);
        System.Runtime.InteropServices.ComTypes.IStream i = stream as System.Runtime.InteropServices.ComTypes.IStream;
        if (o != null && i != null) { while (totalBlocks-- > 0) { i.Read(buf, blockSize, ptr); o.Write(buf, 0, bytes); } o.Flush(); o.Close(); }
    }
}
'@
        $cp = New-Object System.CodeDom.Compiler.CompilerParameters
        $cp.CompilerOptions = '/unsafe'
        Add-Type -CompilerParameters $cp -TypeDefinition $src
    }
    $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $fsi.FileSystemsToCreate = 3   # ISO9660 + Joliet (los nombres con guion y minusculas se conservan por Joliet)
    $fsi.VolumeName = 'cidata'
    if (-not ('Win32.ShlwapiStream' -as [type])) {
        Add-Type -Namespace Win32 -Name ShlwapiStream -MemberDefinition '[DllImport("shlwapi.dll", CharSet=CharSet.Unicode, PreserveSig=false)] public static extern void SHCreateStreamOnFileEx(string f, uint mode, uint attr, bool create, System.Runtime.InteropServices.ComTypes.IStream tmpl, out System.Runtime.InteropServices.ComTypes.IStream stm);'
    }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('seed-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        foreach ($name in $Files.Keys) {
            $f = Join-Path $tmp $name
            [IO.File]::WriteAllText($f, [string]$Files[$name], (New-Object Text.UTF8Encoding($false)))
            $stm = $null
            [Win32.ShlwapiStream]::SHCreateStreamOnFileEx($f, 0, 128, $false, $null, [ref]$stm)
            $fsi.Root.AddFile($name, $stm)
        }
        $res = $fsi.CreateResultImage()
        if (Test-Path $Path) { Remove-Item $Path -Force }
        [ISOFile]::Create($Path, $res.ImageStream, $res.BlockSize, $res.TotalBlocks)
    } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

# ===================================================================================================================
# Preflight (solo lectura)
# ===================================================================================================================
function Add-Check([string]$Estado, [string]$Item, [string]$Detalle) {
    $c = switch ($Estado) { 'OK' { 'Green' } 'ATENCION' { 'Yellow' } 'FALLA' { 'Red' } default { 'Gray' } }
    Write-Host ('    [{0,-8}] {1}: {2}' -f $Estado, $Item, $Detalle) -ForegroundColor $c
    if ($Estado -eq 'FALLA') { $script:PreflightFallas++ }
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Preflight {
    Write-Step 'Preflight del host (solo lectura)'
    $os = Get-CimInstance Win32_OperatingSystem
    $esServer = ($os.ProductType -ne 1)
    $caption = $os.Caption
    if ($esServer) { Add-Check 'OK' 'Sistema operativo' "$caption (build $($os.BuildNumber))" }
    elseif ($caption -match 'Pro|Enterprise|Education') { Add-Check 'ATENCION' 'Sistema operativo' "${caption}: Hyper-V funciona, pero la plataforma prevista es Windows Server" }
    else { Add-Check 'FALLA' 'Sistema operativo' "${caption} no incluye Hyper-V (Home); usar Windows Server o Pro" }

    if (Test-Admin) { Add-Check 'OK' 'Administrador' 'PowerShell elevado' } else { Add-Check 'FALLA' 'Administrador' 'ejecutar PowerShell como Administrador' }

    $hv = Get-Command Get-VM -ErrorAction SilentlyContinue
    if ($hv) {
        Add-Check 'OK' 'Modulo Hyper-V' 'cmdlets disponibles'
        $svc = Get-Service vmms -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq 'Running') { Add-Check 'OK' 'Servicio vmms' 'en ejecucion' } else { Add-Check 'FALLA' 'Servicio vmms' 'no esta en ejecucion (reiniciar tras instalar el rol Hyper-V)' }
    } else {
        $cmd = if ($esServer) { 'Install-WindowsFeature Hyper-V -IncludeManagementTools -Restart' } else { 'Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All' }
        Add-Check 'FALLA' 'Hyper-V' "no instalado. Instalar (reinicia el equipo): $cmd"
    }

    $cs = Get-CimInstance Win32_ComputerSystem
    $ramGB = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    $need = $MemoryGB + 4
    if ($ramGB -ge $need) { Add-Check 'OK' 'RAM del host' "$ramGB GB (VM $MemoryGB GB + 4 GB para el host)" } else { Add-Check 'FALLA' 'RAM del host' "$ramGB GB; se necesitan al menos $need GB (VM $MemoryGB GB + 4 GB)" }
    $cores = ($cs.NumberOfLogicalProcessors)
    if ($cores -ge $CpuCount + 1) { Add-Check 'OK' 'CPU del host' "$cores logicos (VM $CpuCount)" } else { Add-Check 'ATENCION' 'CPU del host' "$cores logicos para una VM de $CpuCount" }

    $root = Resolve-VmRoot
    $drive = (Split-Path -Qualifier $root).TrimEnd(':')
    $free = [math]::Round((Get-PSDrive -Name $drive -ErrorAction SilentlyContinue).Free / 1GB, 1)
    $needDisk = $DiskGB + 10
    if ($free -ge $needDisk) { Add-Check 'OK' "Disco ${drive}: (VM)" "$free GB libres (disco maximo $DiskGB GB + 10 GB de margen)" } else { Add-Check 'FALLA' "Disco ${drive}: (VM)" "$free GB libres; se necesitan $needDisk GB en $root" }

    if ($cs.HypervisorPresent -or $hv) { Add-Check 'OK' 'Virtualizacion' 'hipervisor presente / disponible' }
    else { Add-Check 'ATENCION' 'Virtualizacion' 'activar VT-x/AMD-V en el firmware; si este host es una VM, habilitar virtualizacion anidada' }

    if (Get-Command ssh.exe -ErrorAction SilentlyContinue) { Add-Check 'OK' 'OpenSSH cliente' 'ssh.exe presente' } else { Add-Check 'FALLA' 'OpenSSH cliente' 'falta: Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0' }
    if (Get-Command ssh-keygen.exe -ErrorAction SilentlyContinue) { Add-Check 'OK' 'ssh-keygen' 'presente' } else { Add-Check 'FALLA' 'ssh-keygen' 'falta (parte de OpenSSH cliente)' }
    if (Get-Command scp.exe -ErrorAction SilentlyContinue) { Add-Check 'OK' 'scp' 'presente' } else { Add-Check 'FALLA' 'scp' 'falta (parte de OpenSSH cliente)' }

    $imapi = $true
    try { $null = New-Object -ComObject IMAPI2FS.MsftFileSystemImage } catch { $imapi = $false }
    if ($imapi) { Add-Check 'OK' 'IMAPI2 (ISO de cloud-init)' 'disponible' }
    else { Add-Check 'FALLA' 'IMAPI2 (ISO de cloud-init)' 'no disponible (p. ej. Server Core): este instalador genera el ISO con IMAPI2; usar Windows Server con Desktop Experience' }

    if ($RepoPath) {
        if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Add-Check 'FALLA' 'git' 'falta (necesario para -RepoPath: crea un bundle del repositorio)' }
        elseif (-not (Test-Path (Join-Path $RepoPath '.git'))) { Add-Check 'FALLA' '-RepoPath' "$RepoPath no es un repositorio git" }
        else { Add-Check 'OK' 'Repositorio local' $RepoPath }
    } elseif ($RepoUrl) { Add-Check 'OK' 'Repositorio' "se clonara $RepoUrl dentro de la VM (debe ser alcanzable desde la VM sin credenciales interactivas)" }
    else { Add-Check 'FALLA' 'Repositorio' 'indicar -RepoUrl o -RepoPath' }

    $imgExt = if ($BaseImage) { [IO.Path]::GetExtension($BaseImage).ToLowerInvariant() } else { '.img' }
    if ($BaseImage -and -not (Test-Path -LiteralPath $BaseImage)) { Add-Check 'FALLA' 'Imagen base' "no existe $BaseImage" }
    elseif (-not $BaseImage -and -not $DownloadImage) { Add-Check 'FALLA' 'Imagen base' 'indicar -BaseImage <.vhdx|.img|.qcow2> o -DownloadImage' }
    elseif ($imgExt -ne '.vhdx') {
        if (Get-Command qemu-img.exe -ErrorAction SilentlyContinue) { Add-Check 'OK' 'qemu-img' 'presente (convierte .img/.qcow2 a VHDX)' }
        else { Add-Check 'FALLA' 'qemu-img' 'falta para convertir la imagen a VHDX (instalar QEMU para Windows, o pasar un .vhdx ya convertido en -BaseImage)' }
    } else { Add-Check 'OK' 'Imagen base' $BaseImage }

    if ($hv) {
        $existe = Get-VM -Name $VmName -ErrorAction SilentlyContinue
        if ($existe) { Add-Check 'INFO' "VM '$VmName'" "ya existe (estado $($existe.State)): se reutiliza, no se recrea" }
        if ($SwitchName) {
            if (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue) { Add-Check 'OK' "Conmutador '$SwitchName'" 'existe' }
            elseif ($CreateExternalSwitch -and $ExternalAdapter) { Add-Check 'ATENCION' "Conmutador '$SwitchName'" "se creara sobre '$ExternalAdapter': el host pierde la red unos segundos" }
            else { Add-Check 'FALLA' "Conmutador '$SwitchName'" 'no existe (usar -CreateExternalSwitch -ExternalAdapter <nombre> para crearlo)' }
        } else { Add-Check 'FALLA' 'Conmutador virtual' 'indicar -SwitchName (externo, para que la VM tenga IP de la LAN)' }
    }
    if ($ExternalAdapter) {
        $na = Get-NetAdapter -Name $ExternalAdapter -ErrorAction SilentlyContinue
        if ($na -and $na.Status -eq 'Up') { Add-Check 'OK' "Adaptador '$ExternalAdapter'" "$($na.InterfaceDescription)" } else { Add-Check 'FALLA' "Adaptador '$ExternalAdapter'" 'no existe o no esta conectado' }
    }
    if (-not $VmIp -and -not $UseDhcp) { Add-Check 'FALLA' 'Direccion de la VM' 'indicar -VmIp <ip/prefijo> -Gateway <ip> (recomendado) o -UseDhcp (requiere reserva en el router)' }
    elseif ($VmIp) {
        try { $null = New-CloudInitNetworkConfig $VmIp $Gateway $Dns; Add-Check 'OK' 'Red de la VM' "$VmIp via $Gateway" } catch { Add-Check 'FALLA' 'Red de la VM' $_.Exception.Message }
    } else { Add-Check 'ATENCION' 'Red de la VM' 'DHCP: reservar la IP en el router o el ERP cambiara de direccion' }
    if (-not $SkipInstall -and -not $AdminEmail) { Add-Check 'FALLA' '-AdminEmail' 'falta el correo del administrador inicial' }
    foreach ($c in @($AllowFrom) + @($AdminFrom)) { if ($c -and -not (Test-Cidr $c -AllowNoPrefix)) { Add-Check 'FALLA' 'Redes permitidas' "valor invalido: $c" } }
    foreach ($c in @($AdminFrom)) { if ($c -and (Test-Cidr $c -AllowNoPrefix) -and -not $AllowLanAdmin -and -not (Test-AdminSource $c)) { Add-Check 'FALLA' '-AdminFrom' "'$c' es una red completa: la administracion solo se permite desde la tailnet (100.64.0.0/10) o equipos /32; la LAN solo usa la web (-AllowLanAdmin para forzarlo, no recomendado)" } }
}

function Resolve-VmRoot {
    if ($VmRoot) { return $VmRoot }
    if (Get-Command Get-VMHost -ErrorAction SilentlyContinue) { try { return (Join-Path (Get-VMHost).VirtualMachinePath $VmName) } catch { } }
    return (Join-Path $env:ProgramData "TheBury\vm\$VmName")
}

# Permite cargar las funciones con '. .\install-windows-server.ps1' (pruebas) sin ejecutar nada.
if ($MyInvocation.InvocationName -eq '.') { return }

# ===================================================================================================================
# Ejecucion
# ===================================================================================================================
if ($PreflightOnly) {
    Invoke-Preflight
    Write-Host ''
    if ($script:PreflightFallas -gt 0) { Write-Host "Preflight: $($script:PreflightFallas) FALLA(s). Resolverlas antes de instalar." -ForegroundColor Red; exit 1 }
    Write-Host 'Preflight sin FALLAS.' -ForegroundColor Green; exit 0
}

if (-not $DryRun -and -not (Test-Admin)) { Fail 'Ejecutar PowerShell como Administrador (o usar -DryRun / -PreflightOnly).' }
if ($VmName -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,14}$') { Fail "-VmName invalido: '$VmName' (1-15 caracteres: letras, digitos, guion)" }
$hostName = $VmName.ToLowerInvariant()
if ($BackupDir -notmatch '^/[A-Za-z0-9_./-]+$') { Fail '-BackupDir debe ser una ruta absoluta de Linux sin caracteres especiales' }

Invoke-Preflight
if ($script:PreflightFallas -gt 0) {
    if ($DryRun) { Write-Warn2 "(dry-run) preflight con $($script:PreflightFallas) FALLA(s): en una ejecucion real el script se detendria aqui." }
    else { Fail "Preflight con $($script:PreflightFallas) FALLA(s): resolverlas y repetir (es seguro repetir)." }
}

$vmRoot = Resolve-VmRoot
$vhdPath = Join-Path $vmRoot "$VmName.vhdx"
$seedPath = Join-Path $vmRoot 'seed.iso'
$keyPath = Join-Path $StateDir 'bury_vm_ed25519'
$knownHosts = Join-Path $StateDir 'known_hosts'
# Redes permitidas: por defecto la red de la VM (HTTPS y SSH). Con DHCP no se puede deducir: hay que indicarlas.
$vmNet = if ($VmIp) { Get-NetworkCidr $VmIp } else { '' }
$allowList = @($AllowFrom | Where-Object { $_ }); if ($allowList.Count -eq 0 -and $vmNet) { $allowList = @($vmNet) }
# Administracion (SSH): NUNCA la LAN completa por defecto. Se agrega el host Hyper-V (/32) porque el instalador administra la VM desde ahi;
# el resto (-AdminFrom) debe ser la tailnet y/o equipos concretos. Sin lista no se activa ufw.
$adminList = @($AdminFrom | Where-Object { $_ })
if (-not $AllowLanAdmin) { foreach ($a in $adminList) { if (-not (Test-AdminSource $a)) { Fail "-AdminFrom '$a' es una red completa: la administracion solo se permite desde la tailnet (100.64.0.0/10) o equipos /32 (-AllowLanAdmin para forzarlo)." } } }
$hostIp = Get-HostIpOnNetwork $VmIp
if ($hostIp) { if ($adminList -notcontains $hostIp -and $adminList -notcontains "$hostIp/32") { $adminList = @($hostIp) + $adminList } }
elseif ($adminList.Count -and -not ($adminList | Where-Object { $_ -notmatch '^100\.' -and (Test-AdminSource $_) })) {
    # ufw se activaria con SSH solo desde la tailnet y el instalador (host Hyper-V) perderia el acceso a mitad de la instalacion.
    Fail 'No se pudo determinar la IP del host Hyper-V en la red de la VM (p. ej. con -UseDhcp). Agregarla a -AdminFrom como equipo /32 (p. ej. -AdminFrom 192.168.1.10,100.64.0.0/10) para no perder el SSH al activar ufw.'
}
if ($allowList.Count -eq 0) { Fail 'Con -UseDhcp hay que indicar -AllowFrom (redes con acceso HTTPS, p. ej. 192.168.1.0/24).' }
if ($adminList.Count -eq 0) { Write-Warn2 'Sin IP de administracion (host o -AdminFrom) no se activa ufw (SSH queda sin restringir dentro de la VM).' }

# --- Ejecutables nativos ---------------------------------------------------------------------------------------------
# Windows PowerShell 5.1 convierte el stderr de un ejecutable nativo en error terminante bajo 'Stop' si se redirige: se llama siempre con
# 'Continue' y se devuelve solo el codigo de salida.
function Invoke-Exe([string]$File, [string[]]$Arguments, [switch]$Quiet) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { if ($Quiet) { & $File @Arguments | Out-Null } else { & $File @Arguments | Out-Host } } finally { $ErrorActionPreference = $old }
    return $LASTEXITCODE
}

# --- SSH ----------------------------------------------------------------------------------------------------------
$script:VmAddr = if ($VmIp) { ($VmIp -split '/')[0] } else { '' }
function Get-SshArgs { @('-i', $keyPath, '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new', '-o', "UserKnownHostsFile=$knownHosts", '-o', 'ConnectTimeout=8', '-o', 'LogLevel=ERROR', '-o', 'ServerAliveInterval=30') }
function Invoke-Ssh([string]$Command, [switch]$Quiet) {
    $a = @(Get-SshArgs) + @("$VmUser@$($script:VmAddr)", $Command)
    return (Invoke-Exe 'ssh.exe' $a -Quiet:$Quiet)
}
function Copy-ToVm([string]$Local, [string]$Remote) {
    $a = @(Get-SshArgs) + @($Local, "$VmUser@$($script:VmAddr):$Remote")
    if ((Invoke-Exe 'scp.exe' $a) -ne 0) { Fail "scp fallo ($Local)" }
}
# Si la VM se recrea con la misma IP su host key cambia: se descarta la entrada vieja (la nueva se acepta en el primer contacto, 'accept-new').
function Clear-KnownHost([string]$Addr) {
    if ($Addr -and (Test-Path $knownHosts)) { $null = Invoke-Exe 'ssh-keygen.exe' @('-R', $Addr, '-f', $knownHosts) -Quiet }
}

# --- 1. clave SSH -------------------------------------------------------------------------------------------------
Write-Step "Clave SSH dedicada en $StateDir"
if ($DryRun) { Write-Dry "ssh-keygen -t ed25519 -N '' -f $keyPath (ACL: solo Administradores y SYSTEM, por SID)" }
else {
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    if (-not (Test-Path $keyPath)) {
        # Start-Process con una sola cadena: la passphrase vacia (-N "") llega igual en Windows PowerShell 5.1 y en 7.x.
        $p = Start-Process -FilePath ssh-keygen.exe -ArgumentList "-q -t ed25519 -N `"`" -C thebury-server -f `"$keyPath`"" -Wait -NoNewWindow -PassThru
        if ($p.ExitCode -ne 0 -or -not (Test-Path $keyPath)) { Fail 'ssh-keygen fallo' }
    }
    if (-not (Test-Path "$keyPath.pub")) {
        $pubTxt = & ssh-keygen.exe -y -f $keyPath
        if ($LASTEXITCODE -ne 0 -or -not $pubTxt) { Fail "No se pudo derivar la clave publica de $keyPath" }
        [IO.File]::WriteAllText("$keyPath.pub", (($pubTxt -join '') + "`n"))
    }
    # Por SID (S-1-5-32-544 = Administradores, S-1-5-18 = SYSTEM): los nombres cambian segun el idioma de Windows.
    & icacls.exe $StateDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "icacls no pudo restringir $StateDir (la clave privada quedaria expuesta)" }
    Write-Ok "clave en $keyPath (la clave privada no sale de este host)"
}
$pubKey = if ($DryRun) { 'ssh-ed25519 AAAAC3NzaDRYRUNDRYRUN dry-run' } else { (Get-Content "$keyPath.pub" -Raw).Trim() }

# --- 2. imagen base (solo obtenerla y verificarla; el disco de la VM se crea justo antes de la VM) --------------------
Write-Step 'Imagen base de Ubuntu'
$vmExists = (-not $DryRun) -and [bool](Get-VM -Name $VmName -ErrorAction SilentlyContinue)
$img = $BaseImage
if ($vmExists) { Write-Ok "la VM '$VmName' ya existe: se omite la preparacion de la imagen" }
elseif (-not $img) {
    $dl = Join-Path $StateDir 'downloads'
    $img = Join-Path $dl ([IO.Path]::GetFileName(([uri]$ImageUrl).AbsolutePath))
    if ($DryRun) { Write-Dry "descargar $ImageUrl a $img y verificar SHA256 contra SHA256SUMS de Canonical" }
    else {
        New-Item -ItemType Directory -Force -Path $dl | Out-Null
        # PS 5.1: la barra de progreso de Invoke-WebRequest hace la descarga de ~600 MB mucho mas lenta; y Server 2016 no negocia TLS 1.2 por defecto.
        $ProgressPreference = 'SilentlyContinue'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $sumsUrl = ($ImageUrl -replace '[^/]+$', 'SHA256SUMS')
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $sumsUrl).Content
        $sums = if ($content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($content) } else { [string]$content }
        $leaf = [IO.Path]::GetFileName($img)
        $line = ($sums -split "`n") | Where-Object { $_ -match "\s\*?$([regex]::Escape($leaf))\s*$" } | Select-Object -First 1
        if (-not $line) { Fail "SHA256SUMS no lista $leaf" }
        $expected = ($line -split '\s+')[0].ToLowerInvariant()
        if (-not (Test-Path $img) -or ((Get-FileHash $img -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected)) {
            Invoke-WebRequest -UseBasicParsing -Uri $ImageUrl -OutFile $img
        }
        if ((Get-FileHash $img -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { Fail 'El SHA256 de la imagen descargada no coincide con SHA256SUMS' }
        Write-Ok "imagen verificada (SHA256 $expected)"
    }
}
elseif (-not $DryRun) { Write-Ok "imagen indicada: $img" }

# --- 3. cloud-init ------------------------------------------------------------------------------------------------
Write-Step 'ISO de cloud-init (usuario, clave SSH, hora, red)'
$userData = New-CloudInitUserData $hostName $VmUser $pubKey $TimeZone
$netCfg = New-CloudInitNetworkConfig $VmIp $Gateway $Dns
$metaData = "instance-id: $hostName-$([guid]::NewGuid().ToString('N').Substring(0, 8))`nlocal-hostname: $hostName`n"
if ($DryRun) { Write-Dry "crear $seedPath con user-data, meta-data y network-config (etiqueta cidata)" }
elseif ($vmExists) { Write-Ok 'VM existente: no se regenera el ISO' }
else {
    New-Item -ItemType Directory -Force -Path $vmRoot | Out-Null
    New-SeedIso $seedPath @{ 'user-data' = $userData; 'meta-data' = $metaData; 'network-config' = $netCfg }
    Write-Ok "ISO: $seedPath"
}

# --- 4. VM --------------------------------------------------------------------------------------------------------
Write-Step "VM Hyper-V '$VmName'"
if ($DryRun) {
    if ($CreateExternalSwitch) { Write-Dry "New-VMSwitch -Name '$SwitchName' -NetAdapterName '$ExternalAdapter' -AllowManagementOS `$true" }
    Write-Dry "disco: $vhdPath (VHDX dinamico de $DiskGB GB desde la imagen base)"
    Write-Dry "New-VM -Generation 2 -MemoryStartupBytes ${MemoryGB}GB; Set-VMProcessor -Count $CpuCount; Secure Boot = MicrosoftUEFICertificateAuthority"
    Write-Dry 'Set-VM -CheckpointType Disabled -AutomaticStartAction Start -AutomaticStopAction ShutDown; Start-VM'
}
elseif (-not $vmExists) {
    # Primero lo que puede fallar sin dejar nada (conmutador), recien despues el disco y la VM: asi un fallo no deja un VHDX huerfano.
    if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) {
        if (-not ($CreateExternalSwitch -and $ExternalAdapter)) { Fail "El conmutador '$SwitchName' no existe." }
        Write-Warn2 "Creando el conmutador externo '$SwitchName' sobre '$ExternalAdapter': la red del host se interrumpe unos segundos."
        New-VMSwitch -Name $SwitchName -NetAdapterName $ExternalAdapter -AllowManagementOS $true | Out-Null
    }
    if (Test-Path $vhdPath) { Fail "$vhdPath ya existe sin VM asociada: revisarlo y borrarlo a mano (no se sobrescribe)." }
    try {
        if ([IO.Path]::GetExtension($img).ToLowerInvariant() -eq '.vhdx') { Copy-Item -LiteralPath $img -Destination $vhdPath }
        else {
            # block_size=1M: recomendado por Microsoft para discos dinamicos de Linux (con 32 MB, valor por defecto de qemu, el VHDX crece de mas).
            if ((Invoke-Exe 'qemu-img.exe' @('convert', '-p', '-O', 'vhdx', '-o', 'subformat=dynamic,block_size=1M', $img, $vhdPath)) -ne 0) { throw 'qemu-img convert fallo' }
        }
        Resize-VHD -Path $vhdPath -SizeBytes ([int64]$DiskGB * 1GB)
    } catch {
        Remove-Item -LiteralPath $vhdPath -Force -ErrorAction SilentlyContinue
        Fail "No se pudo preparar el disco de la VM: $($_.Exception.Message)"
    }
    Write-Ok "disco listo: $vhdPath"
    $vm = New-VM -Name $VmName -Generation 2 -MemoryStartupBytes ([int64]$MemoryGB * 1GB) -VHDPath $vhdPath -SwitchName $SwitchName -Path $vmRoot
    Set-VMProcessor -VM $vm -Count $CpuCount
    Set-VMMemory -VM $vm -DynamicMemoryEnabled $false
    # Linux exige la plantilla 'UEFI CA' (la de Windows no arranca Ubuntu con Secure Boot activado).
    Set-VMFirmware -VM $vm -EnableSecureBoot On -SecureBootTemplate 'MicrosoftUEFICertificateAuthority'
    Add-VMDvdDrive -VM $vm -Path $seedPath
    $hd = Get-VMHardDiskDrive -VM $vm
    Set-VMFirmware -VM $vm -FirstBootDevice $hd
    # Sin checkpoints (un checkpoint de una VM con SQL Server puede corromper la base al revertirse).
    # El host arranca la VM solo y, al apagarse, pide un apagado ORDENADO al invitado (no "guardar estado" ni "apagar de golpe").
    # Los servicios de integracion Shutdown/Heartbeat/Time vienen habilitados por defecto (sus nombres cambian segun el idioma de Windows).
    Set-VM -VM $vm -CheckpointType Disabled -AutomaticStartAction Start -AutomaticStartDelay 30 -AutomaticStopAction ShutDown -AutomaticCriticalErrorAction None
    Clear-KnownHost $script:VmAddr
    Start-VM -VM $vm
    Write-Ok "VM creada y arrancada (CPU $CpuCount, RAM $MemoryGB GB, disco $DiskGB GB, sin checkpoints, arranque automatico con el host)"
}
else {
    $vm = Get-VM -Name $VmName
    Set-VM -VM $vm -CheckpointType Disabled -AutomaticStartAction Start -AutomaticStartDelay 30 -AutomaticStopAction ShutDown
    if ($vm.State -ne 'Running') { Start-VM -VM $vm }
    Write-Ok "VM existente: politica de arranque/apagado reafirmada (estado $((Get-VM -Name $VmName).State))"
}

# --- 5. esperar a la VM -------------------------------------------------------------------------------------------
if ($SkipInstall -or $DryRun) {
    Write-Step 'Instalacion dentro de la VM'
    if ($DryRun) {
        Write-Dry "esperar SSH en $VmUser@$($script:VmAddr)"
        Write-Dry ('repositorio: ' + $(if ($RepoPath) { "git bundle de $RepoPath -> scp -> git fetch/checkout" } else { "git clone $RepoUrl" }))
        Write-Dry "sudo scripts/install/server-linux-bootstrap.sh --user $VmUser --allow-from $($allowList -join ',') --admin-from $($adminList -join ',') --backup-dir $BackupDir$(if ($Tailscale) { ' --tailscale' })"
        Write-Dry "scripts/install/install.sh --domain $Domain --admin-email $AdminEmail --backup-dir $BackupDir --schedule --yes"
        Write-Dry "scp caddy-root-ca.crt -> $StateDir"
    } else { Write-Ok 'omitida (-SkipInstall)' }
    Write-Host ''; Write-Host '(dry-run: no se modifico nada)' -ForegroundColor DarkGray
    exit 0
}

if (-not $script:VmAddr) {
    Write-Step 'Buscando la IP de la VM (DHCP; requiere los servicios de integracion del invitado, que se instalan en el primer arranque)'
    for ($i = 0; $i -lt 60 -and -not $script:VmAddr; $i++) {
        $ip = (Get-VMNetworkAdapter -VMName $VmName).IPAddresses | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1
        if ($ip) { $script:VmAddr = $ip } else { Start-Sleep -Seconds 5 }
    }
    if (-not $script:VmAddr) { Fail 'La VM no informo ninguna IPv4. Ver su IP en la consola de Hyper-V y repetir con -VmIp, o usar IP fija (recomendado).' }
    Clear-KnownHost $script:VmAddr
    Write-Ok "IP de la VM: $($script:VmAddr)"
}
Write-Step "Esperando SSH en $VmUser@$($script:VmAddr) (el primer arranque tarda 1-3 minutos)"
$ok = $false
for ($i = 0; $i -lt 90; $i++) { if ((Invoke-Ssh 'true' -Quiet) -eq 0) { $ok = $true; break }; Start-Sleep -Seconds 5 }
if (-not $ok) { Fail 'La VM no respondio por SSH en 7,5 minutos. Ver la consola de la VM en Hyper-V (cloud-init / red). Si la VM se recreo con la misma IP, borrar su entrada de known_hosts.' }
$null = Invoke-Ssh 'sudo cloud-init status --wait' -Quiet
Write-Ok 'VM accesible por SSH'

# --- 6. repositorio -----------------------------------------------------------------------------------------------
Write-Step 'Repositorio dentro de la VM'
$remoteRepo = '$HOME/theburyproject1'
$gitGuard = 'command -v git >/dev/null || sudo apt-get -o DPkg::Lock::Timeout=300 install -y -qq git'
if ($RepoPath) {
    if (@(& git.exe -C $RepoPath status --porcelain).Count -gt 0) { Write-Warn2 "$RepoPath tiene cambios sin commit: NO viajan a la VM (solo lo commiteado de '$(if ($Ref) { $Ref } else { 'HEAD' })')." }
    $bundle = Join-Path ([IO.Path]::GetTempPath()) 'thebury.bundle'
    $refName = if ($Ref) { $Ref } else { 'HEAD' }
    if ((Invoke-Exe 'git.exe' @('-C', $RepoPath, 'bundle', 'create', $bundle, $refName)) -ne 0) { Fail 'git bundle fallo' }
    Copy-ToVm $bundle '/tmp/thebury.bundle'
    Remove-Item $bundle -Force
    # init + fetch del bundle a una referencia propia + checkout desacoplado: funciona igual para HEAD o para una rama, nueva o existente.
    $cmd = "set -e; $gitGuard; [ -d $remoteRepo/.git ] || git init -q $remoteRepo; git -C $remoteRepo fetch -q /tmp/thebury.bundle $(ConvertTo-BashQuoted "+${refName}:refs/bundle/head"); git -C $remoteRepo checkout -q -f --detach refs/bundle/head; rm -f /tmp/thebury.bundle"
}
else {
    $q = ConvertTo-BashQuoted $RepoUrl
    $checkout = if ($Ref) { "git -C $remoteRepo checkout -q $(ConvertTo-BashQuoted $Ref)" } else { 'true' }
    $cmd = "set -e; $gitGuard; if [ -d $remoteRepo/.git ]; then git -C $remoteRepo fetch -q --all && $checkout && (git -C $remoteRepo pull -q --ff-only || echo 'AVISO: no se pudo avanzar a la ultima version (ref fija o sin cambios)'); else git clone -q $q $remoteRepo && $checkout; fi"
}
if ((Invoke-Ssh $cmd) -ne 0) { Fail 'No se pudo obtener el repositorio dentro de la VM.' }
Write-Ok 'repositorio listo'

# --- 7. bootstrap + instalacion -----------------------------------------------------------------------------------
Write-Step 'Docker Engine, firewall y cierre ordenado (server-linux-bootstrap.sh)'
$bs = "sudo bash $remoteRepo/scripts/install/server-linux-bootstrap.sh --user $VmUser --allow-from $($allowList -join ',') --backup-dir $(ConvertTo-BashQuoted $BackupDir)"
if ($adminList.Count -gt 0) { $bs += " --admin-from $($adminList -join ',')" }
if ($AllowLanAdmin) { $bs += ' --allow-lan-admin' }
if ($Tailscale) { $bs += ' --tailscale' }
if ((Invoke-Ssh $bs) -ne 0) { Fail 'server-linux-bootstrap.sh fallo (ver la salida anterior; es seguro repetir).' }

Write-Step 'Instalando el ERP (install.sh): .env, imagen, db-init, migrate, app, Caddy, cron de backups'
$inst = "cd $remoteRepo && bash scripts/install/install.sh --domain $(ConvertTo-BashQuoted $Domain) --admin-email $(ConvertTo-BashQuoted $AdminEmail) --admin-user $(ConvertTo-BashQuoted $AdminUser) --license $License --backup-dir $(ConvertTo-BashQuoted $BackupDir) --schedule --yes"
if ($License -eq 'commercial') { $inst += " --automapper-key $(ConvertTo-BashQuoted $AutomapperKey)" }   # visible en 'ps' dentro de la VM mientras corre install.sh (limitacion conocida)
if ((Invoke-Ssh $inst) -ne 0) { Fail 'install.sh fallo. Es seguro volver a ejecutar este script.' }

# --- 8. CA de Caddy -----------------------------------------------------------------------------------------------
Write-Step 'CA publica de Caddy'
$caLocal = Join-Path $StateDir 'caddy-root-ca.crt'
$a = @(Get-SshArgs) + @("$VmUser@$($script:VmAddr):theburyproject1/caddy-root-ca.crt", $caLocal)
if ((Invoke-Exe 'scp.exe' $a) -eq 0 -and (Test-Path $caLocal)) {
    Write-Ok "CA en $caLocal (distribuir a los clientes: docs/red-privada-lan-vpn.md)"
    if ($TrustCaHere) { Import-Certificate -FilePath $caLocal -CertStoreLocation Cert:\LocalMachine\Root | Out-Null; Write-Ok 'CA instalada en este host' }
} else { Write-Warn2 'No se pudo traer la CA todavia; repetir luego: scp <vm>:theburyproject1/caddy-root-ca.crt .' }

Write-Host ''
Write-Host "Listo. VM '$VmName' en $($script:VmAddr). ERP: https://$Domain  (clientes: 'hosts' con '$($script:VmAddr) $Domain' + CA instalada)" -ForegroundColor Green
Write-Host 'Pendientes del propietario (P-05..P-08): sudo tailscale up (si -Tailscale), backup-offsite.env SFTP, custodia de claves, ver docs/instalacion-windows-server.md' -ForegroundColor Yellow
Write-Host "Verificar: scripts\install\Check-ErpHealth.ps1 -VmHost $($script:VmAddr) -VmName $VmName -VmKey `"$keyPath`" -VmKnownHosts `"$knownHosts`""
