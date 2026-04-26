# Auditoría Unlighthouse — TheBuryProject ERP
# Uso: .\run-audit.ps1
#
# Requiere .env.unlighthouse con:
#   UNLIGHTHOUSE_USER=<email>
#   UNLIGHTHOUSE_PASS=<contraseña>

$envFile = ".env.unlighthouse"

if (-not (Test-Path $envFile)) {
    Write-Error "No encontré $envFile`nCopiá .env.unlighthouse.example a .env.unlighthouse y completalo."
    exit 1
}

# Cargar variables del .env
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+)=(.+)$') {
        [System.Environment]::SetEnvironmentVariable($matches[1].Trim(), $matches[2].Trim(), 'Process')
    }
}

$user = $env:UNLIGHTHOUSE_USER
$pass = $env:UNLIGHTHOUSE_PASS

if (-not $user -or -not $pass) {
    Write-Error "UNLIGHTHOUSE_USER o UNLIGHTHOUSE_PASS vacíos en $envFile"
    exit 1
}

# Verificar que el servidor esté corriendo
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5187/" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    Write-Host "Servidor detectado (HTTP $($response.StatusCode))."
} catch {
    Write-Error "El servidor no responde en http://localhost:5187/`nArrancá la app primero: dotnet run"
    exit 1
}

Write-Host "Iniciando auditoría Unlighthouse..."
Write-Host "Usuario: $user"
Write-Host "Reportes en: .\.unlighthouse\"
Write-Host ""

npx unlighthouse --config unlighthouse.config.mjs
