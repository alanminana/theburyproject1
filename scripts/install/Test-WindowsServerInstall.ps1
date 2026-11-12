<#
.SYNOPSIS
  Pruebas de install-windows-server.ps1 SIN Hyper-V ni administrador: funciones puras, cloud-init y construccion real del ISO 'cidata'.

.DESCRIPTION
  Carga install-windows-server.ps1 con dot-source (no ejecuta nada) y verifica:
    - validacion de CIDR/gateway, calculo de red, rechazo de entradas peligrosas (inyeccion en user-data);
    - contenido de user-data / network-config (DHCP y fija);
    - New-SeedIso genera un ISO9660/Joliet de etiqueta 'cidata' con los 3 archivos (se relee el binario);
    - -PreflightOnly y -DryRun del instalador terminan sin modificar nada (se ejecutan en un proceso hijo).
  Opcional: -IsoOut <ruta> conserva el ISO para montarlo en Linux (scripts/install/test-server-linux.sh lo usa).
  Compatible con Windows PowerShell 5.1. Codigo de salida: 0 si todo pasa, 1 si algo falla.
#>
[CmdletBinding()]
param([string]$IsoOut = '')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$installer = Join-Path $here 'install-windows-server.ps1'
. $installer

$script:Fallos = 0
function Check([string]$Nombre, [scriptblock]$Prueba) {
    try {
        $r = & $Prueba
        if ($r -eq $false) { throw 'la comprobacion devolvio $false' }
        Write-Host "[PASA ] $Nombre" -ForegroundColor Green
    } catch { $script:Fallos++; Write-Host "[FALLA] $Nombre -> $($_.Exception.Message)" -ForegroundColor Red }
}
function Throws([scriptblock]$b) { try { & $b | Out-Null; return $false } catch { return $true } }

# --- CIDR / red ----------------------------------------------------------------------------------------------------
Check 'Test-Cidr acepta IPv4/CIDR validos' { (Test-Cidr '192.168.1.0/24') -and (Test-Cidr '100.64.0.0/10') -and (Test-Cidr '10.0.0.1' -AllowNoPrefix) }
Check 'Test-Cidr rechaza basura' { -not ((Test-Cidr '256.1.1.1/24') -or (Test-Cidr '1.2.3.4/33') -or (Test-Cidr '1.2.3') -or (Test-Cidr '10.0.0.1') -or (Test-Cidr '1.2.3.4/24; rm -rf /')) }
Check 'Get-NetworkCidr /24' { (Get-NetworkCidr '192.168.1.50/24') -eq '192.168.1.0/24' }
Check 'Get-NetworkCidr /22 y /10' { ((Get-NetworkCidr '10.20.7.9/22') -eq '10.20.4.0/22') -and ((Get-NetworkCidr '100.100.1.1/10') -eq '100.64.0.0/10') }
Check 'Test-SameSubnet' { (Test-SameSubnet '192.168.1.50/24' '192.168.1.1') -and -not (Test-SameSubnet '192.168.1.50/24' '192.168.2.1') }
Check 'Test-AdminSource acepta equipos /32 y la tailnet (minimo privilegio)' { (Test-AdminSource '192.168.1.5') -and (Test-AdminSource '192.168.1.5/32') -and (Test-AdminSource '100.64.0.0/10') -and (Test-AdminSource '100.101.2.3') -and (Test-AdminSource '100.100.0.0/16') }
Check 'Test-AdminSource rechaza redes LAN completas y rangos amplios' { -not ((Test-AdminSource '192.168.1.0/24') -or (Test-AdminSource '10.0.0.0/8') -or (Test-AdminSource '100.0.0.0/8') -or (Test-AdminSource '100.64.0.0/9') -or (Test-AdminSource '172.16.0.0/12')) }
Check 'Test-AdminSource rechaza equipos publicos y 0.0.0.0 (la administracion no se abre a Internet)' { -not ((Test-AdminSource '8.8.8.8') -or (Test-AdminSource '0.0.0.0') -or (Test-AdminSource '100.128.0.1') -or (Test-AdminSource '172.32.0.1') -or (Test-AdminSource '8.8.8.8/32')) }
Check 'Test-AdminSource acepta RFC 1918' { (Test-AdminSource '10.1.2.3') -and (Test-AdminSource '172.16.0.9') -and (Test-AdminSource '172.31.255.1') -and (Test-AdminSource '192.168.0.5') }
Check 'Test-AdminSource rechaza basura' { -not ((Test-AdminSource '256.1.1.1') -or (Test-AdminSource '1.2.3') -or (Test-AdminSource 'a; id')) }
Check 'Get-HostIpOnNetwork sin red de la VM devuelve $null' { $null -eq (Get-HostIpOnNetwork '') }

