<#
.SYNOPSIS
  Windows 11 Pro (Docker Desktop/WSL2, Tailscale instalado en el propio Windows): habilita (o retira) la ADMINISTRACION REMOTA del servidor
  por Escritorio remoto (RDP 3389) SOLO para el administrador remoto de la tailnet. NO abre nada si no se pasa -Apply.

.DESCRIPTION
  Modelo de acceso (docs/p05-permisos-acceso-remoto.md):
    - Usuarios de la LAN y usuario remoto: solo la web (443). install-windows.ps1 crea esas reglas y NINGUNA de administracion.
    - Administrador remoto: web + administracion. La politica de Tailscale (deploy/tailscale/policy.windows.hujson) le concede tcp:3389;
      esta regla del firewall de Windows es la segunda capa y debe coincidir: sin ella el puerto sigue cerrado aunque la politica lo permita.

  La regla que crea es la mas estrecha posible:
    - Entrada, TCP, puerto local 3389, accion Allow;
    - SOLO desde la IP de Tailscale del administrador (/32, dentro de 100.64.0.0/10);
    - SOLO si el paquete entra por el adaptador 'Tailscale' (una IP 100.x suplantada desde la LAN no coincide);
    - nunca LocalSubnet, Any ni redes LAN.
  Sin -Apply solo muestra lo que haria (modo por defecto). -Remove retira la regla (baja del administrador o cambio de equipo).
  Antes de -Apply hace falta: Tailscale instalado y conectado (adaptador 'Tailscale'), el Escritorio remoto de Windows habilitado a proposito
  (Configuracion > Sistema > Escritorio remoto; NO se habilita desde aqui).
  AVISO: al habilitar el Escritorio remoto Windows activa sus reglas predefinidas, que aceptan CUALQUIER origen (la LAN incluida). -Apply las deshabilita
  (RemoteDesktop-UserMode-In-TCP/UDP, RemoteDesktop-Shadow-In-TCP): despues solo queda la regla estrecha de este script. Una regla por administrador/dispositivo y la regla aplicada en la politica de la tailnet.
  Windows 11 Pro permite UNA sesion de RDP a la vez y no es un servidor de terminales; para Windows Server + Hyper-V la administracion es SSH dentro
  de la VM (server-linux-bootstrap.sh --admin-from), no este script.

.PARAMETER AdminTailscaleIp
  IP de Tailscale (100.x.y.z) del dispositivo del administrador remoto (consola de Tailscale > Machines). Una IP por dispositivo autorizado.

.EXAMPLE
  .\Enable-ErpRemoteAdmin.ps1 -AdminTailscaleIp 100.101.2.3              # solo muestra
  .\Enable-ErpRemoteAdmin.ps1 -AdminTailscaleIp 100.101.2.3 -Apply       # crea la regla (administrador)
  .\Enable-ErpRemoteAdmin.ps1 -AdminTailscaleIp 100.101.2.3 -Remove -Apply   # la retira
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AdminTailscaleIp,
    [switch]$Apply,
    [switch]$Remove
)
# -Remove retira SOLO la regla de la IP indicada (revocacion individual); con varios administradores no se toca a los demas.
$ErrorActionPreference = 'Stop'
$RuleBase = 'TheBury ERP Admin RDP (Tailscale)'   # una regla POR administrador/dispositivo: '<base> <IP>' (alta y baja individuales)
# Reglas predefinidas de Windows que el Escritorio remoto activa desde cualquier origen (nombres internos, no dependen del idioma). Se deshabilitan al aplicar.
$PredefinedRdp = @('RemoteDesktop-UserMode-In-TCP', 'RemoteDesktop-UserMode-In-UDP', 'RemoteDesktop-Shadow-In-TCP')

# IPv4 individual dentro de 100.64.0.0/10 (rango de Tailscale). Nunca un rango ni una IP de la LAN.
function Test-TailnetHostIp([string]$Ip) {
    if ($Ip -notmatch '^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$') { return $false }
    foreach ($i in 1..4) { if ([int]$Matches[$i] -gt 255 -or ($Matches[$i].Length -gt 1 -and $Matches[$i].StartsWith('0'))) { return $false } }
    return ([int]$Matches[1] -eq 100 -and [int]$Matches[2] -ge 64 -and [int]$Matches[2] -le 127)
}

# Especificacion de la regla (pura: se prueba sin tocar el firewall).
function New-ErpAdminRuleSpec([string]$Ip) {
    if (-not (Test-TailnetHostIp $Ip)) { throw "AdminTailscaleIp debe ser una IP individual de Tailscale (100.64.0.0 - 100.127.255.255), no '$Ip'" }
    return [ordered]@{
        DisplayName = "$RuleBase $Ip"; Direction = 'Inbound'; Action = 'Allow'; Protocol = 'TCP'; LocalPort = 3389
        RemoteAddress = $Ip; InterfaceAlias = 'Tailscale'; Profile = 'Any'
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

$spec = New-ErpAdminRuleSpec $AdminTailscaleIp
$RuleName = $spec.DisplayName
if ($Remove) {
    Write-Host "Retirar la regla '$RuleName'" -ForegroundColor Cyan
    if (-not $Apply) { Write-Host "[solo muestra] Remove-NetFirewallRule -DisplayName '$RuleName'  (agregar -Apply para ejecutarlo)"; return }
    Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Write-Host "Regla de $AdminTailscaleIp retirada: ese dispositivo ya no puede conectarse por RDP (las reglas de otros administradores no se tocan)." -ForegroundColor Green
    return
}
Write-Host "Regla de administracion remota (RDP) para el administrador $AdminTailscaleIp" -ForegroundColor Cyan
$spec.GetEnumerator() | ForEach-Object { Write-Host ('  {0,-15} {1}' -f $_.Key, $_.Value) }
if (-not $Apply) {
    Write-Host '[solo muestra] No se abrio ningun puerto. Agregar -Apply para crear la regla (requiere administrador, Tailscale conectado y el Escritorio remoto habilitado).' -ForegroundColor Yellow
    return
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecutar como administrador para -Apply' }
if (-not (Get-NetAdapter -Name 'Tailscale' -ErrorAction SilentlyContinue)) { throw "No existe el adaptador 'Tailscale': instalar y conectar Tailscale antes. No se modifico el firewall." }
Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
# Habilitar el Escritorio remoto en Windows activa sus reglas predefinidas, que aceptan cualquier origen (LAN incluida): se deshabilitan; solo queda la regla estrecha.
foreach ($n in $PredefinedRdp) { Get-NetFirewallRule -Name $n -ErrorAction SilentlyContinue | Where-Object { $_.Enabled -eq 'True' } | ForEach-Object { Write-Host "  deshabilitando regla predefinida '$($_.DisplayName)' (aceptaba cualquier origen)" -ForegroundColor Yellow; $_ | Disable-NetFirewallRule } }
New-NetFirewallRule -DisplayName $spec.DisplayName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3389 -RemoteAddress $spec.RemoteAddress -InterfaceAlias 'Tailscale' -Profile Any | Out-Null
Write-Host "Regla creada. Verificar: .\Test-ErpRemoteAccess.ps1 (la LAN no debe poder administrar) y probar RDP desde el equipo del administrador." -ForegroundColor Green
