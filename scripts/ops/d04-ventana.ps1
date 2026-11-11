<#
.SYNOPSIS
  Runner de la ventana de mantenimiento D-04 (indice unico filtrado de apertura vigente por caja).
  PowerShell 5.1. Seguro por defecto: el modo por defecto es Preflight (solo lectura) y -Base es obligatorio y explicito.

.DESCRIPTION
  Modos:
    Preflight  (default) Solo lectura. Guardas GO/NO-GO sobre el estado de la base.
    Respaldo   BACKUP COPY_ONLY + CHECKSUM + RESTORE VERIFYONLY + huella (conteos) a archivo. No modifica la base.
    Ensayo     Corre el script de migracion con DryRun=1 (aplica y revierte). Sobre la base real exige token y -ConfirmarBaseReal.
    Aplicar    Exige token + respaldo verificado reciente + Preflight GO. Corre el ensayo y luego aplica (DryRun=0).
    Verificar  Post-aplicacion (o post-rollback con -Esperado Previo): historial, indices, huella identica al respaldo.
               -PruebaSegundaApertura (solo bases que NO sean la real): intenta duplicar una apertura vigente dentro de una
               transaccion que siempre se revierte y espera el rechazo 2601.
    Rollback   Reversion de los indices (scripts\sql\d04-rollback-apertura-unica-caja.sql). Exige el mismo token.

  Token: -Autorizado D04-<yyyyMMdd> con la fecha de HOY. Sobre la base TheBuryProjectDb, los modos que escriben (Ensayo,
  Aplicar, Rollback) exigen ADEMAS -ConfirmarBaseReal.

  Codigos de salida: 0 = GO / OK, 2 = NO-GO (una guarda fallo, no se cambio nada), 3 = error de ejecucion.
  Estado del procedimiento: PREPARADO, NO APLICADO. Ver docs\d04-aplicacion-controlada.md.

.EXAMPLE
  .\scripts\ops\d04-ventana.ps1 -Base TheBuryProjectDb_d04                       # Preflight
  .\scripts\ops\d04-ventana.ps1 -Modo Respaldo -Base TheBuryProjectDb_d04
  .\scripts\ops\d04-ventana.ps1 -Modo Aplicar  -Base TheBuryProjectDb_d04 -Autorizado D04-20261111
#>
[CmdletBinding()]
param(
    [ValidateSet('Preflight', 'Respaldo', 'Ensayo', 'Aplicar', 'Verificar', 'Rollback')]
    [string]$Modo = 'Preflight',
    [Parameter(Mandatory = $true)][string]$Base,
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    [string]$DirTrabajo = 'D:\tmp\d04-ventana',
    [string]$Autorizado,
    [switch]$ConfirmarBaseReal,
    [ValidateSet('Aplicada', 'Previo')][string]$Esperado = 'Aplicada',
    [switch]$PruebaSegundaApertura,
    [switch]$PermitirTurnosAbiertos,
    [int]$RespaldoMaxMinutos = 120
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version 2
$BaseReal = 'TheBuryProjectDb'
$Migracion = '20261108185231_D04AperturaCajaUnicaAbierta'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$SqlDir = Join-Path $RepoRoot 'scripts\sql'
$sellos = Get-Date -Format 'yyyyMMdd-HHmmss'
$esReal = ($Base -ieq $BaseReal)

$DirTrabajo = [System.IO.Path]::GetFullPath($DirTrabajo)
New-Item -ItemType Directory -Force -Path (Join-Path $DirTrabajo 'logs') | Out-Null
$LogFile = Join-Path $DirTrabajo ("logs\d04-{0}-{1}-{2}.log" -f $Modo, $Base, $sellos)

function Log([string]$m) {
    $l = "{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $LogFile -Value $l -Encoding UTF8
}

$script:NoGo = 0
function Guarda([string]$nombre, [bool]$ok, [string]$detalle) {
    if ($ok) { Log ("[GO]    {0} - {1}" -f $nombre, $detalle) }
    else { Log ("[NO-GO] {0} - {1}" -f $nombre, $detalle); $script:NoGo++ }
}
function Salir([int]$codigo, [string]$msg) {
    Log $msg
    Log ("Log: {0}" -f $LogFile)
    exit $codigo
}

# --- sqlcmd -----------------------------------------------------------------------------------------------------------
function SqlArchivo([string]$db, [string]$archivo, [string[]]$vars) {
    $a = @('-S', $Servidor, '-d', $db, '-E', '-C', '-I', '-b', '-f', '65001', '-i', $archivo)
    foreach ($v in $vars) { $a += @('-v', $v) }
    $out = & sqlcmd @a 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = @($out) }
}
# Consulta de filas: devuelve arreglo de arreglos de columnas (separador |).
function SqlFilas([string]$db, [string]$sql) {
    $out = & sqlcmd -S $Servidor -d $db -E -C -I -b -h -1 -W -s '|' -f 65001 -Q ("SET NOCOUNT ON; " + $sql) 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw ("sqlcmd fallo: " + ($out -join ' / ')) }
    $filas = @()
    foreach ($l in $out) { if ($l.Trim().Length -gt 0) { $filas += , @($l.Split('|')) } }
    return , $filas
}
function SqlEscalar([string]$db, [string]$sql) {
    $f = SqlFilas $db $sql
    if ($f.Count -eq 0) { return $null }
    return $f[0][0]
}
function SqlNoQuery([string]$db, [string]$sql) {
    $out = & sqlcmd -S $Servidor -d $db -E -C -I -b -f 65001 -Q $sql 2>&1 | ForEach-Object { "$_" }
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = @($out) }
}

