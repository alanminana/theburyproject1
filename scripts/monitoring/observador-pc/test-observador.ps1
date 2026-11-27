# Pruebas de la logica del observador (sin red). Uso: powershell -NoProfile -File test-observador.ps1   (exit 0 = OK)
. (Join-Path $PSScriptRoot 'observador-logica.ps1')
$script:fails = 0
function Assert-Eq($name, $got, $want) {
    if ("$got" -eq "$want") { Write-Output "PASS $name" } else { Write-Output "FAIL $name : obtenido [$got] esperado [$want]"; $script:fails++ }
}
function New-S([bool]$net = $true, [bool]$hostOk = $true, [bool]$erp = $true, [string]$line = 'ac=1 cap=90 status=Charging monitor=active uptime_s=10') {
    @{ netOk = $net; hostOk = $hostOk; erpOk = $erp; power = (ConvertFrom-PowerLine $line) }
}
function Invoke-Seq($state, [object[]]$samples) {
    $codes = @()
    foreach ($s in $samples) { foreach ($e in @(Update-ObsState $state $s)) { if ($e) { $codes += $e.code } } }
    return ($codes -join ',')
}
function Bat([int]$cap) { New-S -line "ac=0 cap=$cap status=Discharging monitor=active uptime_s=10" }

$st = New-ObsState
Assert-Eq 'todo normal: sin eventos' (Invoke-Seq $st @((New-S), (New-S), (New-S))) ''
$seq = @((Bat 90), (Bat 89), (Bat 60), (Bat 30), (Bat 29), (Bat 16), (Bat 15), (Bat 9), (Bat 9), (Bat 9))
Assert-Eq 'corte, 30 y 15: un evento cada uno' (Invoke-Seq $st $seq) 'CORRIENTE_PERDIDA,BATERIA_30,BATERIA_15'
$back = New-S -line 'ac=1 cap=9 status=Charging monitor=active uptime_s=10'
Assert-Eq 'restablecimiento una sola vez' (Invoke-Seq $st @($back, $back, $back)) 'CORRIENTE_RESTABLECIDA'
Assert-Eq 'segundo corte vuelve a avisar' (Invoke-Seq $st @((Bat 80), (Bat 80), (Bat 30))) 'CORRIENTE_PERDIDA,BATERIA_30'

$st = New-ObsState
Assert-Eq 'microcorte de una muestra ignorado' (Invoke-Seq $st @((Bat 80), (New-S), (Bat 80), (New-S))) ''
$st = New-ObsState
Assert-Eq 'corte ya con bateria baja: solo 15' (Invoke-Seq $st @((Bat 12), (Bat 11))) 'CORRIENTE_PERDIDA,BATERIA_15'
$st = New-ObsState
$low = New-S -line 'ac=1 cap=5 status=Charging monitor=active uptime_s=10'
Assert-Eq 'bateria baja pero cargando: sin aviso' (Invoke-Seq $st @($low, $low, $low)) ''
$st = New-ObsState
$bad = New-S -line 'ac=1 cap=70 status=Discharging monitor=active uptime_s=10'
Assert-Eq 'cargador conectado que no entrega' (Invoke-Seq $st @($bad, $bad)) 'CORRIENTE_PERDIDA'

$st = New-ObsState
$down = New-S -hostOk $false -erp $false -line ''
Assert-Eq 'dos fallos no bastan' (Invoke-Seq $st @($down, $down)) ''
Assert-Eq 'tercer fallo: caida, una vez' (Invoke-Seq $st @($down, $down, $down)) 'EQUIPO_CAIDO'
Assert-Eq 'vuelta del equipo' (Invoke-Seq $st @((New-S), (New-S))) 'EQUIPO_RECUPERADO'

$st = New-ObsState
[void](Invoke-Seq $st @((Bat 3), (Bat 3)))
$ev = @(); 1..3 | ForEach-Object { $ev += @(Update-ObsState $st $down) }
Assert-Eq 'caida tras bateria: lo dice' ([bool](($ev | Where-Object { $_.code -eq 'EQUIPO_CAIDO' }).text -match 'sin carga')) 'True'

$st = New-ObsState
$blind = New-S -net $false -hostOk $false -erp $false -line ''
Assert-Eq 'PC sin red: no culpa a la notebook' (Invoke-Seq $st @($blind, $blind, $blind, $blind, $blind)) 'PC_SIN_RED'
Assert-Eq 'vuelve la red' (Invoke-Seq $st @((New-S))) 'PC_RED_OK'

$st = New-ObsState
$erpDown = New-S -erp $false
Assert-Eq 'ERP caido y recuperado' (Invoke-Seq $st @($erpDown, $erpDown, $erpDown, $erpDown, (New-S))) 'ERP_CAIDO,ERP_RECUPERADO'
$st = New-ObsState
$nopow = New-S -line 'basura'
Assert-Eq 'sin lectura de energia avisa una vez' (Invoke-Seq $st @($nopow, $nopow, $nopow, $nopow, $nopow, $nopow, $nopow)) 'ENERGIA_SIN_LECTURA'
$st = New-ObsState
$mon = New-S -line 'ac=1 cap=90 status=Charging monitor=failed uptime_s=10'
Assert-Eq 'monitor detenido avisa una vez' (Invoke-Seq $st @($mon, $mon, $mon, $mon)) 'MONITOR_DETENIDO'
Assert-Eq 'linea de energia invalida se descarta' ($null -eq (ConvertFrom-PowerLine 'ac=2 cap=x')) 'True'