# --- cloud-init ----------------------------------------------------------------------------------------------------
$pub = 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFakeFakeFakeFakeFakeFakeFakeFakeFakeFakeFake thebury-server'
$ud = New-CloudInitUserData 'tbp-erp' 'bury' $pub 'America/Argentina/Buenos_Aires'
Check 'user-data: cabecera, usuario, clave, sin password' { $ud.StartsWith("#cloud-config`n") -and $ud -match 'lock_passwd: true' -and $ud -match 'ssh_pwauth: false' -and $ud.Contains($pub) }
Check 'user-data: LF y termina en salto de linea' { -not $ud.Contains("`r") -and $ud.EndsWith("`n") }
Check 'user-data rechaza host/usuario/zona/clave invalidos' {
    (Throws { New-CloudInitUserData 'Mal Host' 'bury' $pub 'UTC' }) -and (Throws { New-CloudInitUserData 'ok' 'bury; id' $pub 'UTC' }) -and
    (Throws { New-CloudInitUserData 'ok' 'bury' "ssh-ed25519 AAAA`nruncmd: evil" 'UTC' }) -and (Throws { New-CloudInitUserData 'ok' 'bury' $pub "UTC`nruncmd: x" })
}
$nd = New-CloudInitNetworkConfig '' '' @()
Check 'network-config DHCP' { $nd -match 'dhcp4: true' -and $nd -match 'driver: hv_netvsc' }
$nf = New-CloudInitNetworkConfig '192.168.1.50/24' '192.168.1.1' @('1.1.1.1', '9.9.9.9')
Check 'network-config fija: ip, ruta, dns' { $nf -match '- 192.168.1.50/24' -and $nf -match 'via: 192.168.1.1' -and $nf -match '- 9.9.9.9' -and $nf -match 'dhcp4: false' }
Check 'network-config rechaza gateway fuera de la red o invalido' {
    (Throws { New-CloudInitNetworkConfig '192.168.1.50/24' '10.0.0.1' @('1.1.1.1') }) -and (Throws { New-CloudInitNetworkConfig '192.168.1.50' '192.168.1.1' @('1.1.1.1') }) -and
    (Throws { New-CloudInitNetworkConfig '192.168.1.50/24' '' @('1.1.1.1') }) -and (Throws { New-CloudInitNetworkConfig '192.168.1.50/24' '192.168.1.1' @('1.1.1.1; x') })
}
Check 'ConvertTo-BashQuoted escapa comillas simples' { (ConvertTo-BashQuoted "a'b") -eq "'a'\''b'" }

# YAML valido (si hay python con PyYAML)
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('ci-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $tmp | Out-Null
    [IO.File]::WriteAllText("$tmp\ud.yaml", $ud); [IO.File]::WriteAllText("$tmp\nf.yaml", $nf); [IO.File]::WriteAllText("$tmp\nd.yaml", $nd)
    $out = & python -I -c "import sys
try:
    import yaml
except ImportError:
    print('SIN_YAML'); sys.exit(0)
for f in sys.argv[1:]:
    yaml.safe_load(open(f, encoding='utf-8'))
print('YAML_OK')" "$tmp\ud.yaml" "$tmp\nf.yaml" "$tmp\nd.yaml"
    Remove-Item $tmp -Recurse -Force
    if ($out -match 'SIN_YAML') { Write-Host '[OMITE] YAML valido: PyYAML no instalado' -ForegroundColor Yellow }
    else { Check 'YAML valido (PyYAML)' { $out -match 'YAML_OK' } }
}

