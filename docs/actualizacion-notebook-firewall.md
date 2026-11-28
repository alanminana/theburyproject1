# Actualizar el firewall de la notebook provisional

**Estado: NO ejecutado. Requiere autorización del propietario.** Escrito a partir de comprobaciones de solo lectura del 2026-11-27 23:06–23:10 y
ensayado en una VM Ubuntu 26.04.1 con la misma versión del firewall anterior y la misma disposición de dos instancias, con contenedores
sintéticos ([evidencias-instalacion.md](evidencias-instalacion.md)). El ensayo **no** usa el ERP, los datos ni el hardware de la notebook.

## 0. Qué hay y qué cambia

| Dato (2026-11-27) | Valor |
|---|---|
| Sistema | Ubuntu 26.04.1, Docker 29.10.0, Compose 5.8.0 (el ensayo usó Docker 29.10.1) |
| Instancias | `theburyproject-notebook` (443, `~/theburyproject1`) y `theburyproject-prov` (8443, `~/theburyproject-prov`) |
| Configuración | `/etc/bury/firewall.conf` (443) y `/etc/bury/firewall-prov.conf` (8443); ambas `ALLOW_FROM="192.168.50.0/24"` |
| Firewall instalado | Anterior (SHA-256 `9290e777…`, igual al del repositorio antes del 27/11): **falla abierto** |
| Unidades | `bury-firewall` y `bury-firewall-prov`, ambas con `ExecStop=remove`; drop-in con `ExecStartPost=-` |

**Se cambia:** el script del firewall, sus unidades y el drop-in; se agrega la vigilancia; se actualizan el lector de energía y el monitor.

**No se cambia:** `firewall.conf`, `ufw`, paquetes, Docker, las imágenes, las bases, los `.env`, los clones de las instancias ni los cron.

**No usar `server-linux-bootstrap.sh` aquí:** ejecuta `apt`, puede reconfigurar `ufw` y reescribe `firewall.conf` con lo que se le pase.

**Riesgo que se asume:** desde este cambio, si el firewall no se puede aplicar, **Docker no queda en marcha**: caen las dos instancias y no corren
los respaldos. SSH sigue disponible.

## 1. Requisitos: si falta uno, no empezar

- [ ] Autorización del propietario y ventana acordada. Evitar **02:15–04:15** (respaldos y verificación de copias) y los domingos 04:15–06:15.
- [ ] **Consola física:** pantalla y teclado en la notebook, y la contraseña de `sudo`. Comprobar **antes** que se puede iniciar sesión ahí.
- [ ] Cargador conectado, `AC online=1` y batería sobre 80 %.
- [ ] La PC de respaldos encendida, con `sshd` en marcha.
- [ ] Passphrase del paquete de recuperación **verificada fuera de la notebook** (§8). Sin esto, no seguir.
- [ ] El PR con la vigilancia fusionado y con CI verde. Anotar el commit: `SHA=<commit completo de main>`.

Dejar abiertas **dos** sesiones: la consola física y una SSH desde la PC.

## 2. Respaldos

```bash
cd ~/theburyproject-prov
COMPOSE_PROJECT_NAME=theburyproject-prov scripts/backup/backup.sh all      # detiene app unos segundos
COMPOSE_PROJECT_NAME=theburyproject-prov scripts/backup/restore-test.sh
grep -E 'copia externa (sql|files): subida y VERIFICADA' /srv/bury-backups-prov/logs/backup.log | tail -2
cd ~/theburyproject1
COMPOSE_PROJECT_NAME=theburyproject-notebook scripts/backup/backup.sh all
```

Esperado: cada comando termina con código 0 y las dos líneas `VERIFICADA` son de ahora. El paquete de recuperación no cambia con este
procedimiento (no toca `.env`); basta con que el vigente esté copiado en la PC y verificado (§8).

## 3. Código nuevo en una carpeta aparte, y copia del estado actual

