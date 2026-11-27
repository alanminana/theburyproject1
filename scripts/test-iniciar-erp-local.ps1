<#
  Pruebas de scripts/iniciar-erp-local.ps1 en repositorios Git temporales, con un sqlcmd simulado. No necesita SQL Server,
  no toca el repositorio real ni inicia la aplicacion (usa -SoloVerificar).
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test-iniciar-erp-local.ps1      # exit 0 = todo OK
#>
$ErrorActionPreference = 'Continue'
$lanzador = Join-Path $PSScriptRoot 'iniciar-erp-local.ps1'
$tmp = Join-Path ([IO.Path]::GetTempPath()) "erp-lanzador-$([guid]::NewGuid().ToString('N'))"
$origen = Join-Path $tmp 'origen.git'; $repo = Join-Path $tmp 'repo'; $otro = Join-Path $tmp 'otro'; $bin = Join-Path $tmp 'bin'
New-Item -ItemType Directory -Force $tmp, $bin | Out-Null
$script:ok = 0; $script:mal = 0

function Invoke-Git { git @args 2>$null | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $args fallo" } }
function Set-Aplicadas([string[]]$Ids) { Set-Content -Path (Join-Path $bin 'aplicadas.txt') -Value $Ids -Encoding ascii }
function Test-Caso([string]$Nombre, [int]$Codigo, [string]$Texto, [string[]]$Extra = @('-Base', 'base_prueba')) {
    $puertoLibre = 49000 + (Get-Random -Maximum 900)
    $salida = powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts\iniciar-erp-local.ps1') -SoloVerificar -Puerto $puertoLibre @Extra 2>&1 | Out-String
    if ($LASTEXITCODE -eq $Codigo -and $salida -match $Texto) { $script:ok++; Write-Host "PASS $Nombre" }
    else { $script:mal++; Write-Host "FAIL $Nombre (exit $LASTEXITCODE)`n$salida" }
}

$pathPrevio = $env:PATH
try {
    # sqlcmd simulado: devuelve las migraciones "aplicadas" del archivo; falla si la base se llama base_rota.
    Set-Content -Path (Join-Path $bin 'sqlcmd.cmd') -Encoding ascii -Value @(
        '@echo off', 'echo %* | findstr /C:"base_rota" >nul && exit /b 1', 'type "%~dp0aplicadas.txt"', 'exit /b 0')
    $env:PATH = "$bin;$env:PATH"

    Invoke-Git init --quiet --bare --initial-branch=main $origen
    Invoke-Git clone --quiet $origen $repo
    Set-Location $repo
    Invoke-Git config user.email 'prueba@ejemplo.invalid'; Invoke-Git config user.name 'prueba'; Invoke-Git config core.autocrlf false
    New-Item -ItemType Directory -Force scripts, Migrations | Out-Null
    Copy-Item $lanzador scripts\
    Set-Content TheBuryProyect.csproj '<Project />' -Encoding ascii
    Set-Content Migrations\20260101000000_Uno.cs '// 1' -Encoding ascii
    Set-Content Migrations\20260101000000_Uno.Designer.cs '// d' -Encoding ascii
    Set-Content Migrations\20260102000000_Dos.cs '// 2' -Encoding ascii
    Invoke-Git add .; Invoke-Git commit --quiet -m inicial; Invoke-Git push --quiet origin HEAD:main; Invoke-Git branch --quiet -M main; Invoke-Git branch --quiet -u origin/main

    Set-Aplicadas '20260101000000_Uno', '20260102000000_Dos'
    Test-Caso 'al dia: verifica OK' 0 'VERIFICACION OK'

    Set-Aplicadas '20260101000000_Uno'
    Test-Caso 'migracion pendiente: se niega' 1 'faltan 1 migraciones'
    Set-Aplicadas '20260101000000_Uno', '20260102000000_Dos', '20260103000000_Futura'
    Test-Caso 'base mas nueva que el codigo: se niega' 1 'codigo mas viejo que la base'
    Set-Aplicadas '20260101000000_Uno', '20260102000000_Dos'
    Test-Caso 'base ilegible: se niega' 1 'no se pudo leer el historial' @('-Base', 'base_rota')
    Test-Caso 'nombre de base invalido: se niega' 1 'nombre de base invalido' @('-Base', 'x;DROP')
    Test-Caso 'servidor remoto: se niega' 1 'no es local' @('-Base', 'base_prueba', '-Servidor', 'sql.ejemplo.invalid')

    Set-Content nota.txt 'x' -Encoding ascii
    New-Item -ItemType Directory -Force .agents | Out-Null; Set-Content .agents\herramienta.js 'x' -Encoding ascii
    Test-Caso 'notas y herramientas de agentes sin versionar: no bloquean' 0 'VERIFICACION OK'
    Set-Content Nuevo.cs 'x' -Encoding ascii
    Test-Caso 'codigo sin versionar: se niega' 1 'cambios locales'
    Remove-Item Nuevo.cs
    Set-Content TheBuryProyect.csproj '<Project></Project>' -Encoding ascii
    Test-Caso 'archivo versionado modificado: se niega' 1 'cambios locales'
    Invoke-Git checkout --quiet -- TheBuryProyect.csproj

    Invoke-Git switch --quiet -c otra
    Test-Caso 'rama distinta de main: se niega' 1 "no en main"
    Invoke-Git switch --quiet main

    Invoke-Git clone --quiet $origen $otro
    Set-Content (Join-Path $otro 'LEEME.md') 'nuevo' -Encoding ascii
    Invoke-Git -C $otro add LEEME.md; Invoke-Git -C $otro -c user.email=p@ejemplo.invalid -c user.name=p commit --quiet -m nuevo; Invoke-Git -C $otro push --quiet origin HEAD:main
    Test-Caso 'detras de origin/main: se niega' 1 '1 commits detras'
    Test-Caso 'con -Actualizar avanza por fast-forward' 0 'VERIFICACION OK' @('-Base', 'base_prueba', '-Actualizar')

    Set-Content local.md 'solo local' -Encoding ascii; Invoke-Git add local.md; Invoke-Git commit --quiet -m local
    Test-Caso 'commit local sin publicar: se niega' 1 '1 delante'

    Invoke-Git remote set-url origin (Join-Path $tmp 'no-existe.git')
    Test-Caso 'origin inaccesible: se niega' 1 'no se pudo consultar origin'
}
finally {
    $env:PATH = $pathPrevio
    Set-Location $PSScriptRoot
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "`nlanzador local: $($script:ok) PASS, $($script:mal) FAIL"
if ($script:mal -gt 0 -or $script:ok -eq 0) { exit 1 }
exit 0
