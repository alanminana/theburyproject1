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

if ($script:fails) { Write-Output "$($script:fails) FALLA(S)"; exit 1 }
Write-Output 'OK'