# --- estado de la base ------------------------------------------------------------------------------------------------
function MigracionesDelRepo {
    $ids = @()
    foreach ($f in Get-ChildItem (Join-Path $RepoRoot 'Migrations') -Filter '*.cs' -File) {
        $m = Select-String -Path $f.FullName -Pattern 'Migration\("(\d{14}_[A-Za-z0-9_]+)"\)' -AllMatches
        foreach ($x in $m) { foreach ($mm in $x.Matches) { $ids += $mm.Groups[1].Value } }
    }
    return @($ids | Sort-Object -Unique)
}

function LeerEstado([string]$db) {
    $e = @{}
    $e.Existe = ([int](SqlEscalar 'master' ("SELECT COUNT(*) FROM sys.databases WHERE name = N'{0}' AND database_id > 4" -f $db.Replace("'", "''"))) -eq 1)
    if (-not $e.Existe) { return $e }
    $e.Migs = @((SqlFilas $db 'SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId') | ForEach-Object { $_[0] })
    $e.D04Registrada = ($e.Migs -contains $Migracion)
    $v = (SqlFilas $db "SELECT COUNT(*), ISNULL(SUM(CASE WHEN Cerrada = 0 AND IsDeleted = 0 THEN 1 ELSE 0 END),0) FROM dbo.AperturasCaja")[0]
    $e.Aperturas = [int]$v[0]; $e.Vigentes = [int]$v[1]
    $e.CajasDuplicadas = [int](SqlEscalar $db "SELECT COUNT(*) FROM (SELECT CajaId FROM dbo.AperturasCaja WHERE Cerrada = 0 AND IsDeleted = 0 GROUP BY CajaId HAVING COUNT(*) > 1) d")
    $e.Indices = @((SqlFilas $db "SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name IS NOT NULL ORDER BY name") | ForEach-Object { $_[0] })
    $e.UnicoFiltradoOk = ([int](SqlEscalar $db "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AperturasCaja') AND name = N'UX_AperturasCaja_CajaId_Abierta' AND is_unique = 1 AND filter_definition LIKE N'%Cerrada%' AND filter_definition LIKE N'%IsDeleted%'") -eq 1)
    $e.ConexionesAjenas = [int](SqlEscalar $db "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID AND is_user_process = 1")
    $h = (SqlFilas $db "SELECT (SELECT COUNT(*) FROM dbo.Cajas), (SELECT COUNT(*) FROM dbo.AperturasCaja), (SELECT COUNT(*) FROM dbo.MovimientosCaja), (SELECT COUNT(*) FROM dbo.Ventas), (SELECT COUNT(*) FROM dbo.Cuotas), (SELECT COUNT(*) FROM dbo.Creditos)")[0]
    $e.Huella = [ordered]@{ Cajas = [int]$h[0]; AperturasCaja = [int]$h[1]; MovimientosCaja = [int]$h[2]; Ventas = [int]$h[3]; Cuotas = [int]$h[4]; Creditos = [int]$h[5] }
    $e.SizeMB = [double](SqlEscalar $db "SELECT SUM(size)*8.0/1024 FROM sys.master_files WHERE database_id = DB_ID(N'$($db.Replace("'", "''"))')")
    return $e
}

