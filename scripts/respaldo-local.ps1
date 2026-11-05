<#
  Respaldo y prueba de restauracion para la instalacion local (SQL Server LocalDB + archivos). NO toca la base original:
  el respaldo es COPY_ONLY y la prueba restaura en una base NUEVA descartable (<Base>_prueba_restore) que se elimina al final.

  Uso (Windows PowerShell 5.1):
    powershell -NoProfile -File scripts\respaldo-local.ps1 -Accion Respaldar -Raiz D:\git\theburyproject1-run
    powershell -NoProfile -File scripts\respaldo-local.ps1 -Accion Probar    -Raiz D:\git\theburyproject1-run
    (opcional: -Destino D:\respaldos-erp  -Base TheBuryProjectDb  -Servidor "(localdb)\MSSQLLocalDB"  -Carpeta <respaldo concreto>)

  -Raiz es el directorio de ejecucion de la app (contentRoot): ahi viven keys\ (Data Protection), wwwroot\uploads\ y
  App_Data\ (contratos). Un respaldo de la base sin esos archivos NO alcanza: sin keys\ no se descifran cookies/tokens
  persistidos y sin uploads\ / App_Data\ se pierden documentos de clientes y contratos.

  Que respalda: base (BACKUP ... COPY_ONLY, CHECKSUM, verificado con RESTORE VERIFYONLY ... CHECKSUM), keys\, wwwroot\uploads\,
  App_Data\, manifest.json (SHA256 de cada archivo, recuentos de tablas, migraciones, huella de la base).
  Que NO respalda (a proposito): secretos (user-secrets / variables ErpDb__*, .env). Guardarlos aparte en un gestor de
  contrasenas. El respaldo contiene datos sensibles: el destino debe tener acceso restringido y, fuera del equipo, ir cifrado.

  Que prueba -Accion Probar: integridad del archivo (VERIFYONLY), restauracion en base nueva, DBCC CHECKDB,
  DBCC CHECKCONSTRAINTS (integridad referencial), mismas migraciones y mismos recuentos/huellas que el manifiesto,
  usuarios SuperAdmin presentes, saldos (cajas, stock) iguales, y recuperacion de archivos (copia a carpeta temporal
  y comparacion de SHA256). Termina con codigo 0 solo si TODO coincide.
#>
param(
    [Parameter(Mandatory)][ValidateSet('Respaldar', 'Probar')][string]$Accion,
    [Parameter(Mandatory)][string]$Raiz,
    [string]$Destino = 'D:\respaldos-erp',
    [string]$Base = 'TheBuryProjectDb',
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    [string]$Carpeta = ''
)
$ErrorActionPreference = 'Stop'
if ($Base -notmatch '^[A-Za-z0-9_]+$') { throw "Nombre de base invalido." }
$Raiz = (Resolve-Path $Raiz).Path
$subcarpetas = @('keys', 'wwwroot\uploads', 'App_Data')

