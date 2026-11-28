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
        fw = 'ok'; fwFails = 0; fwLast = ''
        destDownFails = 0; destDown = $false
        destLogFails = 0; destLogLate = $false
        destFileFails = 0; destFileLate = $false
        destSpace = 'ok'
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
    $ac = $Matches[1]; $status = $Matches[3]; $monitor = $Matches[4]
    $fw = 'na'
    if ($line -match ' fw=([a-z-]+)') { $fw = $Matches[1] }
    [pscustomobject]@{ ac = $ac; cap = $cap; status = $status; monitor = $monitor; fw = $fw }
}

# Antiguedad en minutos de la marca de "ultima copia verificada" (<destino>\_ok-<tipo>\), que backup.sh actualiza SOLO despues de verificar la
# subida. $null = no hay marca. Los archivos de sql\ y files\ no sirven para esto: una subida fallida puede dejar solo un .sha256 reciente.
function Get-ObsMarkerAge([string]$root, [string]$kind, [DateTime]$nowUtc) {
    $dir = Join-Path $root "_ok-$kind"
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $newest = @(Get-ChildItem -LiteralPath $dir -File -Force -ErrorAction Stop | Sort-Object LastWriteTimeUtc -Descending) | Select-Object -First 1
    if (-not $newest) { return $null }
    return ($nowUtc - $newest.LastWriteTimeUtc).TotalMinutes
}

# Destino de respaldos EN ESTA PC (solo si config.json define "destino"). Se evalua aparte de la notebook: el servicio y el espacio son
# locales. $dest: ok (servicio activo y carpeta legible), logAgeMin / fileAgeHours (antiguedad de la copia mas nueva; $null = no hay
# ninguna), usedGB, capGB, freeGB. $expectCopies = $false cuando la notebook no responde: el atraso ya esta explicado por ese aviso.
function Update-ObsDestState($state, $dest, [bool]$expectCopies, $limits) {
    $events = @()
    if ($null -eq $dest) { return $events }

    if (-not $dest.ok) {
        $state.destDownFails++
        if (-not $state.destDown -and $state.destDownFails -ge 2) {
            $state.destDown = $true
            $events += New-ObsEvent 'DESTINO_CAIDO' 'critico' "El destino de respaldos en esta PC NO esta disponible ($($dest.reason)). La notebook no puede enviar copias."
        }
        return $events
    }
    $state.destDownFails = 0
    if ($state.destDown) {
        $state.destDown = $false
        $events += New-ObsEvent 'DESTINO_DISPONIBLE' 'info' 'El destino de respaldos en esta PC volvio a estar disponible.'
    }

    if ($expectCopies) {
        $lateLog = ($null -eq $dest.logAgeMin) -or ($dest.logAgeMin -gt $limits.maxMinutosLogs)
        if ($lateLog) {
            $state.destLogFails++
            if (-not $state.destLogLate -and $state.destLogFails -ge $limits.muestrasAtraso) {
                $state.destLogLate = $true
                $age = 'no hay ninguna copia'
                if ($null -ne $dest.logAgeMin) { $age = "la mas nueva tiene $([int]$dest.logAgeMin) minutos" }
                $events += New-ObsEvent 'COPIAS_ATRASADAS' 'critico' "No estan llegando respaldos de la base a esta PC: $age (se esperan cada 15 minutos)."
            }
        } else {
            $state.destLogFails = 0
            if ($state.destLogLate) {
                $state.destLogLate = $false
                $events += New-ObsEvent 'COPIAS_AL_DIA' 'info' 'Volvieron a llegar respaldos de la base a esta PC.'
            }
        }
        $lateFile = ($null -eq $dest.fileAgeHours) -or ($dest.fileAgeHours -gt $limits.maxHorasArchivos)
        if ($lateFile) {
            $state.destFileFails++
            if (-not $state.destFileLate -and $state.destFileFails -ge $limits.muestrasAtraso) {
                $state.destFileLate = $true
                $events += New-ObsEvent 'COPIA_DIARIA_ATRASADA' 'critico' "La copia diaria completa (03:00) no llego a esta PC en las ultimas $($limits.maxHorasArchivos) horas."
            }
        } else {
            $state.destFileFails = 0
            if ($state.destFileLate) {
                $state.destFileLate = $false
                $events += New-ObsEvent 'COPIA_DIARIA_AL_DIA' 'info' 'La copia diaria volvio a llegar a esta PC.'
            }
        }
    } else {
        $state.destLogFails = 0
        $state.destFileFails = 0
    }

    # espacio: porcentaje del tope y disco libre; para volver a "ok" hace falta bajar del 75 % (sin avisos que oscilan)
    $pct = 100.0 * $dest.usedGB / $dest.capGB
    $level = 'ok'
    if ($pct -ge 95 -or $dest.freeGB -lt 5) { $level = 'critico' }
    elseif ($pct -ge 80 -or $dest.freeGB -lt 20) { $level = 'aviso' }
    elseif ($state.destSpace -ne 'ok' -and $pct -ge 75) { $level = $state.destSpace }
    if ($level -ne $state.destSpace) {
        $detail = "usado $([math]::Round($dest.usedGB, 1)) de $($dest.capGB) GB; libres en el disco $([int]$dest.freeGB) GB"
        if ($level -eq 'critico') { $events += New-ObsEvent 'ESPACIO_CRITICO' 'critico' "Destino de respaldos casi sin espacio: $detail." }
        elseif ($level -eq 'aviso' -and $state.destSpace -eq 'ok') { $events += New-ObsEvent 'ESPACIO_ALTO' 'aviso' "Destino de respaldos llenandose: $detail." }
        elseif ($level -eq 'ok') { $events += New-ObsEvent 'ESPACIO_OK' 'info' "Espacio del destino de respaldos normalizado: $detail." }
        $state.destSpace = $level
    }
    return $events
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

    # firewall (vigilancia del servidor): dos muestras iguales para avisar; un aviso por cada estado distinto. 'na' no dice nada.
    # Un estado.json guardado por la version anterior no tiene estas claves: se crean.
    foreach ($k in @(@('fw', 'ok'), @('fwFails', 0), @('fwLast', ''))) { if ($null -eq $state.($k[0])) { $state.($k[0]) = $k[1] } }
    if (@('incidente', 'detenido', 'config-invalida', 'vencido', 'ilegible') -contains $p.fw) {
        if ($p.fw -eq $state.fwLast) { $state.fwFails++ } else { $state.fwLast = $p.fw; $state.fwFails = 1 }
        $need = 2
        if ($p.fw -eq 'ilegible') { $need = 3 }
        if ($state.fwFails -ge $need -and $state.fw -ne $p.fw) {
            $state.fw = $p.fw
            $events += New-ObsEvent 'FIREWALL_ALERTA' 'critico' "Firewall del servidor: estado '$($p.fw)'. En el servidor: cat /var/lib/bury-firewall/incidentes.log (docs/operacion-servidor-ubuntu.md 7)."
        }
    } elseif ($p.fw -eq 'ok' -or $p.fw -eq 'docker-inactivo') {
        $state.fwFails = 0
        $state.fwLast = ''
        if ($state.fw -ne 'ok' -and $p.fw -eq 'ok') {
            $state.fw = 'ok'
            $events += New-ObsEvent 'FIREWALL_OK' 'info' 'La vigilancia del firewall del servidor volvio a ok y sin incidentes abiertos.'
        }
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