function EstadoPrevioOk($e) { return ($e.Indices -contains 'IX_AperturasCaja_CajaId') -and -not ($e.Indices -contains 'UX_AperturasCaja_CajaId_Abierta') -and -not ($e.Indices -contains 'IX_AperturasCaja_CajaId_Cerrada') }
function EstadoAplicadoOk($e) { return ($e.Indices -contains 'IX_AperturasCaja_CajaId_Cerrada') -and $e.UnicoFiltradoOk -and -not ($e.Indices -contains 'IX_AperturasCaja_CajaId') }

function EvaluarHistorial($e) {
    # Logica (reemplaza "136 exactas" fijas): lo aplicado debe ser un PREFIJO exacto de lo conocido por el codigo de main
    # (ningun extra desconocido y ningun hueco) y lo unico pendiente puede ser D-04. El script SQL conserva ademas su propia
    # guarda de historial exacto (51054), que se ejercita en Ensayo/Aplicar.
    $repo = MigracionesDelRepo
    $aplicadas = @($e.Migs)
    $extras = @($aplicadas | Where-Object { $repo -notcontains $_ })
    $pend = @($repo | Where-Object { $aplicadas -notcontains $_ })
    $sinD04 = @($pend | Where-Object { $_ -ne $Migracion })
    # prefijo: lo aplicado (sin D04) debe ser igual a lo conocido anterior a D04 o a un prefijo contiguo
    return [pscustomobject]@{ Repo = $repo.Count; Aplicadas = $aplicadas.Count; Extras = $extras; Pendientes = $pend; PendientesNoD04 = $sinD04 }
}

# --- guardas comunes ---------------------------------------------------------------------------------------------------
function TokenValido {
    $esperado = 'D04-' + (Get-Date -Format 'yyyyMMdd')
    return ($Autorizado -ceq $esperado)
}
function ExigirAutorizacion([string]$que) {
    Guarda 'Token' (TokenValido) ("-Autorizado debe ser D04-{0} (fecha de hoy)" -f (Get-Date -Format 'yyyyMMdd'))
    if ($esReal) {
        Guarda 'BaseReal' ([bool]$ConfirmarBaseReal) "la base es ${BaseReal}: ademas del token se exige -ConfirmarBaseReal"
    }
    if ($script:NoGo -gt 0) { Salir 2 "NO-GO: $que abortado antes de tocar nada." }
}

function UltimaHuella([string]$db) {
    $f = Get-ChildItem $DirTrabajo -Filter ("{0}-previo-*.huella.json" -f $db) -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $f) { return $null }
    $o = Get-Content $f.FullName -Raw | ConvertFrom-Json
    $o | Add-Member -NotePropertyName ArchivoHuella -NotePropertyValue $f.FullName -Force
    return $o
}