```bash
git clone https://github.com/alanminana/theburyproject1.git ~/bury-fw && cd ~/bury-fw && git switch --detach "$SHA" && git log --oneline -1

PREVIO=~/bury-fw-previo-$(date +%Y%m%d-%H%M)
sudo mkdir -p "$PREVIO/dropin" "$PREVIO/units" "$PREVIO/etc-bury"
sudo cp -a /usr/local/sbin/bury-firewall /usr/local/bin/bury-estado-energia "$PREVIO/"
sudo cp -a /etc/systemd/system/bury-firewall.service /etc/systemd/system/bury-firewall-prov.service "$PREVIO/units/"
sudo cp -a /etc/systemd/system/docker.service.d/bury-firewall.conf "$PREVIO/dropin/"
sudo cp -a /etc/bury/. "$PREVIO/etc-bury/"
sudo cp -a /opt/theburyproject/scripts/monitoring "$PREVIO/monitoring"
sudo iptables-save | sudo tee "$PREVIO/iptables.txt" >/dev/null
echo "$PREVIO"     # anotarlo: se usa en §7
```

## 4. Comprobación previa, sin cambiar nada

El script nuevo revisa las reglas **actuales** de las dos instancias. Solo lee.

```bash
cd ~/bury-fw
sudo bash scripts/install/bury-firewall.sh check-config && echo "configuración OK"
sudo bash scripts/install/bury-firewall.sh verify
sudo cat /etc/docker/daemon.json
ls /etc/systemd/system/docker.service.d/
```

Esperado: `configuración OK`; `verificado (BURY-ERP, puerto 443)` y `verificado (BURY-ERP-PROV, puerto 8443)`; `daemon.json` sin `iptables`,
`firewall-backend` ni `live-restore`; en el drop-in, solo `bury-firewall.conf`.

**Si algo difiere, detenerse aquí.** No se cambió nada. Guardar la salida y analizarla.

## 5. Instalación

No reinicia Docker ni los contenedores.

```bash
cd ~/bury-fw
sudo install -m 0755 scripts/install/bury-firewall.sh /usr/local/sbin/bury-firewall
sudo bury-firewall install-systemd      # unidades y drop-in nuevos; quita ExecStop de bury-firewall-prov.service
sudo systemctl daemon-reload
sudo bury-firewall guard                # aplica las dos cadenas; si falla, NO seguir: ir a §7
sudo systemctl restart bury-firewall.service bury-firewall-prov.service
sudo bury-firewall verify
sudo systemctl enable --now bury-firewall-watch.service
sleep 15; cat /var/lib/bury-firewall/estado      # estado=ok ... incidente=-
```

Comprobar las unidades:

```bash
systemctl cat docker.service | grep bury-firewall      # ExecStartPre=... guard  y  ExecStartPost=... verify (sin '-')
grep -c '^ExecStop' /etc/systemd/system/bury-firewall*.service      # 0 en todas
```

Aviso y monitor:

```bash
sudo install -m 0755 scripts/monitoring/bury-estado-energia.sh /usr/local/bin/bury-estado-energia
/usr/local/bin/bury-estado-energia       # la línea termina en fw=ok
sudo bash scripts/monitoring/instalar-agente.sh /opt/theburyproject/scripts/monitoring
sudo systemctl restart bury-monitor && sleep 70 && systemctl is-active bury-monitor
```

En la **PC**, con el checkout principal actualizado, actualizar el observador (conserva `config.json` y la clave):

```powershell
Stop-ScheduledTask -TaskName BuryObservador
powershell -NoProfile -ExecutionPolicy Bypass -File D:\git\theburyproject1\scripts\monitoring\observador-pc\instalar-observador.ps1 -Dir D:\bury-observador
powershell -NoProfile -File D:\bury-observador\test-observador.ps1     # termina en OK
Start-ScheduledTask -TaskName BuryObservador
```

## 6. Verificación

### 6.1 Acceso desde la PC (autorizada)

```powershell
foreach ($p in 443, 8443) { Test-NetConnection 192.168.50.223 -Port $p | Select-Object RemotePort, TcpTestSucceeded }
```

Esperado: `True` en las dos.

### 6.2 Que el filtro corta a un origen no autorizado

En la LAN todos los equipos están autorizados. Se excluye la PC **solo del 8443** durante la prueba, permitiendo una dirección que no existe.
SSH no se ve afectado: lo filtra `ufw`.

```bash
sudo cp -a /etc/bury/firewall-prov.conf /root/firewall-prov.conf.prueba
sudo sed -i 's|^ALLOW_FROM=.*|ALLOW_FROM="192.168.50.254/32"|' /etc/bury/firewall-prov.conf
sudo systemctl restart bury-firewall-prov.service
```

