<#
.SYNOPSIS
  Pruebas de Install-ErpCa.ps1 (confianza de la CA en clientes). Genera certificados SINTETICOS y usa solo -VerifyOnly:
  NUNCA modifica ningun almacen de certificados. Funciona en PowerShell 7 (Linux/Windows) y Windows PowerShell 5.1.
    pwsh -NoProfile -File scripts/install/Test-InstallErpCa.ps1     (exit 0 = todo OK)
#>
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Install-ErpCa.ps1'
$exe = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("erpca-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$script:pass = 0; $script:fail = 0
function Check([string]$name, [bool]$cond) { if ($cond) { $script:pass++; Write-Host "PASS $name" } else { $script:fail++; Write-Host "FAIL $name" } }

Add-Type -AssemblyName System.Security
$HA = [System.Security.Cryptography.HashAlgorithmName]::SHA256
$Pad = [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
function New-Cert([string]$subject, [bool]$isCa, [datetime]$nb, [datetime]$na, $issuerCert = $null) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$subject", $rsa, $HA, $Pad)
    $req.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($isCa, $false, 0, $true))
    if ($issuerCert) {
        $serial = [byte[]](1..8 | ForEach-Object { Get-Random -Maximum 255 }); $serial[0] = 1
        return $req.Create($issuerCert, $nb, $na, $serial)
    }
    return $req.CreateSelfSigned($nb, $na)
}
function Save-Pem($cert, [string]$path, [string]$extra = '') {
    $b64 = [Convert]::ToBase64String($cert.RawData, 'InsertLineBreaks')
    "-----BEGIN CERTIFICATE-----`n$b64`n-----END CERTIFICATE-----`n$extra" | Set-Content -LiteralPath $path -Encoding ascii -NoNewline
}
function Fp($cert) { $cert.GetCertHashString('SHA256') }
function Run([string[]]$argv) { $o = & $exe -NoProfile -File $script @argv 2>&1 | Out-String; [pscustomobject]@{ Code = $LASTEXITCODE; Out = $o } }

try {
    $now = Get-Date
    $root = New-Cert 'Caddy Local Authority - TEST' $true $now.AddDays(-1) $now.AddYears(10)
    $rootPem = Join-Path $tmp 'root.crt'; Save-Pem $root $rootPem
    $fp = Fp $root

    $r = Run @('-CertPath', $rootPem, '-Sha256', $fp, '-VerifyOnly')
    Check 'CA raiz valida con la huella correcta: aceptada (-VerifyOnly)' ($r.Code -eq 0 -and $r.Out -match 'no se modifico ningun almacen')
    $r = Run @('-CertPath', $rootPem, '-Sha256', ($fp -replace '(..)(?!$)', '$1:'), '-VerifyOnly')
    Check 'huella con formato AA:BB:... tambien se acepta' ($r.Code -eq 0)
    $r = Run @('-CertPath', $rootPem, '-Sha256', ($fp.ToLower()), '-VerifyOnly')
    Check 'huella en minusculas se acepta' ($r.Code -eq 0)

    $other = ('0' * 64)
    $r = Run @('-CertPath', $rootPem, '-Sha256', $other, '-VerifyOnly')
    Check 'huella que NO coincide: rechazada' ($r.Code -ne 0 -and $r.Out -match 'NO coincide')
    $r = Run @('-CertPath', $rootPem, '-Sha256', 'abc', '-VerifyOnly')
    Check 'huella con longitud invalida: rechazada' ($r.Code -ne 0)

    $leaf = New-Cert 'tbp' $false $now.AddDays(-1) $now.AddDays(30) $root
    $leafPem = Join-Path $tmp 'leaf.crt'; Save-Pem $leaf $leafPem
    $r = Run @('-CertPath', $leafPem, '-Sha256', (Fp $leaf), '-VerifyOnly')
    Check 'certificado de hoja (no CA), aunque la huella coincida: rechazado' ($r.Code -ne 0 -and $r.Out -match 'no es una CA')

    $inter = New-Cert 'Intermedia - TEST' $true $now.AddDays(-1) $now.AddYears(1) $root
    $interPem = Join-Path $tmp 'inter.crt'; Save-Pem $inter $interPem
    $r = Run @('-CertPath', $interPem, '-Sha256', (Fp $inter), '-VerifyOnly')
    Check 'CA intermedia (no autofirmada): rechazada' ($r.Code -ne 0 -and $r.Out -match 'RAIZ')

    $old = New-Cert 'Vencida - TEST' $true $now.AddYears(-3) $now.AddYears(-1)
    $oldPem = Join-Path $tmp 'old.crt'; Save-Pem $old $oldPem
    $r = Run @('-CertPath', $oldPem, '-Sha256', (Fp $old), '-VerifyOnly')
    Check 'CA vencida: rechazada' ($r.Code -ne 0 -and $r.Out -match 'no esta vigente')

    $keyPem = Join-Path $tmp 'withkey.crt'; Save-Pem $root $keyPem "-----BEGIN PRIVATE KEY-----`nMIIBVgIBADANBgkqhkiG9w0BAQEFAASCAUAwggE8AgEAAkEA`n-----END PRIVATE KEY-----`n"
    $r = Run @('-CertPath', $keyPem, '-Sha256', $fp, '-VerifyOnly')
    Check 'archivo con CLAVE PRIVADA: rechazado' ($r.Code -ne 0 -and $r.Out -match 'CLAVE PRIVADA')

    $twoPem = Join-Path $tmp 'two.crt'; (Get-Content -LiteralPath $rootPem -Raw) + (Get-Content -LiteralPath $leafPem -Raw) | Set-Content -LiteralPath $twoPem -Encoding ascii -NoNewline
    $r = Run @('-CertPath', $twoPem, '-Sha256', $fp, '-VerifyOnly')
    Check 'archivo con dos certificados: rechazado' ($r.Code -ne 0 -and $r.Out -match 'UN certificado')

    $junk = Join-Path $tmp 'junk.crt'; "-----BEGIN CERTIFICATE-----`nAAAA`n-----END CERTIFICATE-----`n" | Set-Content -LiteralPath $junk -Encoding ascii
    $r = Run @('-CertPath', $junk, '-Sha256', $fp, '-VerifyOnly')
    Check 'basura con forma de PEM: rechazada' ($r.Code -ne 0)
    $r = Run @('-CertPath', (Join-Path $tmp 'no-existe.crt'), '-Sha256', $fp, '-VerifyOnly')
    Check 'archivo inexistente: rechazado' ($r.Code -ne 0 -and $r.Out -match 'no existe')
    $r = Run @('-Sha256', $fp)
    Check 'sin -CertPath (y sin -Remove): rechazado' ($r.Code -ne 0)
    $r = Run @('-Remove', '-Sha256', $fp)
    Check '-Remove de una CA que no esta instalada: no hace nada y termina bien' ($r.Code -eq 0 -and $r.Out -match 'nada que retirar')
}
finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "Install-ErpCa tests: $($script:pass) PASS, $($script:fail) FAIL"
exit ([int]($script:fail -ne 0))
