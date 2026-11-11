<#
  csp-observacion.ps1 - resume las violaciones CSP registradas en el log de la aplicacion durante la fase de observacion en ReportOnly.
  SOLO LECTURA: lee el/los archivo(s) de log (abiertos con FileShare.ReadWrite: se pueden leer mientras la app escribe) y escribe a pantalla.
  No toca la aplicacion, su configuracion ni su base.

  Fuente: el endpoint /csp-report de la app registra un WARN por violacion (categoria CspReport):
      2026-11-11T12:00:00Z warn: CspReport[0]
            => ... RequestPath:/csp-report ...
            CSP violation: document-uri=... | blocked-uri=... | effective-directive=... | violated-directive=... | source-file=... | line-number=... | disposition=...
  Donde cae ese log segun como corre la app (ver docs/csp-enforce-activacion.md):
    - app local (scripts\ejecutar-erp-local.ps1): <worktree de ejecucion>\bin\app.log (stdout, UTC)
    - Docker:  docker compose logs --no-color -t app > app-compose.log     (y pasar esa ruta)

  Uso:
    powershell -NoProfile -File scripts\ops\csp-observacion.ps1 -Ruta D:\git\theburyproject1-run\bin\app.log -Desde 2026-11-12 -Hasta 2026-11-25
    powershell -NoProfile -File scripts\ops\csp-observacion.ps1 -Ruta .\logs -DiasMinimos 10 -ExigirCentinela -Json

  Parametros:
    -Ruta            archivo o carpeta (en carpeta: *.log, *.err, *.txt, *.out, sin recursion). Obligatorio.
    -Desde/-Hasta    fechas (hora local de la maquina, ambas inclusivas). Los timestamps del log son UTC y se convierten a hora local.
    -MaxPropias      umbral de violaciones propias aceptadas (defecto 0).
    -DiasMinimos     exige N dias calendario (locales) distintos con al menos una linea de log dentro del rango (proxy de dias con la app en uso).
    -ExigirCentinela exige haber visto la violacion centinela (blocked-uri con host csp-centinela.invalid): prueba que navegador -> /csp-report -> log funciona.
    -RuidoExtra      regex adicional (sobre blocked-uri y source-file) para ruido propio del negocio; usar solo con justificacion documentada.
    -Json            ademas del resumen, imprime un objeto JSON con los totales.

  Codigos de salida (un resultado vacio o dudoso NUNCA da verde):
    0  medicion valida y violaciones propias <= -MaxPropias (por defecto 0)
    1  hay mas violaciones propias que -MaxPropias
    2  uso incorrecto (ruta inexistente, fechas invalidas)
    3  medicion INVALIDA: sin archivos legibles, ninguna linea de log dentro del rango, menos dias con actividad que -DiasMinimos,
       o falta el centinela con -ExigirCentinela.
  Limites conocidos: /csp-report tiene rate limit de 30/min por IP (lo que lo excede da 429 y NO queda en el log): los conteos son una
  cota inferior; basta UNA violacion propia para no pasar. Un reporte que no se puede interpretar cuenta como propio.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Ruta,
    [Nullable[datetime]]$Desde = $null,
    [Nullable[datetime]]$Hasta = $null,
    [int]$MaxPropias = 0,
    [int]$DiasMinimos = 0,
    [switch]$ExigirCentinela,
    [string]$RuidoExtra = '',
    [switch]$Json
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Ruta)) { Write-Host "No existe la ruta: $Ruta"; exit 2 }
if ($MaxPropias -lt 0 -or $DiasMinimos -lt 0) { Write-Host 'MaxPropias/DiasMinimos no pueden ser negativos'; exit 2 }
$d0 = $null; $d1 = $null
if ($Desde) { $d0 = ([datetime]$Desde).Date }
if ($Hasta) { $d1 = ([datetime]$Hasta).Date.AddDays(1) }   # exclusivo
if ($d0 -and $d1 -and $d1 -le $d0) { Write-Host '-Hasta anterior a -Desde'; exit 2 }
$reExtra = $null
if ($RuidoExtra) { try { $reExtra = [regex]::new($RuidoExtra, 'IgnoreCase') } catch { Write-Host "RuidoExtra no es una regex valida: $_"; exit 2 } }