# --- ISO real ------------------------------------------------------------------------------------------------------
$haveImapi = $true
try { $null = New-Object -ComObject IMAPI2FS.MsftFileSystemImage } catch { $haveImapi = $false }
if (-not $haveImapi) { Write-Host '[OMITE] ISO cidata: IMAPI2 no disponible en este equipo (Server Core sin Desktop Experience)' -ForegroundColor Yellow }
else {
$iso = if ($IsoOut) { $IsoOut } else { Join-Path ([IO.Path]::GetTempPath()) ('seed-test-' + [guid]::NewGuid().ToString('N') + '.iso') }
Check 'New-SeedIso crea el ISO' {
    New-SeedIso $iso @{ 'user-data' = $ud; 'meta-data' = "instance-id: test-1`nlocal-hostname: tbp-erp`n"; 'network-config' = $nf }
    (Test-Path $iso) -and ((Get-Item $iso).Length -gt 20000)
}
function Find-Bytes([byte[]]$Haystack, [byte[]]$Needle) {
    $n = $Needle.Length
    for ($i = 0; $i -le $Haystack.Length - $n; $i++) {
        if ($Haystack[$i] -ne $Needle[0]) { continue }
        $j = 1; while ($j -lt $n -and $Haystack[$i + $j] -eq $Needle[$j]) { $j++ }
        if ($j -eq $n) { return $true }
    }
    return $false
}
Check "ISO: etiqueta 'cidata' y los 3 archivos (nombres Joliet UTF-16BE)" {
    $b = [IO.File]::ReadAllBytes($iso)
    $u = [Text.Encoding]::BigEndianUnicode
    $lab = (Find-Bytes $b ([Text.Encoding]::ASCII.GetBytes('cidata'))) -or (Find-Bytes $b ([Text.Encoding]::ASCII.GetBytes('CIDATA'))) -or (Find-Bytes $b ($u.GetBytes('cidata')))
    $lab -and (Find-Bytes $b ($u.GetBytes('user-data'))) -and (Find-Bytes $b ($u.GetBytes('meta-data'))) -and (Find-Bytes $b ($u.GetBytes('network-config')))
}
Check 'ISO: contenido de user-data presente (clave y hostname)' {
    $b = [IO.File]::ReadAllBytes($iso); $l = [Text.Encoding]::GetEncoding(28591).GetString($b)
    $l.Contains('hostname: tbp-erp') -and $l.Contains('AAAAC3NzaC1lZDI1NTE5')
}
}
if (-not $IsoOut -and $haveImapi) { Remove-Item $iso -Force -ErrorAction SilentlyContinue }

# --- instalador en proceso hijo: -PreflightOnly / -DryRun no modifican nada -------------------------------------------
$ps = (Get-Command powershell.exe -ErrorAction SilentlyContinue)
if ($ps) {
    $stateTest = Join-Path ([IO.Path]::GetTempPath()) ('state-' + [guid]::NewGuid().ToString('N'))
    $commonArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer, '-StateDir', $stateTest, '-VmRoot', (Join-Path $stateTest 'vm'), '-VmIp', '192.168.1.50/24', '-Gateway', '192.168.1.1', '-SwitchName', 'TheBury-External', '-RepoUrl', 'https://example.invalid/r.git', '-DownloadImage', '-AdminEmail', 'qa@example.test')
    Check '-DryRun termina con 0, no crea StateDir ni VM' {
        $o = & powershell.exe @commonArgs -DryRun 2>&1 | Out-String
        ($LASTEXITCODE -eq 0) -and $o.Contains('dry-run: no se modifico nada') -and -not (Test-Path $stateTest) -and $o.Contains('server-linux-bootstrap.sh') -and $o.Contains('--allow-from 192.168.1.0/24')
    }
    Check '-PreflightOnly sin Hyper-V/admin informa FALLAS y devuelve 1 (sin tocar nada)' {
        $hasHv = [bool](Get-Command Get-VM -ErrorAction SilentlyContinue)
        $o = & powershell.exe @commonArgs -PreflightOnly 2>&1 | Out-String
        if ($hasHv -and (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator'))) { return $true }
        ($LASTEXITCODE -eq 1) -and $o.Contains('FALLA') -and -not (Test-Path $stateTest)
    }
    Check 'entradas invalidas se rechazan (-VmName, -BackupDir)' {
        & powershell.exe @commonArgs -DryRun -VmName 'mal nombre' 2>&1 | Out-Null; $a = $LASTEXITCODE
        & powershell.exe @commonArgs -DryRun -BackupDir '/srv/x;id' 2>&1 | Out-Null; $b = $LASTEXITCODE
        ($a -ne 0) -and ($b -ne 0)
    }
}

Write-Host ''
if ($script:Fallos -eq 0) { Write-Host 'Todas las pruebas del instalador de Windows Server pasaron.' -ForegroundColor Green; exit 0 }
Write-Host "$($script:Fallos) prueba(s) fallaron." -ForegroundColor Red; exit 1
