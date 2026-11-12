<#
.SYNOPSIS
  Audita las reglas ENTRANTES del Firewall de Windows (servidor Windows 11 Pro): la LAN solo debe llegar a la web (443); la administracion
  (RDP 3389, SSH 22, SMB 445, WinRM, etc.) solo desde la red de Tailscale por el adaptador 'Tailscale' o desde equipos concretos (/32).
  SOLO LECTURA: no modifica el firewall.

.DESCRIPTION
  Sin parametros lee el firewall real (Get-NetFirewallRule, solo reglas Allow, entrantes y habilitadas).
  Con -RulesFile lee un JSON con reglas normalizadas (pruebas): [{ "Name": "...", "Protocol": "TCP", "LocalPort": ["443"],
  "RemoteAddress": ["LocalSubnet"], "InterfaceAlias": "", "Program": "Any" }, ...].
  Falla si una regla Allow entrante habilita puertos de administracion/SQL/Kestrel, o TODOS los puertos sin restringir programa, desde
  LocalSubnet/Any/una red completa, o desde Tailscale sin atar al adaptador 'Tailscale'.
  Exit 0 = cumple; 1 = incumple; 2 = no se pudo leer.
#>
[CmdletBinding()]
param([string]$RulesFile = '')
$ErrorActionPreference = 'Stop'
$Mgmt = @(22, 23, 135, 445, 3389, 5985, 5986)
$Never = @(1433, 8080)

function ConvertTo-IpUInt([string]$Ip) {
    $b = [Net.IPAddress]::Parse($Ip).GetAddressBytes(); [Array]::Reverse($b); return [BitConverter]::ToUInt32($b, 0)
}
# Origen remoto de una regla -> @{ Ok; Why }. Aceptable: IP /32 individual, o dentro de 100.64.0.0/10 SOLO si la regla esta atada al adaptador 'Tailscale'.
function Test-RemoteSource([string]$Remote, [string]$Iface) {
    $r = $Remote.Trim()
    if ($r -in @('Any', '*', '')) { return @{ Ok = $false; Why = 'desde cualquier origen (Any)' } }
    if ($r -match '^(LocalSubnet|Intranet|DefaultGateway|DHCP|DNS|WINS|Internet|PlayToDevice|LocalSubnet6)$') { return @{ Ok = $false; Why = "desde '$r' (incluye la LAN)" } }
    if ($r -notmatch '^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?:/(\d{1,2})|/(\d+\.\d+\.\d+\.\d+))?$') { return @{ Ok = $false; Why = "origen no interpretable: '$r'" } }
    $ip = ($r -split '/')[0]
    $len = 32
    if ($Matches[5]) { $len = [int]$Matches[5] }
    elseif ($Matches[6]) { $mask = ConvertTo-IpUInt $Matches[6]; $len = 0; while ($len -lt 32 -and (($mask -shr (31 - $len)) -band 1)) { $len++ } }
    $o = [int]($ip -split '\.')[1]
    $inTailnetAny = (($ip -split '\.')[0] -eq '100' -and $o -ge 64 -and $o -le 127)
    if ($len -eq 32 -and -not $inTailnetAny) {   # equipo concreto fuera de la tailnet: debe ser privado (RFC 1918), nunca publico
        $f = [int]($ip -split '.', 0, 'SimpleMatch')[0]
        if ($f -eq 10 -or ($f -eq 172 -and $o -ge 16 -and $o -le 31) -or ($f -eq 192 -and $o -eq 168)) { return @{ Ok = $true; Why = '' } }
        return @{ Ok = $false; Why = "equipo $ip que no es privado ni de la tailnet (la administracion no se abre a Internet)" }
    }
    if ($inTailnetAny -and $len -ge 10) {
        if ($Iface -eq 'Tailscale') { return @{ Ok = $true; Why = '' } }
        return @{ Ok = $false; Why = "red de Tailscale sin atar al adaptador 'Tailscale' (un equipo de la LAN podria suplantar un origen 100.x)" }
    }
    return @{ Ok = $false; Why = "red completa $r (la LAN solo debe tener HTTPS)" }
}