Desde la PC, `Test-NetConnection 192.168.50.223 -Port 8443` debe dar `False`, y el 443 seguir en `True`. Restaurar **enseguida**:

```bash
sudo cp -a /root/firewall-prov.conf.prueba /etc/bury/firewall-prov.conf
sudo systemctl restart bury-firewall-prov.service && sudo bury-firewall verify && cat /etc/bury/firewall-prov.conf
```

Si la vigilancia abrió un incidente durante la prueba, revisarlo y cerrarlo (§6.3).

### 6.3 La vigilancia repone una regla borrada

Expone el 8443 unos segundos a la LAN, que ya está autorizada.

```bash
sudo iptables -D DOCKER-USER -j BURY-ERP-PROV
sleep 20; cat /var/lib/bury-firewall/estado; sudo bury-firewall verify
tail -n 3 /var/lib/bury-firewall/incidentes.log
sudo bury-firewall ack "$(sed -n 's/^id=//p' /var/lib/bury-firewall/incidente)"
```

Esperado: `estado=incidente`, `verify` OK, y tras `ack`, `incidente=-`. La PC debe mostrar el aviso `FIREWALL_ALERTA` y después `FIREWALL_OK`
si el incidente se deja abierto más de dos minutos.

### 6.4 Reinicio, con la consola delante

```bash
sudo systemctl reboot
```

Al volver, **sin tocar nada**:

```bash
systemctl is-active docker bury-firewall bury-firewall-prov bury-firewall-watch bury-monitor
sudo bury-firewall verify
cat /var/lib/bury-firewall/estado
docker ps --format '{{.Names}} {{.Status}}'
curl -s -o /dev/null -w '%{http_code}\n' --cacert ~/theburyproject-prov/caddy-root-ca.crt --resolve tbp-prov:8443:127.0.0.1 https://tbp-prov:8443/health/ready
curl -s -o /dev/null -w '%{http_code}\n' --cacert ~/theburyproject1/caddy-root-ca.crt --resolve tbp-nb:443:127.0.0.1 https://tbp-nb/health/ready
```

Esperado: todo `active`, `verify` de las dos cadenas, `estado=ok`, contenedores sanos y `200` en ambos. Repetir §6.1.
En el siguiente cuarto de hora, comprobar una copia `VERIFICADA` nueva y su llegada a la PC.

## 7. Si algo falla

| Síntoma | Qué hacer |
|---|---|
| §4 falla | No se cambió nada. Detenerse |
| `guard` falla en §5 | Las reglas anteriores siguen puestas (se valida antes de reemplazar) y Docker no se reinició. **No reiniciar el equipo.** Volver atrás (abajo) |
| Docker figura `failed` | `journalctl -u docker -b --no-pager \| grep bury-firewall`; corregir la causa; `sudo bury-firewall guard && sudo systemctl reset-failed docker && sudo systemctl start docker && sudo systemctl start bury-firewall bury-firewall-prov` |
| `estado=detenido` | La vigilancia detuvo Docker por no poder garantizar el filtro. Leer `incidentes.log`, corregir y usar la fila anterior |
| `estado=config-invalida` | Las reglas siguen vigentes. Corregir el `.conf` **antes de reiniciar** nada |
| La vigilancia actúa por error y repite | `sudo systemctl stop bury-firewall-watch` (el filtro queda puesto) y analizar |

**Volver al firewall anterior.** Restituye un sistema que **falla abierto**: usarlo solo para recuperar el servicio, y reprogramar la actualización.
Al reiniciar las unidades viejas, el puerto queda un instante sin filtro.

```bash
sudo systemctl disable --now bury-firewall-watch.service
sudo rm -f /etc/systemd/system/bury-firewall-watch.service
sudo cp -a "$PREVIO/bury-firewall" /usr/local/sbin/bury-firewall
sudo cp -a "$PREVIO/units/." /etc/systemd/system/
sudo cp -a "$PREVIO/dropin/bury-firewall.conf" /etc/systemd/system/docker.service.d/
sudo cp -a "$PREVIO/etc-bury/." /etc/bury/
sudo systemctl daemon-reload
while sudo iptables -D DOCKER-USER -m conntrack --ctstate DNAT -m comment --comment bury-barrera -j DROP 2>/dev/null; do :; done
sudo systemctl reset-failed docker
sudo systemctl start docker
sudo systemctl restart bury-firewall.service bury-firewall-prov.service
sudo bury-firewall status; sudo BURY_FW_CONF=/etc/bury/firewall-prov.conf BURY_FW_CHAIN=BURY-ERP-PROV bury-firewall status
sudo cp -a "$PREVIO/bury-estado-energia" /usr/local/bin/bury-estado-energia
sudo cp -a "$PREVIO/monitoring/." /opt/theburyproject/scripts/monitoring/ && sudo systemctl restart bury-monitor
```

