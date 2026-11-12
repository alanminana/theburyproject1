<#
.SYNOPSIS
  Instala (o retira) en un equipo CLIENTE la CA raiz interna de Caddy para que el navegador confie en https://<ERP_DOMAIN>.

.DESCRIPTION
  Antes de instalar valida el archivo: debe ser UN certificado X.509 de CA raiz (autofirmado, CA:TRUE, vigente), sin clave privada, y su
  huella SHA-256 debe coincidir con -Sha256, la huella que el administrador entrega POR OTRO CANAL (en persona, telefono o mensajeria
  distinta de la que trajo el archivo). Sin esa comprobacion, quien intercepte el archivo podria hacerte confiar en su propia CA.

  Por defecto instala en el almacen del USUARIO actual (no requiere administrador). -Scope LocalMachine instala para todo el equipo
  (requiere administrador). Firefox usa su propio almacen: ver docs/p05-tailscale-acceso-remoto.md.
  -Remove retira la CA (por huella) del almacen: es el paso de baja de un equipo cliente.
  -VerifyOnly solo valida y muestra la huella, sin tocar ningun almacen.

.EXAMPLE
  # Huella entregada por el administrador (ver `openssl x509 -in caddy-root-ca.crt -noout -fingerprint -sha256` en el servidor)
  .\Install-ErpCa.ps1 -CertPath .\caddy-root-ca.crt -Sha256 'AB:CD:...'
.EXAMPLE
  .\Install-ErpCa.ps1 -CertPath .\caddy-root-ca.crt -Sha256 'AB:CD:...' -VerifyOnly
.EXAMPLE
  .\Install-ErpCa.ps1 -Remove -Sha256 'AB:CD:...'
#>
[CmdletBinding()]
param(
    [string]$CertPath,
    [Parameter(Mandatory)][string]$Sha256,
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$Scope = 'CurrentUser',
    [switch]$VerifyOnly,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'

function Normalize-Fingerprint([string]$s) { ($s -replace '[^0-9A-Fa-f]', '').ToUpperInvariant() }
function Fail([string]$m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

$expected = Normalize-Fingerprint $Sha256
if ($expected.Length -ne 64) { Fail "-Sha256 debe ser una huella SHA-256 (64 hex, con o sin ':'); recibida de $($expected.Length) caracteres hex" }

$storePath = "Cert:\$Scope\Root"

if ($Remove) {
    if ($Scope -eq 'LocalMachine' -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Fail 'retirar del almacen LocalMachine requiere PowerShell como administrador'
    }
    $found = @(); try { $found = @(Get-ChildItem $storePath -ErrorAction Stop | Where-Object { (Normalize-Fingerprint $_.GetCertHashString('SHA256')) -eq $expected }) } catch { }  # almacen inexistente (p. ej. Linux sin CAs de usuario) = nada que retirar
    if ($found.Count -eq 0) { Write-Host "La CA con esa huella no esta en $storePath (nada que retirar)."; exit 0 }
    foreach ($c in $found) { Remove-Item -Path "$storePath\$($c.Thumbprint)" -Force; Write-Host "Retirada: $($c.Subject)" -ForegroundColor Green }
    exit 0
}

if (-not $CertPath) { Fail '-CertPath es obligatorio (salvo con -Remove)' }
if (-not (Test-Path -LiteralPath $CertPath -PathType Leaf)) { Fail "no existe el archivo: $CertPath" }
# Se lee el archivo UNA vez: lo que se valida es exactamente lo que se instala (sin releer por ruta: evita TOCTOU y certificados extra).
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $CertPath).ProviderPath)
$raw = [Text.Encoding]::ASCII.GetString($bytes)
if ($raw -match 'PRIVATE KEY') { Fail 'el archivo contiene una CLAVE PRIVADA. No se instala: solo se distribuye el certificado publico. Avisar al administrador.' }
$blocks = [regex]::Matches($raw, '-----BEGIN [A-Z0-9 ]*CERTIFICATE[A-Z0-9 ]*-----')
if ($blocks.Count -ne 1) { Fail "se espera UN certificado en el archivo, hay $($blocks.Count)" }
if ($raw -notmatch '(?s)^\s*-----BEGIN CERTIFICATE-----.*-----END CERTIFICATE-----\s*$') { Fail 'formato no admitido: se espera un unico bloque PEM (BEGIN/END CERTIFICATE) sin texto adicional' }

try { $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes) }
catch { Fail "no es un certificado X.509 valido: $($_.Exception.Message)" }

$actual = Normalize-Fingerprint $cert.GetCertHashString('SHA256')
if ($actual -ne $expected) { Fail "la huella SHA-256 NO coincide.`n  esperada: $expected`n  archivo : $actual`nNo se instala. Pedir al administrador un archivo y una huella nuevos." }

$bc = $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] } | Select-Object -First 1
if (-not $bc -or -not $bc.CertificateAuthority) { Fail 'el certificado no es una CA (basicConstraints CA:TRUE ausente)' }
if ($cert.Subject -ne $cert.Issuer) { Fail 'el certificado no es una CA RAIZ (emisor distinto del titular)' }
$now = Get-Date
if ($now -lt $cert.NotBefore -or $now -gt $cert.NotAfter) { Fail "el certificado no esta vigente ($($cert.NotBefore) - $($cert.NotAfter))" }
if ($cert.HasPrivateKey) { Fail 'el certificado trae clave privada asociada' }

Write-Host "Certificado valido: $($cert.Subject)"
Write-Host "  SHA-256 : $actual"
Write-Host "  vigencia: $($cert.NotBefore.ToString('yyyy-MM-dd')) -> $($cert.NotAfter.ToString('yyyy-MM-dd'))"
if ($VerifyOnly) { Write-Host 'Solo verificacion (-VerifyOnly): no se modifico ningun almacen.' -ForegroundColor Yellow; exit 0 }

if ($Scope -eq 'LocalMachine' -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Fail '-Scope LocalMachine requiere PowerShell como administrador (o usar el valor por defecto CurrentUser)'
}
# Se agrega el OBJETO ya validado (no se relee el archivo).
$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('Root', $Scope)
try { $store.Open('ReadWrite'); $store.Add($cert) } finally { $store.Close() }
Write-Host "CA instalada en $storePath. Reiniciar el navegador (Firefox: ver la guia)." -ForegroundColor Green
