# Logica PURA del observador externo (sin red ni notificaciones): recibe el estado anterior y una muestra, devuelve los eventos nuevos.
# Un evento por cada cambio de estado: nunca se repite mientras la condicion sigue igual. Solo ASCII (PowerShell 5.1).

function New-ObsState {
    [ordered]@{
        blind = $false
        host = 'up'; hostFails = 0
        erp = 'up'; erpFails = 0
        ac = 'ok'; acFails = 0; bat30 = $false; bat15 = $false
        lastCap = $null; lastOnBattery = $false
        powerReadFails = 0; powerReadAlerted = $false
        monitorFails = 0; monitorAlerted = $false
    }
}

function New-ObsEvent([string]$code, [string]$level, [string]$text) {
    [pscustomobject]@{ code = $code; level = $level; text = $text }
}

function ConvertFrom-PowerLine([string]$line) {
    if (-not $line) { return $null }
    if ($line -notmatch '^ac=(1|0|na) cap=(\d{1,3}|na) status=(\S+) monitor=(\S+)') { return $null }
    $cap = $null
    if ($Matches[2] -ne 'na') { $cap = [int]$Matches[2] }
    # Sin carga legible, o sin saber ni la fuente ni el estado, NO es una lectura: nunca debe parecer "volvio la corriente".
    if ($null -eq $cap -or $cap -gt 100 -or ($Matches[1] -eq 'na' -and ($Matches[3] -eq 'na' -or $Matches[3] -eq 'Unknown'))) { return $null }
    [pscustomobject]@{ ac = $Matches[1]; cap = $cap; status = $Matches[3]; monitor = $Matches[4] }
}

# $sample: netOk, hostOk, erpOk (bool) y power (resultado de ConvertFrom-PowerLine o $null)
function Update-ObsState($state, $sample) {
    $events = @()

    # 0) si la PC no tiene red no se puede afirmar nada de la notebook
    if (-not $sample.netOk) {
        if (-not $state.blind) {
            $state.blind = $true
            $events += New-ObsEvent 'PC_SIN_RED' 'aviso' 'Esta PC no llega al router: el observador no puede ver la notebook.'
        }
        return $events
    }
    if ($state.blind) {
        $state.blind = $false
        $events += New-ObsEvent 'PC_RED_OK' 'info' 'La PC recupero la red: el observador vuelve a mirar la notebook.'
    }

    # 1) equipo completo
    if (-not $sample.hostOk) {
        $state.hostFails++
        if ($state.host -eq 'up' -and $state.hostFails -ge 3) {
            $state.host = 'down'
            $extra = ''
            if ($state.lastOnBattery) { $extra = " Ultimo dato: funcionaba a bateria ($($state.lastCap) %): probablemente se quedo sin carga." }
            $events += New-ObsEvent 'EQUIPO_CAIDO' 'critico' "La notebook no responde (3 intentos seguidos). Revisar alimentacion, encendido y Wi-Fi.$extra"
        }
        return $events
    }
    $state.hostFails = 0
    if ($state.host -eq 'down') {
        $state.host = 'up'
        $events += New-ObsEvent 'EQUIPO_RECUPERADO' 'info' 'La notebook volvio a responder.'
    }

    # 2) ERP por HTTPS
    if (-not $sample.erpOk) {
        $state.erpFails++
        if ($state.erp -eq 'up' -and $state.erpFails -ge 3) {
            $state.erp = 'down'
            $events += New-ObsEvent 'ERP_CAIDO' 'critico' 'La notebook responde pero el ERP no (3 intentos de /health/ready).'
        }
    } else {
        $state.erpFails = 0
        if ($state.erp -eq 'down') {
            $state.erp = 'up'
            $events += New-ObsEvent 'ERP_RECUPERADO' 'info' 'El ERP volvio a responder.'
        }
    }

    # 3) energia
    $p = $sample.power
    if ($null -eq $p) {
        $state.powerReadFails++
        if ($state.powerReadFails -ge 5 -and -not $state.powerReadAlerted) {
            $state.powerReadAlerted = $true
            $events += New-ObsEvent 'ENERGIA_SIN_LECTURA' 'aviso' 'No se puede leer el estado de energia de la notebook (5 intentos). La vigilancia de bateria no esta activa.'
        }
        return $events
    }
    if ($state.powerReadAlerted) { $events += New-ObsEvent 'ENERGIA_LECTURA_OK' 'info' 'Se recupero la lectura de energia.' }
    $state.powerReadFails = 0
    $state.powerReadAlerted = $false

    if ($p.monitor -ne 'active') {
        $state.monitorFails++
        if ($state.monitorFails -ge 3 -and -not $state.monitorAlerted) {
            $state.monitorAlerted = $true
            $events += New-ObsEvent 'MONITOR_DETENIDO' 'aviso' "El monitor del servidor no esta activo (estado: $($p.monitor))."
        }
    } else {
        if ($state.monitorAlerted) { $events += New-ObsEvent 'MONITOR_OK' 'info' 'El monitor del servidor volvio a estar activo.' }
        $state.monitorFails = 0
        $state.monitorAlerted = $false
    }

    $onBattery = ($p.ac -eq '0') -or ($p.status -eq 'Discharging')
    $state.lastOnBattery = $onBattery
    $state.lastCap = $p.cap
    if ($onBattery) {
        $state.acFails++
        if ($state.ac -eq 'ok' -and $state.acFails -ge 2) {
            $state.ac = 'lost'
            $events += New-ObsEvent 'CORRIENTE_PERDIDA' 'critico' "La notebook NO esta recibiendo corriente: funciona a bateria ($($p.cap) %). Revisar cargador, cable y tomacorriente."
        }
        if ($state.ac -eq 'lost' -and $null -ne $p.cap) {
            if ($p.cap -le 15) {
                if (-not $state.bat15) {
                    $state.bat15 = $true
                    $state.bat30 = $true
                    $events += New-ObsEvent 'BATERIA_15' 'critico' "BATERIA CRITICA: $($p.cap) %. Conectar la corriente YA: la notebook se apagara sola al 2 %."
                }
            } elseif ($p.cap -le 30 -and -not $state.bat30) {
                $state.bat30 = $true
                $events += New-ObsEvent 'BATERIA_30' 'critico' "Bateria al $($p.cap) % y sin corriente."
            }
        }
    } else {
        $state.acFails = 0
        if ($state.ac -eq 'lost') {
            $state.ac = 'ok'
            $state.bat30 = $false
            $state.bat15 = $false
            $events += New-ObsEvent 'CORRIENTE_RESTABLECIDA' 'info' "Corriente restablecida (bateria $($p.cap) %, $($p.status))."
        }
    }
    return $events
}
