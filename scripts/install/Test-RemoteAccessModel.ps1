<#
.SYNOPSIS
  Pruebas del modelo de acceso de Windows 11 (Test-ErpRemoteAccess.ps1 y Enable-ErpRemoteAdmin.ps1) con reglas SINTETICAS.
  NO lee ni modifica el firewall real y no requiere administrador. Windows PowerShell 5.1 y PowerShell 7.
    powershell -NoProfile -File scripts/install/Test-RemoteAccessModel.ps1     (exit 0 = todo OK)
#>
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'Test-ErpRemoteAccess.ps1')
. (Join-Path $here 'Enable-ErpRemoteAdmin.ps1') -AdminTailscaleIp 100.64.0.1
$ErrorActionPreference = 'Continue'   # los scripts dot-sourced fijan Stop; con Stop, el stderr de un proceso hijo (2>&1) se vuelve error terminante en PS 5.1
$script:pass = 0; $script:fail = 0
function Check([string]$name, [scriptblock]$t) {
    $ok = $false; try { $ok = [bool](& $t) } catch { Write-Host "  excepcion: $($_.Exception.Message)" }
    if ($ok) { $script:pass++; Write-Host "PASS $name" } else { $script:fail++; Write-Host "FAIL $name" }
}
function Rule([string]$name, $ports, $remote, [string]$iface = '', [string]$proto = 'TCP', [string]$prog = 'Any') {
    [pscustomobject]@{ Name = $name; Protocol = $proto; LocalPort = @($ports); RemoteAddress = @($remote); InterfaceAlias = $iface; Program = $prog }
}
function Bad($rules) { @(Get-AccessModelFindings $rules).Count -gt 0 }
function Good($rules) { @(Get-AccessModelFindings $rules).Count -eq 0 }

# --- reglas correctas (lo que crea install-windows.ps1 y Enable-ErpRemoteAdmin.ps1) ---------------------------------------------------------
Check 'OK: 443 desde la LAN (LocalSubnet) es la web, no administracion' { Good @(Rule 'TheBury ERP 443' 443 'LocalSubnet') }
Check 'OK: 443 desde la tailnet atada al adaptador Tailscale' { Good @(Rule 'TheBury ERP 443 Tailscale' 443 '100.64.0.0/10' 'Tailscale') }
Check 'OK: RDP solo desde la IP del administrador por el adaptador Tailscale' { Good @(Rule 'Admin RDP' 3389 '100.101.2.3' 'Tailscale') }
Check 'OK: RDP desde un equipo concreto /32' { Good @(Rule 'Admin RDP host' 3389 '192.168.1.5/32') }
Check 'OK: reglas UDP (descubrimiento, DNS) no son administracion TCP' { Good @(Rule 'mDNS' 5353 'LocalSubnet' '' 'UDP') }
Check 'OK: sin reglas' { Good @() }
# --- la LAN NO puede administrar --------------------------------------------------------------------------------------------------------------
Check 'FALLA: RDP 3389 desde LocalSubnet' { Bad @(Rule 'RDP' 3389 'LocalSubnet') }
Check 'FALLA: RDP 3389 desde Any' { Bad @(Rule 'RDP' 3389 'Any') }
Check 'FALLA: RDP 3389 desde la red LAN 192.168.1.0/24' { Bad @(Rule 'RDP' 3389 '192.168.1.0/24') }
Check 'FALLA: RDP con mascara larga (255.255.255.0)' { Bad @(Rule 'RDP' 3389 '192.168.1.0/255.255.255.0') }
Check 'FALLA: SSH 22 desde la LAN' { Bad @(Rule 'OpenSSH' 22 'LocalSubnet') }
Check 'FALLA: SMB 445 desde la LAN' { Bad @(Rule 'SMB' 445 'LocalSubnet') }
Check 'FALLA: WinRM 5985 desde Any' { Bad @(Rule 'WinRM' 5985 'Any') }
Check 'FALLA: rango de puertos que incluye el 3389 desde la LAN' { Bad @(Rule 'rango' '3000-4000' 'LocalSubnet') }
Check 'FALLA: la tailnet SIN atar al adaptador (suplantacion 100.x desde la LAN)' { Bad @(Rule 'RDP' 3389 '100.64.0.0/10') }
Check 'FALLA: la red de Tailscale muy amplia (100.0.0.0/8)' { Bad @(Rule 'RDP' 3389 '100.0.0.0/8' 'Tailscale') }
Check 'FALLA: SQL 1433 abierto aunque sea a un equipo' { Bad @(Rule 'SQL' 1433 '192.168.1.5') }
Check 'FALLA: Kestrel 8080 abierto desde la tailnet' { Bad @(Rule 'Kestrel' 8080 '100.64.0.0/10' 'Tailscale') }
Check 'FALLA: todos los puertos, cualquier programa, desde la LAN' { Bad @(Rule 'todo' 'Any' 'LocalSubnet') }
Check 'OK: todos los puertos de un programa concreto (p. ej. un navegador) no cuenta' { Good @(Rule 'chrome' 'Any' 'LocalSubnet' '' 'TCP' 'C:\Program Files\Google\Chrome\chrome.exe') }
Check 'FALLA: una regla mala entre varias buenas' { Bad @((Rule 'web' 443 'LocalSubnet'), (Rule 'RDP' 3389 'LocalSubnet'), (Rule 'Admin' 3389 '100.101.2.3' 'Tailscale')) }
Check 'FALLA: una regla con dos origenes, uno de ellos la LAN' { Bad @(Rule 'RDP' 3389 @('100.101.2.3', '192.168.1.0/24') 'Tailscale') }
Check 'FALLA: puerto con nombre (RPC Endpoint Mapper = 135) desde la LAN' { Bad @(Rule 'RPC' 'RPC Endpoint Mapper' 'LocalSubnet') }
Check 'FALLA: sin origen remoto (equivale a cualquiera)' { Bad @([pscustomobject]@{ Name = 'RDP'; Protocol = 'TCP'; LocalPort = @(3389); RemoteAddress = @(); InterfaceAlias = ''; Program = 'Any' }) }
Check 'FALLA: IP de la tailnet /32 SIN atar al adaptador Tailscale (suplantacion)' { Bad @(Rule 'RDP' 3389 '100.101.2.3') }
Check 'FALLA: equipo /32 publico (8.8.8.8)' { Bad @(Rule 'RDP' 3389 '8.8.8.8') }
Check 'FALLA: la regla predefinida de Escritorio remoto (Any) que deja habilitar RDP' { Bad @(Rule 'Escritorio remoto - modo usuario (TCP de entrada)' 3389 'Any' '' 'TCP' 'System') }
$msg = (Get-AccessModelFindings @(Rule 'RDP' 3389 '192.168.1.0/24')) -join ' '
Check 'el informe nombra la red LAN completa' { $msg -match '192\.168\.1\.0/24' -and $msg -match 'RDP' }