$item = Get-Item -LiteralPath $Ruta
if ($item.PSIsContainer) {
    $archivos = @(Get-ChildItem -LiteralPath $Ruta -File | Where-Object { $_.Extension -in '.log', '.err', '.txt', '.out' } | Sort-Object Name)
} else { $archivos = @($item) }
if ($archivos.Count -eq 0) { Write-Host 'MEDICION INVALIDA: no hay archivos de log en la ruta.'; exit 3 }

$esquemasRuido = '^(chrome-extension|moz-extension|safari-extension|safari-web-extension|ms-browser-extension|webkit-masked-url|about|chrome|chrome-search|chrome-error|resource|edge|opera|vivaldi|brave|view-source)(:|$)'
$reTs = [regex]'(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.\d+)?Z\s+(?:trce|dbug|info|warn|fail|crit):'
$reTsLinea = [regex]'^(?:\S+\s+\|\s+)?(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.\d+)?Z'
$reCampo = [regex]'(?:^|\| )(document-uri|blocked-uri|effective-directive|violated-directive|source-file|line-number|disposition)=(.*?)(?= \| (?:document-uri|blocked-uri|effective-directive|violated-directive|source-file|line-number|disposition)=|$)'

function ConvertirFecha([string]$s) {
    $dt = [datetime]::ParseExact($s, 'yyyy-MM-ddTHH:mm:ss', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
    return $dt.ToLocalTime()
}
function NormalizarRuta([string]$u) {
    if (-not $u) { return '(vacio)' }
    $p = $u
    if ($u -match '^[a-z][a-z0-9+.-]*://[^/]+(/[^?#]*)') { $p = $Matches[1] } elseif ($u -match '^[a-z][a-z0-9+.-]*://[^/]+') { $p = '/' }
    $p = $p -replace '/\d+(?=/|$)', '/{id}' -replace '/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?=/|$)', '/{guid}'
    return $p
}
function NormalizarBloqueado([string]$b) {
    if (-not $b) { return '(vacio)' }
    if ($b -match '^([a-z][a-z0-9+.-]*://[^/?#]+)(/[^?#]*)?') { return ($Matches[1] + $Matches[2]) }
    return ($b -replace '^(data|blob):.*', '$1:')
}
function Clasificar($r) {
    $b = ($r['blocked-uri'] + '').Trim(); $sf = ($r['source-file'] + '').Trim(); $du = ($r['document-uri'] + '').Trim()
    $dir = (($r['effective-directive'] + '').Trim()); if (-not $dir) { $dir = (($r['violated-directive'] + '').Trim() -split '\s+')[0] }
    if ($b -match 'csp-centinela\.invalid') { return @('centinela', 'violacion provocada a proposito (prueba del circuito)') }
    if ($b -match $esquemasRuido) { return @('ruido', 'extension/navegador (blocked-uri)') }
    if ($sf -match $esquemasRuido) { return @('ruido', 'extension/navegador (source-file)') }
    if ($du -match $esquemasRuido) { return @('ruido', 'documento no propio (about:/chrome-error:/view-source:)') }
    if ($b -match '^data(:|$)' -and $dir -notmatch '^(img-src|font-src)') { return @('ruido', 'data: fuera de img-src/font-src') }
    if ($reExtra -and ($reExtra.IsMatch($b) -or $reExtra.IsMatch($sf))) { return @('ruido', 'RuidoExtra (regex del operador)') }
    return @('propia', '')
}

$total = 0; $lineasEnRango = 0; $lineasTotal = 0; $sinFecha = 0; $noParseables = 0
$dias = @{}                       # dias locales con alguna linea de log en el rango
$prop = New-Object System.Collections.ArrayList
$ruido = @{}; $centinela = 0
$minTs = $null; $maxTs = $null

foreach ($f in $archivos) {
    $fs = [System.IO.File]::Open($f.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    try {
        $ultimoTs = $null
        while ($null -ne ($linea = $sr.ReadLine())) {
            $lineasTotal++
            $m = $reTs.Match($linea)
            $tsLinea = $null
            if ($m.Success) { $ultimoTs = $m.Groups[1].Value; $tsLinea = $ultimoTs }
            else { $m2 = $reTsLinea.Match($linea); if ($m2.Success) { $tsLinea = $m2.Groups[1].Value } }
            $local = $null
            if ($tsLinea) { try { $local = ConvertirFecha $tsLinea } catch { $local = $null } }
            if ($local) {
                if ($d0 -and $local -lt $d0) { continue }
                if ($d1 -and $local -ge $d1) { continue }
                $lineasEnRango++; $dias[$local.ToString('yyyy-MM-dd')] = 1
                if (-not $minTs -or $local -lt $minTs) { $minTs = $local }
                if (-not $maxTs -or $local -gt $maxTs) { $maxTs = $local }
            }
            $i = $linea.IndexOf('CSP violation:')
            if ($i -lt 0) { continue }
            # fecha de la violacion: la de su linea (docker -t) o la de la ultima cabecera
            if (-not $local -and $ultimoTs) { try { $local = ConvertirFecha $ultimoTs } catch { } }
            if ($local) {
                if ($d0 -and $local -lt $d0) { continue }
                if ($d1 -and $local -ge $d1) { continue }
                if (-not $tsLinea) { $lineasEnRango++; $dias[$local.ToString('yyyy-MM-dd')] = 1 }
            } else { $sinFecha++ }
            $cuerpo = $linea.Substring($i + 'CSP violation:'.Length).Trim()
            $r = @{}
            foreach ($cm in $reCampo.Matches($cuerpo)) { $r[$cm.Groups[1].Value] = $cm.Groups[2].Value.Trim() }
            $total++
            if (-not ($r.ContainsKey('blocked-uri') -or $r.ContainsKey('effective-directive') -or $r.ContainsKey('violated-directive'))) {
                $noParseables++
                [void]$prop.Add([pscustomobject]@{ Dia = $(if ($local) { $local.ToString('yyyy-MM-dd') } else { '?' }); Directiva = '(no parseable)'; Bloqueado = $cuerpo.Substring(0, [Math]::Min(80, $cuerpo.Length)); Pagina = '?'; Disp = '?' })
                continue
            }
            $dirV = ($r['effective-directive'] + ''); if (-not $dirV) { $dirV = (($r['violated-directive'] + '') -split '\s+')[0] }
            $cl = Clasificar $r
            if ($cl[0] -eq 'centinela') { $centinela++; continue }
            if ($cl[0] -eq 'ruido') { $k = "$($cl[1]) :: $dirV"; $ruido[$k] = 1 + [int]$ruido[$k]; continue }
            [void]$prop.Add([pscustomobject]@{
                Dia = $(if ($local) { $local.ToString('yyyy-MM-dd') } else { '?' })
                Directiva = $dirV
                Bloqueado = (NormalizarBloqueado ($r['blocked-uri'] + ''))
                Pagina = (NormalizarRuta ($r['document-uri'] + ''))
                Disp = ($r['disposition'] + '')
            })
        }
    } finally { $sr.Dispose(); $fs.Dispose() }
}

$nProp = $prop.Count
$nRuido = ($ruido.Values | Measure-Object -Sum).Sum; if (-not $nRuido) { $nRuido = 0 }
Write-Host '=== Observacion CSP (solo lectura) ==='
Write-Host ("Archivos leidos : {0}" -f (($archivos | ForEach-Object { $_.FullName }) -join '; '))
Write-Host ("Rango pedido    : {0} .. {1} (hora local)" -f $(if ($d0) { $d0.ToString('yyyy-MM-dd') } else { 'inicio' }), $(if ($Hasta) { ([datetime]$Hasta).ToString('yyyy-MM-dd') } else { 'fin' }))
Write-Host ("Rango con log   : {0} .. {1}" -f $(if ($minTs) { $minTs.ToString('yyyy-MM-dd HH:mm') } else { '-' }), $(if ($maxTs) { $maxTs.ToString('yyyy-MM-dd HH:mm') } else { '-' }))
Write-Host ("Lineas de log   : {0} totales, {1} en el rango, {2} dias con actividad" -f $lineasTotal, $lineasEnRango, $dias.Count)
Write-Host ("Reportes CSP    : {0} (propias {1}, ruido {2}, centinela {3}); sin fecha {4}; no interpretables {5}" -f $total, $nProp, $nRuido, $centinela, $sinFecha, $noParseables)

if ($nProp -gt 0) {
    Write-Host "`n-- VIOLACIONES PROPIAS por directiva --"
    $prop | Group-Object Directiva | Sort-Object Count -Descending | ForEach-Object { Write-Host ("{0,6}  {1}" -f $_.Count, $_.Name) }
    Write-Host "`n-- por URI bloqueada --"
    $prop | Group-Object Bloqueado | Sort-Object Count -Descending | Select-Object -First 25 | ForEach-Object { Write-Host ("{0,6}  {1}" -f $_.Count, $_.Name) }
    Write-Host "`n-- por pagina de origen (document-uri, ids colapsados) --"
    $prop | Group-Object Pagina | Sort-Object Count -Descending | Select-Object -First 25 | ForEach-Object { Write-Host ("{0,6}  {1}" -f $_.Count, $_.Name) }
    Write-Host "`n-- combinacion directiva | bloqueado | pagina (frecuencia) --"
    $prop | Group-Object Directiva, Bloqueado, Pagina | Sort-Object Count -Descending | Select-Object -First 25 | ForEach-Object { Write-Host ("{0,6}  {1}" -f $_.Count, (($_.Group[0].Directiva, $_.Group[0].Bloqueado, $_.Group[0].Pagina) -join ' | ')) }
    Write-Host "`n-- frecuencia por dia --"
    $prop | Group-Object Dia | Sort-Object Name | ForEach-Object { Write-Host ("{0}  {1}" -f $_.Name, $_.Count) }
    Write-Host ("`n-- disposicion --`n" + (($prop | Group-Object Disp | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ', '))
}
if ($nRuido -gt 0) {
    Write-Host "`n-- RUIDO conocido (no cuenta) --"
    $ruido.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { Write-Host ("{0,6}  {1}" -f $_.Value, $_.Key) }
}

# --- veredicto ---
$invalidos = @()
if ($lineasEnRango -eq 0) { $invalidos += 'ninguna linea de log dentro del rango: no hay evidencia (app apagada, ruta equivocada o rango incorrecto)' }
if ($DiasMinimos -gt 0 -and $dias.Count -lt $DiasMinimos) { $invalidos += "solo $($dias.Count) dias con actividad de log; se exigian $DiasMinimos" }
if ($ExigirCentinela -and $centinela -eq 0) { $invalidos += 'no se vio la violacion centinela (csp-centinela.invalid): no esta probado que navegador -> /csp-report -> log funcione' }
$exit = 0; $veredicto = 'OK'
if ($invalidos.Count -gt 0) { $exit = 3; $veredicto = 'MEDICION INVALIDA' }
elseif ($nProp -gt $MaxPropias) { $exit = 1; $veredicto = 'VIOLACIONES PROPIAS' }
Write-Host ''
foreach ($x in $invalidos) { Write-Host "  - $x" }
Write-Host ("VEREDICTO: {0} (propias={1}, umbral={2}) exit {3}" -f $veredicto, $nProp, $MaxPropias, $exit)
if ($Json) {
    [pscustomobject]@{ veredicto = $veredicto; exit = $exit; propias = $nProp; ruido = $nRuido; centinela = $centinela; reportes = $total; lineasEnRango = $lineasEnRango; diasConActividad = $dias.Count; sinFecha = $sinFecha; noInterpretables = $noParseables; invalidos = $invalidos } | ConvertTo-Json -Compress
}
exit $exit
