<#
.SYNOPSIS
  P-03: ensayo de la transicion de cuentas reales (docs/preproduccion-seguridad-y-transicion.md Sec. 3)
  contra un CLON de la base. Nunca contra la base real.

.DESCRIPTION
  Acciones (combinables):
    -CrearClon  BACKUP ... COPY_ONLY de la base origen (solo lectura) y RESTORE como <origen>_p03 (o el nombre dado).
    -Ejecutar   levanta la app propia (bin\p03run) sobre el clon y recorre los pasos 1-6 por HTTP, con evidencia SQL de solo lectura.
    -Limpiar    DROP del clon (SINGLE_USER WITH ROLLBACK_IMMEDIATE) y borra .bak/.mdf/.ldf propios.

  GUARDAS: el nombre de base de datos debe terminar en _p03 o _qa y no puede ser TheBuryProjectDb;
  se aborta si la cadena de conexion resultante apunta a TheBuryProjectDb exacta. Las consultas SQL del ensayo
  solo admiten SELECT. Las unicas escrituras en el clon las hace la propia app (como lo haria un operador).

  CONTRASENAS: nunca se imprimen ni se escriben. Se generan en memoria (RNG criptografico) o se toman de
  variables de entorno: P03_ADMIN_PASSWORD (contrasena ACTUAL de 'admin' en el clon, obligatoria),
  y opcionales P03_PWD_OWNER, P03_PWD_RECOVERY, P03_PWD_ADMIN_NUEVA.

.EXAMPLE
  $env:P03_ADMIN_PASSWORD = '<la actual de admin en el clon>'
  .\scripts\qa\p03-ensayo-transicion-cuentas.ps1 -CrearClon -Ejecutar -Limpiar -Entorno Development
#>
[CmdletBinding()]
param(
    [string]$ServidorSql = '(localdb)\MSSQLLocalDB',
    [string]$BaseOrigen = 'TheBuryProjectDb',
    [string]$BaseClon = 'TheBuryProjectDb_p03',
    [string]$CarpetaTrabajo = 'C:\Users\Public',
    [string]$Prefijo = 'tbp_p03',
    [int]$Puerto = 5197,
    [ValidateSet('Development', 'Staging')][string]$Entorno = 'Development',
    [string]$DirectorioBuild = '',
    [string[]]$CuentasDemoADesactivar = @('administrador', 'cajero', 'contador', 'gerente', 'repositor', 'tecnico', 'vendedor'),
    [string]$SalidaJson = '',
    [switch]$CrearClon,
    [switch]$Ejecutar,
    [switch]$Limpiar
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $DirectorioBuild) { $DirectorioBuild = Join-Path $raiz 'bin\p03run' }
$bak = Join-Path $CarpetaTrabajo "$Prefijo.bak"
$mdf = Join-Path $CarpetaTrabajo "$Prefijo.mdf"
$ldf = Join-Path $CarpetaTrabajo "${Prefijo}_log.ldf"

# ---------------------------------------------------------------- guardas
function Assert-Clon([string]$nombre) {
    if ($nombre -ieq 'TheBuryProjectDb' -or $nombre -ieq $BaseOrigen) { throw "GUARDA: '$nombre' es la base real. Abortado." }
    if ($nombre -notmatch '(?i)_(p03|qa)$') { throw "GUARDA: la base '$nombre' no termina en _p03 o _qa. Abortado." }
}
Assert-Clon $BaseClon
$cadena = "Server=$ServidorSql;Database=$BaseClon;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
if ($cadena -match '(?i)Database=TheBuryProjectDb(;|$)') { throw 'GUARDA: la cadena de conexion apunta a TheBuryProjectDb exacta. Abortado.' }