function Expand-Ports($ports) {
    $out = New-Object System.Collections.Generic.List[int]; $all = $false; $named = $false
    foreach ($p in @($ports)) {
        $s = "$p".Trim()
        if ($s -in @('Any', '*', '')) { $all = $true; continue }
        if ($s -match '^(\d+)-(\d+)$') { foreach ($i in [int]$Matches[1]..[int]$Matches[2]) { $out.Add($i) } }
        elseif ($s -match '^\d+$') { $out.Add([int]$s) }
        else { $named = $true }   # puerto con nombre ('RPC Endpoint Mapper', 'RPCEPMap'...): no se puede descartar que sea administracion
    }
    return @{ All = $all; Ports = $out; Named = $named }
}

function Get-AccessModelFindings($Rules) {
    $f = New-Object System.Collections.Generic.List[string]
    foreach ($r in $Rules) {
        $proto = "$($r.Protocol)"
        if ($proto -and $proto -notin @('TCP', 'Any', '6', '256')) { continue }   # UDP/ICMP: no son puertos de administracion TCP
        $pp = Expand-Ports $r.LocalPort
        $hitMgmt = @($pp.Ports | Where-Object { $Mgmt -contains $_ })
        $hitNever = @($pp.Ports | Where-Object { $Never -contains $_ })
        $program = "$($r.Program)"
        $allPorts = $pp.All -and ($program -in @('', 'Any', '*'))
        if (-not ($hitMgmt.Count -or $hitNever.Count -or $allPorts -or $pp.Named)) { continue }
        $remotes = @($r.RemoteAddress); if (-not $remotes.Count) { $remotes = @('') }   # sin origen = cualquiera
        foreach ($remote in $remotes) {
            if ($hitNever.Count) { $f.Add("regla '$($r.Name)' abre $($hitNever -join ',') (SQL/Kestrel no deben abrirse en el host)"); break }
            $src = Test-RemoteSource "$remote" "$($r.InterfaceAlias)"
            if (-not $src.Ok) {
                $what = if ($hitMgmt.Count) { "administracion ($($hitMgmt -join ','))" } elseif ($pp.Named -and -not $allPorts) { 'puerto con nombre no reconocido (se asume administracion)' } else { 'TODOS los puertos sin restringir programa' }
                $f.Add("regla '$($r.Name)': $what $($src.Why)")
            }
        }
    }
    return $f
}

function Read-LiveRules {
    $out = @()
    foreach ($rule in (Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True)) {
        $pf = $rule | Get-NetFirewallPortFilter; $af = $rule | Get-NetFirewallAddressFilter; $ifa = $rule | Get-NetFirewallInterfaceFilter; $app = $rule | Get-NetFirewallApplicationFilter
        $out += [pscustomobject]@{ Name = $rule.DisplayName; Protocol = "$($pf.Protocol)"; LocalPort = @($pf.LocalPort); RemoteAddress = @($af.RemoteAddress)
            InterfaceAlias = "$($ifa.InterfaceAlias)"; Program = "$($app.Program)" }
    }
    return $out
}

if ($MyInvocation.InvocationName -eq '.') { return }

try { $rules = if ($RulesFile) { Get-Content -LiteralPath $RulesFile -Raw | ConvertFrom-Json } else { Read-LiveRules } }
catch { Write-Host "ERROR leyendo las reglas: $($_.Exception.Message)" -ForegroundColor Red; exit 2 }
$findings = @(Get-AccessModelFindings $rules)
if ($findings.Count) { $findings | ForEach-Object { Write-Host "FALLA $_" -ForegroundColor Red }; Write-Host "$($findings.Count) incumplimiento(s) del modelo de acceso"; exit 1 }
Write-Host 'OK modelo de acceso: la administracion solo desde la tailnet (adaptador Tailscale) o equipos concretos; la LAN no administra' -ForegroundColor Green
exit 0