# --- Enable-ErpRemoteAdmin: la especificacion de la regla ----------------------------------------------------------------------------------
$spec = New-ErpAdminRuleSpec '100.101.2.3'
Check 'regla de admin: entrada, TCP, 3389, solo la IP del administrador, solo adaptador Tailscale' { $spec.Direction -eq 'Inbound' -and $spec.Protocol -eq 'TCP' -and $spec.LocalPort -eq 3389 -and $spec.RemoteAddress -eq '100.101.2.3' -and $spec.InterfaceAlias -eq 'Tailscale' }
Check 'una regla por administrador: el nombre incluye la IP (revocacion individual)' { (New-ErpAdminRuleSpec '100.101.2.3').DisplayName -ne (New-ErpAdminRuleSpec '100.101.2.4').DisplayName -and $spec.DisplayName -match '100\.101\.2\.3$' }
Check 'la regla que crea pasa la auditoria' { Good @(Rule $spec.DisplayName $spec.LocalPort $spec.RemoteAddress $spec.InterfaceAlias) }
foreach ($bad in @('192.168.1.5', '100.64.0.0/10', '100.0.0.1', '100.128.0.1', '10.0.0.1', 'LocalSubnet', 'Any', '100.101.2', '100.101.2.256', '100.101.2.3; evil', '')) {
    Check "rechaza AdminTailscaleIp '$bad'" { try { $null = New-ErpAdminRuleSpec $bad; $false } catch { $true } }.GetNewClosure()
}
Check 'acepta los limites de la tailnet (100.64.0.0 y 100.127.255.255)' { (Test-TailnetHostIp '100.64.0.0') -and (Test-TailnetHostIp '100.127.255.255') }
# Sin -Apply no se abre nada: se ejecuta el script como proceso hijo y se verifica la salida y el codigo.
$exe = if (Get-Command powershell -ErrorAction SilentlyContinue) { 'powershell' } else { 'pwsh' }
$enable = Join-Path $here 'Enable-ErpRemoteAdmin.ps1'
$o = & $exe -NoProfile -File $enable -AdminTailscaleIp 100.101.2.3 2>&1 | Out-String; $c = $LASTEXITCODE
Check 'sin -Apply solo muestra (no abre ningun puerto)' { $c -eq 0 -and $o -match 'solo muestra' -and $o -match 'No se abrio ningun puerto' }
$o = & $exe -NoProfile -File $enable -AdminTailscaleIp 192.168.1.5 2>&1 | Out-String; $c = $LASTEXITCODE
Check 'una IP de la LAN como administrador se rechaza' { $c -ne 0 -and $o -match 'IP individual de Tailscale' }
$o = & $exe -NoProfile -File $enable -AdminTailscaleIp 100.101.2.3 -Remove 2>&1 | Out-String; $c = $LASTEXITCODE
Check 'sin -Apply, -Remove tambien solo muestra' { $c -eq 0 -and $o -match 'solo muestra' }
# La auditoria como proceso (codigos de salida) con un archivo de reglas
$audit = Join-Path $here 'Test-ErpRemoteAccess.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("erpacc-" + [guid]::NewGuid().ToString('N') + '.json')
try {
    ConvertTo-Json @((Rule 'web' 443 'LocalSubnet'), (Rule 'Admin RDP' 3389 '100.101.2.3' 'Tailscale')) -Depth 4 | Set-Content -LiteralPath $tmp -Encoding utf8
    & $exe -NoProfile -File $audit -RulesFile $tmp | Out-Null; $c = $LASTEXITCODE
    Check 'proceso: reglas correctas -> exit 0' { $c -eq 0 }
    ConvertTo-Json @((Rule 'RDP' 3389 'LocalSubnet')) -Depth 4 | Set-Content -LiteralPath $tmp -Encoding utf8
    & $exe -NoProfile -File $audit -RulesFile $tmp | Out-Null; $c = $LASTEXITCODE
    Check 'proceso: RDP desde la LAN -> exit 1' { $c -eq 1 }
    & $exe -NoProfile -File $audit -RulesFile "$tmp.no-existe" | Out-Null; $c = $LASTEXITCODE
    Check 'proceso: archivo inexistente -> exit 2' { $c -eq 2 }
} finally { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue }

Write-Host ''; Write-Host "remote access model tests: $($script:pass) PASS, $($script:fail) FAIL"
exit $(if ($script:fail) { 1 } else { 0 })