function Invoke-SqlMaster([string]$q) {
    $out = & sqlcmd -S $ServidorSql -E -C -b -h -1 -W -Q "SET NOCOUNT ON; $q" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd fallo: $($out -join ' ')" }
    return $out
}
function Get-Sql([string]$q) {   # solo lectura sobre el clon
    if ($q -notmatch '^\s*SELECT\b') { throw 'Get-Sql solo admite SELECT.' }
    Assert-Clon $BaseClon
    $out = & sqlcmd -S $ServidorSql -E -C -b -d $BaseClon -h -1 -W -s '|' -Q "SET NOCOUNT ON; $q" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd fallo: $($out -join ' ')" }
    return ,@($out | Where-Object { $_ -and $_.Trim() })
}

# ---------------------------------------------------------------- clon
function Nuevo-Clon {
    Assert-Clon $BaseClon
    Write-Host "Backup COPY_ONLY de $BaseOrigen (solo lectura) -> $bak"
    Invoke-SqlMaster "BACKUP DATABASE [$BaseOrigen] TO DISK='$bak' WITH COPY_ONLY, INIT, FORMAT" | Out-Null
    $lf = Invoke-SqlMaster "RESTORE FILELISTONLY FROM DISK='$bak'"
    $logico = @($lf | ForEach-Object { ($_ -split '\s+')[0] })
    Write-Host "Restore como $BaseClon"
    Invoke-SqlMaster "RESTORE DATABASE [$BaseClon] FROM DISK='$bak' WITH MOVE '$($logico[0])' TO '$mdf', MOVE '$($logico[1])' TO '$ldf', REPLACE" | Out-Null
}
function Quitar-Clon {
    Assert-Clon $BaseClon
    Write-Host "DROP $BaseClon y archivos propios"
    Invoke-SqlMaster "IF DB_ID('$BaseClon') IS NOT NULL BEGIN ALTER DATABASE [$BaseClon] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$BaseClon]; END" | Out-Null
    foreach ($f in @($bak, $mdf, $ldf)) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
    if (Test-Path -LiteralPath $DirectorioBuild) { Remove-Item -LiteralPath $DirectorioBuild -Recurse -Force }
}

# ---------------------------------------------------------------- contrasenas
function Nueva-Clave {
    $alfa = 'abcdefghijkmnopqrstuvwxyz'; $mayus = 'ABCDEFGHJKLMNPQRSTUVWXYZ'; $dig = '23456789'; $sim = '-_.'
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    function Pick([string]$set) { $b = New-Object byte[] 4; $rng.GetBytes($b); $set[[int]([BitConverter]::ToUInt32($b, 0) % $set.Length)] }
    $chars = @((Pick $mayus), (Pick $alfa), (Pick $dig), (Pick $sim))
    1..12 | ForEach-Object { $chars += (Pick ($alfa + $mayus + $dig)) }
    return -join ($chars | Sort-Object { Get-Random })
}
function Clave([string]$envName) { $v = [Environment]::GetEnvironmentVariable($envName); if ($v) { $v } else { Nueva-Clave } }