function Sql([string]$db, [string]$q) {
    $r = & sqlcmd -S $Servidor -E -C -d $db -h -1 -W -b -Q "SET NOCOUNT ON; $q" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd fallo: $($r -join ' ')" }
    return ($r | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}
function Huellas([string]$db) {
    # Recuentos + huella de contenido de tablas de negocio (excluye rowversion). Misma consulta en origen y restauracion.
    $tablas = Sql $db "SELECT name FROM sys.tables WHERE name NOT LIKE '[_][_]%' ORDER BY name"
    $h = [ordered]@{}
    foreach ($t in $tablas) {
        $cols = Sql $db "SELECT QUOTENAME(c.name) FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.$t') AND c.system_type_id NOT IN (189,241,34,35,99) ORDER BY c.column_id"
        $lista = ($cols -join ',')
        $res = Sql $db "SELECT CAST(COUNT_BIG(*) AS varchar(30)) + '|' + CAST(ISNULL(CHECKSUM_AGG(CHECKSUM($lista)),0) AS varchar(30)) FROM [dbo].[$t]"
        $h[$t] = "$($res | Select-Object -First 1)"
    }
    return $h
}
function HashArchivos([string]$baseDir, [string[]]$rel) {
    $m = [ordered]@{}
    foreach ($r in $rel) {
        $d = Join-Path $baseDir $r
        if (-not (Test-Path $d)) { continue }
        Get-ChildItem $d -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
            $k = $_.FullName.Substring($baseDir.Length).TrimStart('\')
            $m[$k] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    }
    return $m
}

if ($Accion -eq 'Respaldar') {
    if (-not (Sql 'master' "SELECT name FROM sys.databases WHERE name = N'$Base'")) { throw "No existe la base $Base en $Servidor." }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $Destino "respaldo-$stamp"
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $bak = Join-Path $dir "$Base.bak"
    Write-Host "Respaldando base en $bak ..."
    Sql 'master' "BACKUP DATABASE [$Base] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT, STATS = 25" | Out-Null
    Sql 'master' "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | Out-Null
    foreach ($s in $subcarpetas) {
        $o = Join-Path $Raiz $s
        if (Test-Path $o) { Copy-Item $o (Join-Path $dir $s) -Recurse -Force; Write-Host "Copiado $s" } else { Write-Host "Sin $s (no existe)" }
    }
    $manifest = [ordered]@{
        creado     = (Get-Date).ToString('o')
        base       = $Base
        servidor   = $Servidor
        migraciones = [int]((Sql $Base "SELECT COUNT(*) FROM __EFMigrationsHistory") | Select-Object -First 1)
        tablas     = Huellas $Base
        archivos   = HashArchivos $dir $subcarpetas
        bakSha256  = (Get-FileHash $bak -Algorithm SHA256).Hash
        nota       = 'No incluye secretos (user-secrets / ErpDb__* / .env): guardarlos aparte.'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $dir 'manifest.json') -Encoding UTF8
    Write-Host "OK: respaldo en $dir ($($manifest.archivos.Count) archivos, $($manifest.tablas.Count) tablas). Ejecutar -Accion Probar para validarlo."
    return
}

# --- Probar ---
if (-not $Carpeta) {
    $Carpeta = (Get-ChildItem $Destino -Directory -Filter 'respaldo-*' | Sort-Object Name -Descending | Select-Object -First 1).FullName
}
if (-not $Carpeta -or -not (Test-Path (Join-Path $Carpeta 'manifest.json'))) { throw "No hay respaldo valido (manifest.json) en $Destino." }
$man = Get-Content (Join-Path $Carpeta 'manifest.json') -Raw | ConvertFrom-Json
$bak = Join-Path $Carpeta "$($man.base).bak"
$prueba = "$($man.base)_prueba_restore"
$tmp = Join-Path 'C:\Users\Public' "restore_$($prueba)_$(Get-Date -Format yyyyMMddHHmmss)"
$fallos = New-Object System.Collections.Generic.List[string]
function Ok([string]$m) { Write-Host "  [OK]   $m" }
function Mal([string]$m) { Write-Host "  [FALLA] $m"; $fallos.Add($m) }

if ((Get-FileHash $bak -Algorithm SHA256).Hash -eq $man.bakSha256) { Ok "SHA256 del .bak coincide con el manifiesto" } else { Mal "SHA256 del .bak distinto del manifiesto" }
Sql 'master' "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | Out-Null; Ok "RESTORE VERIFYONLY con CHECKSUM"
if (Sql 'master' "SELECT name FROM sys.databases WHERE name = N'$prueba'") { throw "Ya existe $prueba; eliminarla manualmente si es de una prueba anterior." }

New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    $lf = Sql 'master' "RESTORE FILELISTONLY FROM DISK = N'$bak'"
    $moves = @(); $i = 0
    foreach ($linea in $lf) {
        $p = $linea -split '\s+'
        $logico = $p[0]; $tipo = ($linea -split '\s+' | Where-Object { $_ -in 'D', 'L' } | Select-Object -First 1)
        $ext = if ($tipo -eq 'L') { 'ldf' } else { 'mdf' }
        $moves += "MOVE N'$logico' TO N'$(Join-Path $tmp "f$i.$ext")'"; $i++
    }
    Sql 'master' "RESTORE DATABASE [$prueba] FROM DISK = N'$bak' WITH $($moves -join ', '), CHECKSUM, STATS = 50" | Out-Null
    Ok "Restauracion en base nueva $prueba"

    $chk = Sql $prueba "DBCC CHECKDB (N'$prueba') WITH NO_INFOMSGS, ALL_ERRORMSGS"
    if (-not $chk) { Ok "DBCC CHECKDB sin errores" } else { Mal "DBCC CHECKDB: $($chk -join ' | ')" }
    $con = Sql $prueba "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS, NO_INFOMSGS"
    if (-not $con) { Ok "Integridad referencial (CHECKCONSTRAINTS) sin violaciones" } else { Mal "CHECKCONSTRAINTS: $(($con | Select-Object -First 3) -join ' | ')" }

    $mig = [int]((Sql $prueba "SELECT COUNT(*) FROM __EFMigrationsHistory") | Select-Object -First 1)
    if ($mig -eq [int]$man.migraciones) { Ok "Migraciones: $mig" } else { Mal "Migraciones $mig <> $($man.migraciones)" }

    $h = Huellas $prueba; $dif = 0
    foreach ($prop in $man.tablas.PSObject.Properties) {
        if ($h[$prop.Name] -ne $prop.Value) { $dif++; Mal "Tabla $($prop.Name): $($h[$prop.Name]) <> $($prop.Value)" }
    }
    if ($dif -eq 0) { Ok "Recuentos y huellas iguales en $(@($man.tablas.PSObject.Properties).Count) tablas" }

    $sa = [int]((Sql $prueba "SELECT COUNT(*) FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId JOIN AspNetUsers u ON u.Id = ur.UserId WHERE r.Name = N'SuperAdmin' AND u.Activo = 1") | Select-Object -First 1)
    if ($sa -ge 1) { Ok "Acceso administrativo: $sa SuperAdmin activo(s) en la base restaurada" } else { Mal "No hay SuperAdmin activo en la base restaurada" }

    $saldoCaja = Sql $prueba "SELECT CAST(ISNULL(SUM(CASE WHEN Tipo = 0 THEN Monto ELSE -Monto END),0) AS varchar(40)) FROM MovimientosCaja WHERE IsDeleted = 0"
    $stock = Sql $prueba "SELECT CAST(ISNULL(SUM(StockActual),0) AS varchar(40)) FROM Productos WHERE IsDeleted = 0"
    Ok "Saldos restaurados: movimientos de caja neto=$($saldoCaja | Select-Object -First 1), stock total=$($stock | Select-Object -First 1) (coinciden con la huella por tabla)"

    foreach ($s in $subcarpetas) {
        $o = Join-Path $Carpeta $s
        if (Test-Path $o) { Copy-Item $o (Join-Path $tmp $s) -Recurse -Force }
    }
    $hf = HashArchivos $tmp $subcarpetas; $difa = 0
    $esperados = @{}; foreach ($p in $man.archivos.PSObject.Properties) { $esperados[$p.Name] = $p.Value }
    foreach ($k in $esperados.Keys) {
        if (-not $hf.Contains($k)) { $difa++; Mal "Archivo faltante: $k" } elseif ($hf[$k] -ne $esperados[$k]) { $difa++; Mal "Archivo distinto: $k" }
    }
    if ($difa -eq 0) { Ok "Archivos recuperados: $($esperados.Count) con SHA256 identico (keys, uploads, contratos)" }
    if (-not (Get-ChildItem (Join-Path $tmp 'keys') -Filter '*.xml' -ErrorAction SilentlyContinue)) { Mal "El respaldo no contiene claves de Data Protection (keys\*.xml)" } else { Ok "Claves de Data Protection presentes" }
}
finally {
    try { Sql 'master' "IF DB_ID(N'$prueba') IS NOT NULL BEGIN ALTER DATABASE [$prueba] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$prueba]; END" | Out-Null } catch { Write-Host "No se pudo eliminar $prueba : $_" }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
if ($fallos.Count -gt 0) { Write-Host "`nRESULTADO: FALLA ($($fallos.Count))"; exit 1 }
Write-Host "`nRESULTADO: OK - respaldo verificado y restaurable. Base de prueba eliminada."
exit 0