Assert-Eq 'lectura sin datos se descarta' ($null -eq (ConvertFrom-PowerLine 'ac=na cap=na status=na monitor=active uptime_s=1')) 'True'
Assert-Eq 'sin fuente ni estado conocido se descarta' ($null -eq (ConvertFrom-PowerLine 'ac=na cap=50 status=Unknown monitor=active uptime_s=1')) 'True'
$st = New-ObsState
[void](Invoke-Seq $st @((Bat 25), (Bat 25)))
$na = New-S -line 'ac=na cap=na status=na monitor=active uptime_s=1'
Assert-Eq 'lectura vacia no "restablece" un corte' (Invoke-Seq $st @($na, $na, $na, $na, $na)) 'ENERGIA_SIN_LECTURA'
Assert-Eq 'el corte sigue abierto' $st.ac 'lost'

# ---- destino de respaldos en la PC
$lim = @{ maxMinutosLogs = 45; maxHorasArchivos = 27; muestrasAtraso = 3 }
function New-D([bool]$ok = $true, $log = 10, $file = 5, [double]$used = 1, [double]$free = 700) {
    [pscustomobject]@{ ok = $ok; reason = 'prueba'; logAgeMin = $log; fileAgeHours = $file; usedGB = $used; capGB = 50.0; freeGB = $free }
}
function Invoke-Dest($state, [object[]]$list, [bool]$expect = $true) {
    $codes = @()
    foreach ($x in $list) { foreach ($e in @(Update-ObsDestState $state $x $expect $lim)) { if ($e) { $codes += $e.code } } }
    return ($codes -join ',')
}
$st = New-ObsState
Assert-Eq 'destino sano: sin eventos' (Invoke-Dest $st @((New-D), (New-D), (New-D))) ''
$off = New-D -ok $false
Assert-Eq 'servicio caido avisa una sola vez' (Invoke-Dest $st @($off, $off, $off, $off)) 'DESTINO_CAIDO'
Assert-Eq 'vuelta del destino' (Invoke-Dest $st @((New-D), (New-D))) 'DESTINO_DISPONIBLE'
$st = New-ObsState
Assert-Eq 'un fallo aislado del destino se ignora' (Invoke-Dest $st @($off, (New-D), $off, (New-D))) ''
$st = New-ObsState
$late = New-D -log 50
Assert-Eq 'copias de base atrasadas, una vez' (Invoke-Dest $st @($late, $late, $late, $late, $late)) 'COPIAS_ATRASADAS'
Assert-Eq 'copias de base al dia' (Invoke-Dest $st @((New-D), (New-D))) 'COPIAS_AL_DIA'
$st = New-ObsState
$none = New-D -log $null -file $null
Assert-Eq 'sin ninguna copia avisa de ambas' (Invoke-Dest $st @($none, $none, $none, $none)) 'COPIAS_ATRASADAS,COPIA_DIARIA_ATRASADA'
$st = New-ObsState
$daily = New-D -file 30
Assert-Eq 'falta la copia diaria' (Invoke-Dest $st @($daily, $daily, $daily)) 'COPIA_DIARIA_ATRASADA'
$st = New-ObsState
Assert-Eq 'notebook caida: no suma avisos de atraso' (Invoke-Dest $st @($late, $late, $late, $late) $false) ''
$st = New-ObsState
Assert-Eq 'destino caido aun con la notebook caida' (Invoke-Dest $st @($off, $off) $false) 'DESTINO_CAIDO'
$st = New-ObsState
Assert-Eq 'espacio 80 % avisa una vez' (Invoke-Dest $st @((New-D -used 41), (New-D -used 42), (New-D -used 39))) 'ESPACIO_ALTO'
Assert-Eq 'espacio 95 % es critico' (Invoke-Dest $st @((New-D -used 48), (New-D -used 48))) 'ESPACIO_CRITICO'
Assert-Eq 'vuelve a ok solo bajo 75 %' (Invoke-Dest $st @((New-D -used 38), (New-D -used 30))) 'ESPACIO_OK'
$st = New-ObsState
Assert-Eq 'disco casi lleno es critico' (Invoke-Dest $st @((New-D -free 3))) 'ESPACIO_CRITICO'
$st = New-ObsState
Assert-Eq 'sin destino configurado no hay eventos' (@(Update-ObsDestState $st $null $true $lim).Count) 0

# ---- antiguedad medida sobre archivos reales: solo cuenta la marca de copia verificada
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("bury-obs-" + [guid]::NewGuid().ToString('N'))
try {
    $now = [DateTime]::UtcNow
    New-Item -ItemType Directory -Force (Join-Path $tmp 'sql') | Out-Null
    $aux = Join-Path $tmp 'sql\recien-subido.sha256'; Set-Content -LiteralPath $aux -Value 'x'
    Assert-Eq 'un archivo reciente sin marca NO es una copia' ($null -eq (Get-ObsMarkerAge $tmp 'sql' $now)) 'True'
    New-Item -ItemType Directory -Force (Join-Path $tmp '_ok-sql') | Out-Null
    Assert-Eq 'carpeta de marca vacia: sin copia' ($null -eq (Get-ObsMarkerAge $tmp 'sql' $now)) 'True'
    $mark = Join-Path $tmp '_ok-sql\marca'; Set-Content -LiteralPath $mark -Value ''
    (Get-Item -LiteralPath $mark).LastWriteTimeUtc = $now.AddMinutes(-90)
    Assert-Eq 'manda la marca (90 min), no el archivo nuevo' ([int](Get-ObsMarkerAge $tmp 'sql' $now)) 90
    Assert-Eq 'los tipos son independientes' ($null -eq (Get-ObsMarkerAge $tmp 'files' $now)) 'True'
} finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force } }

if ($script:fails) { Write-Output "$($script:fails) FALLA(S)"; exit 1 }
Write-Output 'OK'