# ---------------------------------------------------------------- HTTP
$script:base = "http://127.0.0.1:$Puerto"
function Nueva-Sesion([string]$nombre) {
    $h = New-Object System.Net.Http.HttpClientHandler
    $h.AllowAutoRedirect = $false
    $h.CookieContainer = New-Object System.Net.CookieContainer
    $c = New-Object System.Net.Http.HttpClient($h)
    $c.Timeout = [TimeSpan]::FromSeconds(60)
    return [pscustomobject]@{ Nombre = $nombre; Cliente = $c; Handler = $h }
}
function Send([object]$s, [string]$metodo, [string]$ruta, [hashtable]$form) {
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($metodo), ($script:base + $ruta))
    $req.Headers.TryAddWithoutValidation('Accept', 'text/html,application/json') | Out-Null
    if ($form) {
        $pairs = New-Object 'System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,string]]'
        foreach ($k in $form.Keys) {
            foreach ($v in @($form[$k])) { $pairs.Add((New-Object 'System.Collections.Generic.KeyValuePair[string,string]'($k, [string]$v))) }
        }
        $req.Content = New-Object System.Net.Http.FormUrlEncodedContent(,$pairs)
    }
    $resp = $s.Cliente.SendAsync($req).GetAwaiter().GetResult()
    $cuerpo = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $loc = if ($resp.Headers.Location) { $resp.Headers.Location.ToString() } else { '' }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Location = $loc; Body = $cuerpo }
}
function Get-Token([string]$html) {
    if ($html -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
    if ($html -match 'name="csrf-token"\s+content="([^"]+)"') { return $Matches[1] }
    throw 'No se encontro token antiforgery.'
}
function Token-De([object]$s, [string]$ruta) {
    $b = (Send $s 'GET' $ruta $null).Body
    if ($b -notmatch 'csrf-token|__RequestVerificationToken') { $b = (Send $s 'GET' '/Identity/Account/Login' $null).Body }
    Get-Token $b
}

function Login([object]$s, [string]$usuario, [string]$clave) {
    $t = Get-Token (Send $s 'GET' '/Identity/Account/Login' $null).Body
    $r = Send $s 'POST' '/Identity/Account/Login' @{ 'Input.UserName' = $usuario; 'Input.Password' = $clave; 'Input.RememberMe' = 'false'; '__RequestVerificationToken' = $t }
    # con Terminos pendientes el servidor responde 200 (mismo formulario, paso 2); la prueba de login es la cookie de Identity
    $cookie = $s.Handler.CookieContainer.GetCookies([Uri]$script:base) | Where-Object { $_.Name -eq '.AspNetCore.Identity.Application' }
    return ([bool]$cookie -and $r.Location -notmatch 'Lockout')
}
function Terminos-Pendientes([object]$s) {
    $r = Send $s 'GET' '/Seguridad' $null
    return ($r.Status -eq 302 -and $r.Location -match 'Identity/Account/Login')
}
function Aceptar-Terminos([object]$s, [string]$nombreCompleto) {
    $html = (Send $s 'GET' '/Identity/Account/Login' $null).Body
    $t = Get-Token $html
    $r = Send $s 'POST' '/Identity/Account/Login' @{ 'Input.NombreCompleto' = $nombreCompleto; 'Input.AceptaTerminos' = 'true'; '__RequestVerificationToken' = $t; 'ReturnUrl' = '/' }
    return ($r.Status -eq 302)
}
# 'OK' si responde 200; 'SIN-ACCESO(<status> <loc>)' si redirige/401/403
function Acceso([object]$s, [string]$ruta = '/Seguridad') {
    $r = Send $s 'GET' $ruta $null
    if ($r.Status -eq 200) { return 'OK' }
    return "SIN-ACCESO($($r.Status) $($r.Location))"
}
function Post-Accion([object]$s, [string]$ruta, [hashtable]$campos) {
    $campos['__RequestVerificationToken'] = Token-De $s '/Seguridad'
    Send $s 'POST' $ruta $campos
}
function Id-De([string]$usuario) { (Get-Sql "SELECT Id FROM AspNetUsers WHERE UserName='$usuario'")[0].Trim() }
function Activo-De([string]$usuario) { (Get-Sql "SELECT Activo FROM AspNetUsers WHERE UserName='$usuario'")[0].Trim() -eq '1' }
function SuperAdmins-Activos {
    [int](Get-Sql "SELECT COUNT(DISTINCT u.Id) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE r.Name='SuperAdmin' AND u.Activo=1")[0]
}
function Crear-SuperAdmin([object]$s, [string]$usuario, [string]$clave, [string]$nombre) {
    $r = Post-Accion $s '/Usuarios/Create' @{ UserName = $usuario; Email = "$usuario@ensayo.invalid"; Password = $clave; ConfirmPassword = $clave; Nombre = $nombre; Apellido = 'Ensayo'; RolesSeleccionados = 'SuperAdmin'; Activo = 'true'; EmailConfirmed = 'true' }
    $script:ultimoCuerpo = $r.Body
    return ($r.Status -eq 200 -and $r.Body -match '"success"\s*:\s*true')
}
function Cambiar-Clave([object]$s, [string]$usuario, [string]$clave) {
    $r = Post-Accion $s '/Usuarios/CambiarPassword' @{ UserId = (Id-De $usuario); UserName = $usuario; NewPassword = $clave; ConfirmPassword = $clave }
    return ($r.Status -eq 200 -and $r.Body -match '"success"\s*:\s*true')
}

# ---------------------------------------------------------------- resultados
$script:res = New-Object System.Collections.Generic.List[object]
function Paso([string]$id, [string]$desc, [string]$estado, [string]$evidencia) {
    $script:res.Add([pscustomobject]@{ Paso = $id; Descripcion = $desc; Resultado = $estado; Evidencia = $evidencia })
    Write-Host ("[{0}] {1} {2} :: {3}" -f $estado, $id, $desc, $evidencia)
}
function Verificar([string]$id, [string]$desc, [bool]$ok, [string]$evidencia) { Paso $id $desc $(if ($ok) { 'OK' } else { 'FALLA' }) $evidencia }

# ---------------------------------------------------------------- ensayo
function Ejecutar-Ensayo {
    Assert-Clon $BaseClon
    if (-not [Environment]::GetEnvironmentVariable('P03_ADMIN_PASSWORD')) { throw 'Falta P03_ADMIN_PASSWORD (contrasena actual de admin en el clon).' }
    if (-not (Test-Path (Join-Path $DirectorioBuild 'TheBuryProyect.dll'))) { throw "No existe build en $DirectorioBuild (dotnet build -o)." }
    if (Get-NetTCPConnection -LocalPort $Puerto -State Listen -ErrorAction SilentlyContinue) { throw "El puerto $Puerto esta ocupado." }
    if ((Get-Sql "SELECT DB_NAME()")[0].Trim() -ine $BaseClon) { throw 'La base consultada no es el clon.' }

    $claveAdmin = [Environment]::GetEnvironmentVariable('P03_ADMIN_PASSWORD')
    $claveOwner = Clave 'P03_PWD_OWNER'; $claveRec = Clave 'P03_PWD_RECOVERY'; $claveAdminNueva = Clave 'P03_PWD_ADMIN_NUEVA'; $claveAdminRec = Nueva-Clave
    $owner = 'p03.propietario'; $rec = 'p03.recuperacion'

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = "exec `"$(Join-Path $DirectorioBuild 'TheBuryProyect.dll')`" --contentRoot `"$raiz`" --urls $script:base"
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = $Entorno
    $psi.EnvironmentVariables['ConnectionStrings__DefaultConnection'] = $cadena
    $psi.EnvironmentVariables['Database__InitializeOnStartup'] = 'false'   # no migrar ni sembrar: se ensaya el estado tal cual
    $psi.EnvironmentVariables['Logging__LogLevel__Default'] = 'Warning'
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $null = $proc.StandardOutput.ReadToEndAsync(); $null = $proc.StandardError.ReadToEndAsync()
    Write-Host "App propia PID $($proc.Id) entorno=$Entorno puerto=$Puerto"
    try {
        $listo = $false
        1..60 | ForEach-Object { if (-not $listo) { try { $x = Invoke-WebRequest "$script:base/Identity/Account/Login" -UseBasicParsing -TimeoutSec 3; $listo = $x.StatusCode -eq 200 } catch { Start-Sleep -Seconds 2 } } }
        if (-not $listo) { throw 'La app no respondio.' }

        $antes = Get-Sql "SELECT UserName, Activo FROM AspNetUsers ORDER BY UserName"
        Paso '0' 'Estado inicial del clon' 'OK' ("usuarios=$($antes.Count); SuperAdmin activos=$(SuperAdmins-Activos); entorno=$Entorno")

        # --- previo: sesion A de admin (la que debe perder acceso)
        $sA = Nueva-Sesion 'admin-previa'
        $okA = Login $sA 'admin' $claveAdmin
        if ($okA -and (Terminos-Pendientes $sA)) { Aceptar-Terminos $sA 'Administrador Ensayo' | Out-Null }
        Verificar '3.pre' 'Sesion previa de admin abierta con acceso a Seguridad' ($okA -and (Acceso $sA) -eq 'OK') (Acceso $sA)
        if (-not $okA) { throw 'No se pudo iniciar sesion con admin en el clon (revisar P03_ADMIN_PASSWORD).' }

        # --- Paso 1: SuperAdmin principal
        $corta = Post-Accion $sA '/Usuarios/Create' @{ UserName = 'p03.corta'; Email = 'p03.corta@ensayo.invalid'; Password = 'Ab1'; ConfirmPassword = 'Ab1'; RolesSeleccionados = 'SuperAdmin'; Activo = 'true'; EmailConfirmed = 'true' }
        Verificar '1.0' 'Contrasena demasiado corta rechazada' ($corta.Body -notmatch '"success"\s*:\s*true' -and -not (Get-Sql "SELECT 1 FROM AspNetUsers WHERE UserName='p03.corta'").Count) 'alta con 3 caracteres no creo usuario'
        Verificar '1.1' 'Crear SuperAdmin principal desde Seguridad->Usuarios' (Crear-SuperAdmin $sA $owner $claveOwner 'Dueno') ("existe=" + [bool](Get-Sql "SELECT 1 FROM AspNetUsers WHERE UserName='$owner'").Count + "; resp=" + $script:ultimoCuerpo)
        $sO = Nueva-Sesion 'propietario'
        $okO = Login $sO $owner $claveOwner
        $pend = Terminos-Pendientes $sO
        Verificar '1.2' 'Login independiente del principal; Terminos pendientes antes de operar' ($okO -and $pend) "login=$okO; terminosPendientes=$pend"
        $acepto = Aceptar-Terminos $sO 'Propietario Ensayo'
        Verificar '1.3' 'Aceptacion de Terminos y acceso a Seguridad' ($acepto -and (Acceso $sO) -eq 'OK') ("aceptaciones en BD=" + (Get-Sql "SELECT COUNT(*) FROM TerminosCondicionesAceptaciones a JOIN AspNetUsers u ON u.Id=a.UsuarioId WHERE u.UserName='$owner'")[0])

        # --- Paso 3 (runbook): SuperAdmin de recuperacion creado con el principal
        Verificar '1.4' 'Crear SuperAdmin de recuperacion (con la cuenta propia)' (Crear-SuperAdmin $sO $rec $claveRec 'Resguardo') ("existe=" + [bool](Get-Sql "SELECT 1 FROM AspNetUsers WHERE UserName='$rec'").Count)
        $sR = Nueva-Sesion 'recuperacion'
        $okR = Login $sR $rec $claveRec
        $acR = $okR -and (Terminos-Pendientes $sR) -and (Aceptar-Terminos $sR 'Recuperacion Ensayo')
        Verificar '1.5' 'Login independiente + Terminos del SuperAdmin de recuperacion' ($okR -and $acR -and (Acceso $sR) -eq 'OK') "login=$okR; terminos=$acR"

        # --- Paso 2: rotar admin, desactivar administrador y demo
        Verificar '2.1' 'Rotar contrasena de admin (con la cuenta propia)' (Cambiar-Clave $sO 'admin' $claveAdminNueva) 'POST /Usuarios/CambiarPassword'
        $accA = Acceso $sA
        Verificar '3.1' 'Sesion previa de admin pierde acceso en el siguiente request tras rotar' ($accA -ne 'OK') $accA
        $viejo = Login (Nueva-Sesion 'admin-vieja') 'admin' $claveAdmin
        $nuevo = Login (Nueva-Sesion 'admin-nueva') 'admin' $claveAdminNueva
        Verificar '2.2' 'La clave anterior de admin ya no ingresa y la nueva si' ((-not $viejo) -and $nuevo) "anterior=$viejo; nueva=$nuevo"

        # sesion de admin con la clave nueva, para probar revocacion por desactivacion
        $sA2 = Nueva-Sesion 'admin-nueva-sesion'
        Login $sA2 'admin' $claveAdminNueva | Out-Null
        if (Terminos-Pendientes $sA2) { Aceptar-Terminos $sA2 'Administrador Ensayo' | Out-Null }
        $previo = Acceso $sA2
        Post-Accion $sO '/Usuarios/Desactivar' @{ id = (Id-De 'admin'); returnUrl = '/Seguridad' } | Out-Null
        $accA2 = Acceso $sA2
        Verificar '3.2' 'Desactivar admin: su sesion abierta cae en el siguiente request' ($previo -eq 'OK' -and -not (Activo-De 'admin') -and $accA2 -ne 'OK') "antes=$previo; despues=$accA2; Activo=$(Activo-De 'admin')"
        Verificar '3.3' 'admin desactivado no puede iniciar sesion' (-not (Login (Nueva-Sesion 'x') 'admin' $claveAdminNueva)) 'login rechazado'

        $falla = @()
        foreach ($u in $CuentasDemoADesactivar) {
            if (-not (Get-Sql "SELECT 1 FROM AspNetUsers WHERE UserName='$u'").Count) { continue }
            Post-Accion $sO '/Usuarios/Desactivar' @{ id = (Id-De $u); returnUrl = '/Seguridad' } | Out-Null
            if (Activo-De $u) { $falla += $u }
        }
        Verificar '2.3' 'Desactivar administrador y cuentas demo' ($falla.Count -eq 0) ("pendientes activas=" + ($falla -join ',') + "; demo evaluadas=" + ($CuentasDemoADesactivar -join ','))
        $quedan = Get-Sql "SELECT UserName FROM AspNetUsers WHERE Activo=1 ORDER BY UserName"
        Paso '2.4' 'Cuentas que siguen activas (decidir con el propietario: no demo)' 'INFO' ($quedan -join ', ')
        Verificar '2.5' 'Siguen existiendo >= 2 SuperAdmin activos' ((SuperAdmins-Activos) -ge 2) "SuperAdmin activos=$(SuperAdmins-Activos)"

        # --- Paso 4: ultimo SuperAdmin (activos ahora: propietario y recuperacion)
        Post-Accion $sR '/Usuarios/Desactivar' @{ id = (Id-De $owner); returnUrl = '/Seguridad' } | Out-Null
        Verificar '4.1' 'Con 2 SuperAdmin se puede desactivar uno (queda 1)' ((-not (Activo-De $owner)) -and (SuperAdmins-Activos) -eq 1) "SuperAdmin activos=$(SuperAdmins-Activos)"
        Verificar '3.4' 'Sesion del propietario desactivado cae en el siguiente request' ((Acceso $sO) -ne 'OK') (Acceso $sO)
        Post-Accion $sR '/Usuarios/Desactivar' @{ id = (Id-De $rec); returnUrl = '/Seguridad' } | Out-Null
        Verificar '4.2' 'Desactivar al ultimo SuperAdmin es rechazado' ((Activo-De $rec) -and (SuperAdmins-Activos) -eq 1) "recuperacion Activo=$(Activo-De $rec); SuperAdmin activos=$(SuperAdmins-Activos)"
        Post-Accion $sR '/Usuarios/Delete' @{ id = (Id-De $rec); motivo = 'ensayo P-03'; returnUrl = '/Seguridad' } | Out-Null
        $existeRec = [bool](Get-Sql "SELECT 1 FROM AspNetUsers WHERE UserName='$rec' AND Activo=1").Count
        Verificar '4.3' 'Eliminar al ultimo SuperAdmin es rechazado' ($existeRec -and (SuperAdmins-Activos) -eq 1) "recuperacion activa=$existeRec; SuperAdmin activos=$(SuperAdmins-Activos)"

        # --- Paso 5: recuperacion
        Post-Accion $sR '/Usuarios/Reactivar' @{ id = (Id-De $owner); returnUrl = '/Seguridad' } | Out-Null
        $sO2 = Nueva-Sesion 'propietario-2'
        $okO2 = Login $sO2 $owner $claveOwner
        Verificar '5.1' 'Con la cuenta de recuperacion se reactiva al propietario y este vuelve a ingresar' ((Activo-De $owner) -and $okO2 -and (Acceso $sO2) -eq 'OK') "Activo=$(Activo-De $owner); login=$okO2; acceso=$(Acceso $sO2)"
        Post-Accion $sR '/Usuarios/Reactivar' @{ id = (Id-De 'admin'); returnUrl = '/Seguridad' } | Out-Null
        $rst = Cambiar-Clave $sR 'admin' $claveAdminRec
        $okAdm = Login (Nueva-Sesion 'admin-rec') 'admin' $claveAdminRec
        Verificar '5.2' 'Recuperacion reactiva admin y le resetea la contrasena (ingreso con la nueva)' ((Activo-De 'admin') -and $rst -and $okAdm) "Activo=$(Activo-De 'admin'); reset=$rst; login=$okAdm"
        Post-Accion $sR '/Usuarios/Desactivar' @{ id = (Id-De 'admin'); returnUrl = '/Seguridad' } | Out-Null
        Verificar '5.3' 'admin vuelve a desactivarse; >= 2 SuperAdmin activos' ((-not (Activo-De 'admin')) -and (SuperAdmins-Activos) -ge 2) "admin Activo=$(Activo-De 'admin'); SuperAdmin activos=$(SuperAdmins-Activos)"

        # --- Paso 6: Diagnostico
        $dg = Send $sO2 'GET' '/Diagnostico' $null
        if ($Entorno -eq 'Development') {
            Paso '6' 'Diagnostico fuera de Production' 'NO VALIDADO' "Entorno=Development: /Diagnostico respondio $($dg.Status) (esperado en Development). El 404 solo en Production se cubre por DiagnosticoFueraDeDevelopmentHttpTests."
        } else {
            Paso '6' "/Diagnostico en entorno $Entorno (no Production)" $(if ($dg.Status -eq 404) { 'OK' } else { 'FALLA' }) "status=$($dg.Status) para SuperAdmin autenticado. Production real no ejecutable en LocalDB (exige ErpDb:User/Password): parcial."
        }
        Paso '5.4' 'Estado final (SELECT UserName, Activo FROM AspNetUsers)' 'INFO' ((Get-Sql "SELECT UserName, Activo FROM AspNetUsers ORDER BY UserName") -join ' ; ')
    }
    finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Host "App propia (PID $($proc.Id)) detenida." }
    }
}

# ---------------------------------------------------------------- main
try {
    if ($CrearClon) { Nuevo-Clon }
    if ($Ejecutar) {
        Ejecutar-Ensayo
        Write-Host "`n==== RESUMEN ===="
        $script:res | Format-Table Paso, Resultado, Descripcion -AutoSize | Out-String | Write-Host
        if ($SalidaJson) { $script:res | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -LiteralPath $SalidaJson }
        if (@($script:res | Where-Object Resultado -eq 'FALLA').Count) { $script:codigo = 1 }
    }
}
finally {
    if ($Limpiar) { Quitar-Clon }
}
if ($script:codigo) { exit $script:codigo }