Si no hay SSH, estos pasos se ejecutan desde la consola física. Si Docker no vuelve ni así, **no** borrar volúmenes ni ejecutar `docker compose down`:
los datos están en los volúmenes; detenerse y analizar. La recuperación en otra máquina está en [operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §6.

## 8. Passphrase del paquete: resguardarla y comprobarla sin mostrarla

Hay **dos** passphrases en la notebook y no son la misma:

| Instancia | Archivo en la notebook |
|---|---|
| **Provisional** (paquete vigente) | `~/.bury-custodia-prov/passphrase` |
| Laboratorio | `~/.bury-custodia/passphrase` |

### 8.1 Guardarla en un gestor de contraseñas

En la PC. Antes, en Windows, **desactivar el historial del portapapeles y su sincronización** (Configuración → Sistema → Portapapeles). Cerrar
programas que registren el portapapeles y no compartir pantalla. El comando copia la frase **sin mostrarla** y no la escribe en disco:

```powershell
& "$env:SystemRoot\System32\OpenSSH\ssh.exe" snowsh@192.168.50.223 "head -n1 .bury-custodia-prov/passphrase | tr -d '\r\n'" | Set-Clipboard
```

Pegarla en una entrada nueva del gestor (por ejemplo, «ERP provisional – passphrase del paquete de recuperación»), guardar, y vaciar el
portapapeles:

```powershell
Set-Clipboard -Value ' '
```

No guardarla en un archivo, en el chat ni en la misma carpeta que el paquete (`D:\custodia-B-tbp`). Además, conviene una **copia en papel**
guardada fuera del local: la PC y la notebook están en el mismo lugar. El gestor debe poder abrirse **sin** esta PC.

### 8.2 Comprobar que la copia guardada abre el paquete

La prueba vale solo si la frase sale **del gestor** (o del papel), no de la notebook. `verify` descifra en memoria y muestra **solo nombres** de
archivo. En la PC, en una ventana de Ubuntu (WSL):

```bash
cd /mnt/d/git/theburyproject1
scripts/backup/recovery-package.sh verify "$(ls -t /mnt/d/custodia-B-tbp/provisional/bury-recovery_*.gpg | head -1)"
```

**Esperar a que aparezca `Passphrase del paquete:` antes de pegar** (clic derecho) y pulsar Enter. Si se pega antes, la terminal muestra la frase.
Al terminar, vaciar de nuevo el portapapeles.

| Resultado | Significado |
|---|---|
| `sha256 OK` y `paquete valido`, con `.env`, `backup-offsite.env` y `MANIFEST.txt` | La custodia funciona |
| `no se pudo descifrar/autenticar` (código 3) | La copia guardada es incorrecta o el paquete está dañado. Repetir §8.1 |

Lo ideal es repetirlo **en otro equipo**, con una copia del paquete. Registrar fecha y resultado, nunca la frase. Repetir cada vez que se cree
un paquete nuevo. El comando de §8.2 se probó el 2026-11-27 desde WSL con un paquete sintético; con el paquete real lo ejecuta el propietario.

## 9. Después

- Hasta que los clones de las instancias estén al día, **no ejecutar `rollback.sh` desde ellos**: la versión vieja deja el contenedor `migrate`
  con la imagen nueva y el siguiente respaldo vuelve a migrar la base ([operacion-servidor-ubuntu.md](operacion-servidor-ubuntu.md) §5).
  Actualizar un clon es parte de un despliegue, que se autoriza aparte.
- `~/bury-fw` no es una instancia: no tiene `.env` y desde ahí no se ejecutan `install.sh`, `backup.sh` ni `docker compose`.
- Para actualizar el firewall más adelante: `git -C ~/bury-fw fetch`, cambiar al commit nuevo y repetir §4 y §5.
- Conservar `$PREVIO` hasta cerrar una semana sin incidentes.
- Registrar fecha, commit, resultados de §6 y duración de la ventana.