function GuardasPreflight($e, [bool]$paraAplicar) {
    Guarda 'Base' $e.Existe ("la base '{0}' existe en {1}" -f $Base, $Servidor)
    if (-not $e.Existe) { return }
    $h = EvaluarHistorial $e
    Guarda 'Historial' (($h.Extras.Count -eq 0) -and ($h.PendientesNoD04.Count -eq 0)) ("aplicadas={0}, conocidas en codigo={1}, extras desconocidas={2}, pendientes distintas de D-04={3}" -f $h.Aplicadas, $h.Repo, $h.Extras.Count, $h.PendientesNoD04.Count)
    if ($h.PendientesNoD04.Count -gt 0) { Log ("         pendientes ajenas a D-04: " + ($h.PendientesNoD04 -join ', ')) }
    if ($h.Extras.Count -gt 0) { Log ("         extras: " + ($h.Extras -join ', ')) }
    Guarda 'D04NoRegistrada' (-not $e.D04Registrada) "D-04 aun no figura aplicada (registrada=$($e.D04Registrada))"
    Guarda 'IndicesPrevios' (EstadoPrevioOk $e) ("indices actuales: " + ($e.Indices -join ', '))
    Guarda 'SinDuplicados' ($e.CajasDuplicadas -eq 0) "cajas con mas de una apertura vigente: $($e.CajasDuplicadas)"
    $okTurnos = ($e.Vigentes -eq 0) -or [bool]$PermitirTurnosAbiertos
    Guarda 'SinTurnoAbierto' $okTurnos ("aperturas vigentes (turnos abiertos): {0}{1}" -f $e.Vigentes, $(if ($PermitirTurnosAbiertos) { ' (permitido por -PermitirTurnosAbiertos)' } else { '' }))
    Guarda 'SinConexionesAjenas' ($e.ConexionesAjenas -eq 0) "conexiones de usuario a la base: $($e.ConexionesAjenas) (detener la aplicacion)"
    $drive = Split-Path -Qualifier (Resolve-Path $DirTrabajo).Path
    $libreMB = [math]::Round((Get-PSDrive ($drive.TrimEnd(':'))).Free / 1MB)
    $necesarioMB = [math]::Max(1024, [math]::Ceiling($e.SizeMB * 3))
    Guarda 'EspacioDisco' ($libreMB -ge $necesarioMB) ("libre en {0}: {1} MB; necesario (3x base, min 1 GB): {2} MB" -f $drive, $libreMB, $necesarioMB)
    Log ("Huella: " + (($e.Huella.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ') + (", tamano base={0:N0} MB" -f $e.SizeMB))
}

# =====================================================================================================================
Log ("D-04 ventana :: modo={0} base={1} servidor={2}" -f $Modo, $Base, $Servidor)
Log ("Estado del procedimiento: PREPARADO, NO APLICADO (este runner no aplica nada sin token + guardas)")
if ($Base -notmatch '^[A-Za-z0-9_\-\.]+$') { Salir 3 "Nombre de base invalido." }

$e = LeerEstado $Base

switch ($Modo) {

    'Preflight' {
        GuardasPreflight $e $false
        if ($script:NoGo -eq 0) { Salir 0 "PREFLIGHT: GO" }
        if ($e.Existe -and $e.D04Registrada -and (EstadoAplicadoOk $e)) { Log "Nota: D-04 ya esta aplicada en esta base (no hay nada que aplicar)." }
        Salir 2 ("PREFLIGHT: NO-GO ({0} guardas fallidas)" -f $script:NoGo)
    }

    'Respaldo' {
        Guarda 'Base' $e.Existe "la base existe"
        if (-not $e.Existe) { Salir 2 "NO-GO" }
        $bak = Join-Path $DirTrabajo ("{0}-previo-{1}.bak" -f $Base, $sellos)
        Log "BACKUP DATABASE COPY_ONLY, CHECKSUM -> $bak"
        $t0 = Get-Date
        $r = SqlNoQuery 'master' ("BACKUP DATABASE [{0}] TO DISK = N'{1}' WITH COPY_ONLY, CHECKSUM, INIT, STATS = 100" -f $Base, $bak)
        if ($r.Exit -ne 0) { Log ($r.Out -join "`n"); Salir 3 "ERROR: el BACKUP fallo." }
        $tBackup = ((Get-Date) - $t0).TotalSeconds
        $r = SqlNoQuery 'master' ("RESTORE VERIFYONLY FROM DISK = N'{0}' WITH CHECKSUM" -f $bak)
        $verificado = ($r.Exit -eq 0)
        Log ($r.Out -join ' / ')
        Guarda 'RestoreVerifyOnly' $verificado "RESTORE VERIFYONLY ... WITH CHECKSUM"
        $tTotal = ((Get-Date) - $t0).TotalSeconds
        $obj = [ordered]@{
            Base = $Base; Servidor = $Servidor; Respaldo = $bak; CreadoUtc = (Get-Date).ToUniversalTime().ToString('o')
            VerificadoCHECKSUM = $verificado; BytesRespaldo = (Get-Item $bak).Length
            Migraciones = $e.Migs.Count; D04Registrada = $e.D04Registrada; Indices = $e.Indices; Huella = $e.Huella
            SegundosBackup = [math]::Round($tBackup, 1); SegundosTotal = [math]::Round($tTotal, 1)
        }
        $hf = [System.IO.Path]::ChangeExtension($bak, '.huella.json')
        ($obj | ConvertTo-Json -Depth 5) | Set-Content -Path $hf -Encoding UTF8
        Log ("Huella escrita: {0}" -f $hf)
        Log ("Duraciones: backup={0:N1}s, backup+verify={1:N1}s, tamano respaldo={2:N1} MB" -f $tBackup, $tTotal, ($obj.BytesRespaldo / 1MB))
        if (-not $verificado) { Salir 2 "NO-GO: el respaldo no verifico." }
        Salir 0 "RESPALDO: OK (verificado)"
    }

    'Ensayo' {
        if ($esReal) { ExigirAutorizacion 'Ensayo' }
        GuardasPreflight $e $false
        if ($script:NoGo -gt 0) { Salir 2 "NO-GO: ensayo no ejecutado." }
        $t0 = Get-Date
        $r = SqlArchivo $Base (Join-Path $SqlDir 'd04-migracion-apertura-unica-caja.sql') @('DryRun=1', "BaseEsperada=$Base")
        Log ($r.Out -join "`n")
        $ok = ($r.Exit -eq 0) -and (($r.Out -join "`n") -match 'DryRun=1: se revierte')
        $e2 = LeerEstado $Base
        $intacto = ($e2.Migs.Count -eq $e.Migs.Count) -and (EstadoPrevioOk $e2)
        Guarda 'EnsayoSinError' $ok "el script termino con 'DryRun=1: se revierte' (exit=$($r.Exit))"
        Guarda 'BaseIntacta' $intacto "tras el ensayo: $($e2.Migs.Count) migraciones, indices previos"
        Log ("Duracion ensayo: {0:N2}s" -f ((Get-Date) - $t0).TotalSeconds)
        if ($script:NoGo -gt 0) { Salir 2 "ENSAYO: NO-GO" }
        Salir 0 "ENSAYO: GO (aplica y revierte sin error)"
    }

    'Aplicar' {
        ExigirAutorizacion 'Aplicar'
        if ($e.Existe -and $e.D04Registrada -and (EstadoAplicadoOk $e)) {
            Salir 0 "D-04 ya esta aplicada y los indices existen: no se hace nada (idempotente)."
        }
        GuardasPreflight $e $true
        # respaldo verificado reciente y huella vigente
        $hu = UltimaHuella $Base
        Guarda 'RespaldoExiste' ($null -ne $hu) "respaldo con huella en $DirTrabajo"
        if ($hu) {
            $edad = ((Get-Date).ToUniversalTime() - ([datetime]::Parse($hu.CreadoUtc).ToUniversalTime())).TotalMinutes
            Guarda 'RespaldoVerificado' ([bool]$hu.VerificadoCHECKSUM -and (Test-Path $hu.Respaldo)) "RESTORE VERIFYONLY ok y archivo presente: $($hu.Respaldo)"
            Guarda 'RespaldoReciente' ($edad -le $RespaldoMaxMinutos) ("antiguedad {0:N0} min (maximo {1})" -f $edad, $RespaldoMaxMinutos)
            $igual = $true
            foreach ($k in $e.Huella.Keys) { if ([int]$hu.Huella.$k -ne [int]$e.Huella[$k]) { $igual = $false; Log "         huella distinta: $k respaldo=$($hu.Huella.$k) actual=$($e.Huella[$k])" } }
            Guarda 'HuellaVigente' $igual "los conteos actuales coinciden con los del respaldo (nada cambio despues del respaldo)"
        }
        if ($script:NoGo -gt 0) { Salir 2 ("APLICAR: NO-GO ({0} guardas fallidas). No se cambio nada." -f $script:NoGo) }

        $t0 = Get-Date
        Log "Paso 1/2: ensayo DryRun=1"
        $r = SqlArchivo $Base (Join-Path $SqlDir 'd04-migracion-apertura-unica-caja.sql') @('DryRun=1', "BaseEsperada=$Base")
        Log ($r.Out -join "`n")
        if (($r.Exit -ne 0) -or (($r.Out -join "`n") -notmatch 'DryRun=1: se revierte')) { Salir 2 "APLICAR: el ensayo previo fallo. No se cambio nada." }
        Log "Paso 2/2: aplicacion DryRun=0"
        $r = SqlArchivo $Base (Join-Path $SqlDir 'd04-migracion-apertura-unica-caja.sql') @('DryRun=0', "BaseEsperada=$Base")
        Log ($r.Out -join "`n")
        $seg = ((Get-Date) - $t0).TotalSeconds
        if (($r.Exit -ne 0) -or (($r.Out -join "`n") -notmatch 'Aplicado\.')) { Salir 3 "ERROR: la aplicacion no termino en 'Aplicado.'. Ejecutar -Modo Verificar; si hay dudas, Rollback o restaurar el respaldo." }
        Log ("Duracion (ensayo + aplicacion): {0:N2}s" -f $seg)
        Salir 0 "APLICAR: OK. Siguiente paso: -Modo Verificar."
    }

    'Verificar' {
        Guarda 'Base' $e.Existe "la base existe"
        if (-not $e.Existe) { Salir 2 "NO-GO" }
        $h = EvaluarHistorial $e
        if ($Esperado -eq 'Aplicada') {
            Guarda 'D04Registrada' $e.D04Registrada "D-04 registrada en __EFMigrationsHistory (total $($e.Migs.Count))"
            Guarda 'SinPendientes' ($h.Pendientes.Count -eq 0 -and $h.Extras.Count -eq 0) "pendientes=$($h.Pendientes.Count) extras=$($h.Extras.Count) (codigo conoce $($h.Repo))"
            Guarda 'Indices' (EstadoAplicadoOk $e) ("indices: " + ($e.Indices -join ', '))
        }
        else {
            Guarda 'D04NoRegistrada' (-not $e.D04Registrada) "D-04 no registrada (total $($e.Migs.Count))"
            Guarda 'Indices' (EstadoPrevioOk $e) ("indices: " + ($e.Indices -join ', '))
            Guarda 'SoloD04Pendiente' ($h.PendientesNoD04.Count -eq 0 -and $h.Extras.Count -eq 0) "pendientes distintas de D-04: $($h.PendientesNoD04.Count)"
        }
        Guarda 'SinDuplicados' ($e.CajasDuplicadas -eq 0) "cajas duplicadas: $($e.CajasDuplicadas)"
        $hu = UltimaHuella $Base
        if ($hu) {
            $igual = $true
            foreach ($k in $e.Huella.Keys) { if ([int]$hu.Huella.$k -ne [int]$e.Huella[$k]) { $igual = $false; Log "         huella distinta: $k respaldo=$($hu.Huella.$k) actual=$($e.Huella[$k])" } }
            Guarda 'HuellaIdentica' $igual ("conteos iguales a la huella de $($hu.ArchivoHuella)")
        }
        else { Guarda 'HuellaIdentica' $false "no hay huella (ejecutar -Modo Respaldo antes)" }

        if ($PruebaSegundaApertura) {
            if ($esReal) { Guarda 'PruebaSegundaApertura' $false "se niega a escribir (aunque sea con rollback) en $BaseReal" }
            elseif ($Esperado -ne 'Aplicada') { Guarda 'PruebaSegundaApertura' $false "solo tiene sentido con -Esperado Aplicada" }
            else {
                # Todo dentro de una transaccion que SIEMPRE se revierte; la segunda insercion debe fallar con 2601 (indice unico filtrado).
                $sql = @"
SET NOCOUNT ON; SET XACT_ABORT OFF; SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
DECLARE @c int, @err int = 0, @msg nvarchar(400) = N'';
INSERT INTO dbo.Cajas (Codigo, Nombre, Activa, Estado, CreatedAt, IsDeleted) VALUES (N'D04PRB', N'D04 prueba', 1, 0, SYSUTCDATETIME(), 0);
SET @c = SCOPE_IDENTITY();
INSERT INTO dbo.AperturasCaja (CajaId, FechaApertura, MontoInicial, UsuarioApertura, Cerrada, CreatedAt, IsDeleted) VALUES (@c, SYSUTCDATETIME(), 0, N'prueba1', 0, SYSUTCDATETIME(), 0);
BEGIN TRY
  INSERT INTO dbo.AperturasCaja (CajaId, FechaApertura, MontoInicial, UsuarioApertura, Cerrada, CreatedAt, IsDeleted) VALUES (@c, SYSUTCDATETIME(), 0, N'prueba2', 0, SYSUTCDATETIME(), 0);
END TRY
BEGIN CATCH
  SET @err = ERROR_NUMBER(); SET @msg = ERROR_MESSAGE();
END CATCH;
-- con la primera cerrada, una nueva apertura debe ser posible
UPDATE dbo.AperturasCaja SET Cerrada = 1 WHERE CajaId = @c;
DECLARE @reabre int = 1;
BEGIN TRY
  INSERT INTO dbo.AperturasCaja (CajaId, FechaApertura, MontoInicial, UsuarioApertura, Cerrada, CreatedAt, IsDeleted) VALUES (@c, SYSUTCDATETIME(), 0, N'prueba3', 0, SYSUTCDATETIME(), 0);
END TRY
BEGIN CATCH
  SET @reabre = 0;
END CATCH;
ROLLBACK TRANSACTION;
SELECT @err AS ErrorSegunda, CASE WHEN @msg LIKE N'%UX_AperturasCaja_CajaId_Abierta%' THEN 1 ELSE 0 END AS NombraIndice, @reabre AS ReaperturaPosible, (SELECT COUNT(*) FROM dbo.Cajas WHERE Codigo = N'D04PRB') AS CajaPruebaResidual;
"@
                $f = (SqlFilas $Base $sql)[0]
                Guarda 'PruebaSegundaApertura' (([int]$f[0] -eq 2601) -and ([int]$f[1] -eq 1) -and ([int]$f[2] -eq 1) -and ([int]$f[3] -eq 0)) ("segunda apertura vigente rechazada con error {0} (nombra el indice={1}); reapertura tras cierre posible={2}; residuo de prueba={3} (todo revertido)" -f $f[0], $f[1], $f[2], $f[3])
            }
        }
        if ($script:NoGo -gt 0) { Salir 2 ("VERIFICAR: NO-GO ({0} guardas fallidas)" -f $script:NoGo) }
        Salir 0 "VERIFICAR: OK (estado esperado: $Esperado)"
    }

    'Rollback' {
        ExigirAutorizacion 'Rollback'
        Guarda 'Base' $e.Existe "la base existe"
        if ($e.Existe) {
            Guarda 'SinConexionesAjenas' ($e.ConexionesAjenas -eq 0) "conexiones de usuario: $($e.ConexionesAjenas)"
            $hu = UltimaHuella $Base
            Guarda 'HayRespaldo' ($null -ne $hu -and (Test-Path $hu.Respaldo)) "existe un respaldo verificado previo (via de recuperacion alternativa)"
        }
        if ($script:NoGo -gt 0) { Salir 2 "ROLLBACK: NO-GO. No se cambio nada." }
        if (-not $e.D04Registrada) { Salir 0 "D-04 no esta registrada: no hay nada que revertir." }
        $t0 = Get-Date
        $r = SqlArchivo $Base (Join-Path $SqlDir 'd04-rollback-apertura-unica-caja.sql') @('DryRun=1', "BaseEsperada=$Base")
        Log ($r.Out -join "`n")
        if (($r.Exit -ne 0) -or (($r.Out -join "`n") -notmatch 'DryRun=1: se revierte')) { Salir 2 "ROLLBACK: el ensayo de la reversion fallo. No se cambio nada." }
        $r = SqlArchivo $Base (Join-Path $SqlDir 'd04-rollback-apertura-unica-caja.sql') @('DryRun=0', "BaseEsperada=$Base")
        Log ($r.Out -join "`n")
        if (($r.Exit -ne 0) -or (($r.Out -join "`n") -notmatch 'Revertido\.')) { Salir 3 "ERROR: la reversion no termino en 'Revertido.'. Restaurar el respaldo." }
        Log ("Duracion (ensayo + reversion): {0:N2}s" -f ((Get-Date) - $t0).TotalSeconds)
        Salir 0 "ROLLBACK: OK. Siguiente paso: -Modo Verificar -Esperado Previo."
    }
}
