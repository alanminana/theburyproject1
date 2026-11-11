# CSP Enforce: preparación de la activación (D-05)

**Estado: PREPARADO, NO ACTIVADO.** Este documento deja lista la ventana, las verificaciones, la observación, la reversión y los criterios GO/NO-GO. **No se activó `Enforce` ni se cambió ninguna configuración del entorno real** (ni la app local, ni su `.env`, ni su base). Activarlo requiere una **autorización posterior y explícita del propietario**; hasta entonces `Csp:Mode` sigue en `Off` (por defecto).

Antecedentes: `docs/csp-enforce-pendientes.md` (pruebas y rondas previas), `docs/seguridad-web.md`, `Security/Csp.cs`. Toda cifra de este documento marcada «medido» salió de un **stack QA aislado y descartable** (proyecto compose `bury-r2csp`, base SQL Server sintética, puerto 18240; y, para la modalidad local, la app compilada con `dotnet exec` contra esa misma base en el puerto 18241). Lo que depende del entorno real y no se pudo medir está dicho como tal (§4).

## 1. Cómo se configura hoy `Csp:Mode` y qué cambia realmente

La política la decide **una sola clave de configuración**: `Csp:Mode` = `Off` | `ReportOnly` | `Enforce` (variable de entorno `Csp__Mode`). Se lee al arrancar el proceso: **cambiar el valor exige reiniciar la app**. No hay cambio de código ni de base de datos. En el entorno real la clave **no está definida hoy** (verificado el 2026-11-11, solo lectura: `Csp__Mode` vacía en las variables de usuario y de máquina; la app real no estaba en ejecución al verificar, así que no se pudo inspeccionar su entorno de proceso) ⇒ `Off`: la app no envía cabecera CSP y `/csp-report` responde 404.

El entorno real corre de dos formas, y el procedimiento cubre las dos:

| | A. App local (el entorno real de hoy) | B. Docker + Caddy (despliegue futuro, `docs/despliegue-produccion.md`) |
|---|---|---|
| Cómo corre | `scripts\ejecutar-erp-local.ps1` desde el worktree limpio `D:\git\theburyproject1-run`: `dotnet exec bin\app\TheBuryProyect.dll --urls http://127.0.0.1:18787`, LocalDB, entorno `Development`, una sola instancia | `docker compose` (`db`, `migrate` one-shot, `app`, `caddy`); `app` escucha en `app:8080` solo dentro de la red interna; los clientes entran por `https://<ERP_DOMAIN>` (Caddy, `tls internal`) |
| Dónde se define el modo | Parámetro nuevo `-CspMode Off\|ReportOnly\|Enforce` del lanzador (solo admite esos tres nombres: un typo ni siquiera llega a la app). Sin el parámetro usa la `Csp__Mode` heredada del entorno (con aviso) o `Off`. **No se persiste en variables de usuario/sistema**: queda a la vista en el comando de arranque | Línea `CSP_MODE=` del `.env` (nueva en `.env.example`; `docker-compose.yml` la pasa a `app` como `Csp__Mode: ${CSP_MODE:-Off}`; ausente o vacía = `Off`) |
| Cambio exacto | Detener el proceso y relanzar: `powershell -NoProfile -File scripts\ejecutar-erp-local.ps1 -CspMode Enforce` | Editar `.env` (`CSP_MODE=Enforce`) y `docker compose up -d --no-deps app` |
| Reversión (sin redeploy de código) | Relanzar con `-CspMode ReportOnly` (o `Off`) | `CSP_MODE=ReportOnly` (u `Off`) en `.env` y `docker compose up -d --no-deps app` |
| Dónde cae el log de violaciones | `bin\app.log` del worktree de ejecución (stdout, UTC, formato *simple console*; **se trunca en cada reinicio**) | `docker compose logs --no-color -t app` (json-file, **10 MB × 3 archivos y se pierde al recrear el contenedor**) |
| Tiempo medido (§7) | detener 1,1 s + compilación incremental 7 s + arranque y primera respuesta 4,0–6,9 s ⇒ **≈ 15 s** (el lanzador además hace sus chequeos de git y espera hasta 30 s); medido con `dotnet exec` contra la base sintética, no con el lanzador ni LocalDB reales | `up -d --no-deps app` retorna en 1,2–3,1 s y el login responde 200 a los **2,0–4,0 s** (6 mediciones); `healthy` en menos de 25 s |

Trampas medidas y de uso:

1. **`docker compose restart app` NO aplica el cambio**: el contenedor conserva el entorno con el que se creó. Hay que usar `up -d` (recrea `app` porque cambió su entorno). Comprobado en el ensayo: con `CSP_MODE=Enforce` ya en el `.env`, `docker compose restart app` dejó la cabecera en `Content-Security-Policy-Report-Only`; con `up -d --no-deps app` pasó a `Content-Security-Policy`.
2. **Los logs se pierden al reiniciar**: antes de cada reinicio (activación, reversión, deploy) archivar el log (§5). Si no, se pierde la evidencia de la observación.
3. El lanzador local exige un worktree limpio igual a `origin/main` y compila: el parámetro `-CspMode` solo existe una vez que esta rama está integrada y el worktree de ejecución actualizado. Mientras tanto (o si hay que ejecutar a mano) se puede fijar `$env:Csp__Mode='ReportOnly'` en la misma consola antes de lanzar (el lanzador lo respeta y avisa).
4. **Un `Csp:Mode` inválido impide el arranque** (`ValidateOnStart`, 2026-11-11): `Enforse`, `5`, `ReportOnly,Enforce` o una clave presente pero vacía. Es deliberado (mejor no arrancar que degradar la política en silencio) pero **un typo al activar o revertir deja la app caída**:
   - Local: `-CspMode` rechaza el valor antes de hacer nada (medido: error de validación, 0 efectos). Con `$env:Csp__Mode` a mano el proceso termina al arrancar (medido: `Failed to convert configuration value 'Enforse' at 'Csp:Mode'` en `bin\app.err`/`app.log`, el lanzador informa «la aplicacion termino al arrancar»).
   - Docker: `restart: unless-stopped` deja a `app` en **bucle de reinicio**: medido, `docker compose ps` muestra `Up 1–6 seconds (health: starting)` cuyo uptime **nunca crece** (se reinicia cada ≈ 2,6 s), `/Identity/Account/Login` no responde (HTTP 000) y el log dice `Failed to convert configuration value 'Enforse' at 'Csp:Mode'`; con Caddy delante, 502. **Prevención**: `scripts/deploy/preflight.sh` ahora valida `CSP_MODE` del `.env` (vacío/Off/ReportOnly/Enforce; cualquier otro valor = FAIL; `Enforce` = aviso) y debe correrse **antes** del `up -d`. **Detección** si pasa igual: `docker compose ps`, `docker compose logs --tail 30 app | grep "Csp:Mode"`. **Recuperación**: corregir la línea y repetir el `up -d` (medido: el login vuelve a responder 200 a los 2,4 s). Es el mismo costo que cualquier reversión: por eso la reversión se ensaya y se escribe copiando el comando, no tipeando el valor.
5. La cabecera pasa de `Content-Security-Policy-Report-Only` a `Content-Security-Policy` (y viceversa; con `Off` no hay ninguna): medido con `curl` en §7.
6. El `.env` real y el worktree de ejecución **no se tocaron** en esta tarea; los comandos de §5 son los que se ejecutarán en la ventana.

## 2. Fase de observación en `ReportOnly` con uso real

Objetivo: ver, con usuarios reales, datos reales y navegadores reales, lo que ningún QA sintético reproduce (ítem 8 de `csp-enforce-pendientes.md`). `ReportOnly` no bloquea nada: cada violación que ocurriría bajo `Enforce` se informa a `/csp-report` (sin autenticación, con límite de 30/min por IP, cuerpo ≤ 8 KB) y la app la registra como **WARN** de categoría `CspReport`:

```
2026-11-11T14:03:35Z warn: CspReport[0]
      => SpanId:…, TraceId:… => ConnectionId:… => RequestPath:/csp-report RequestId:…
      CSP violation: document-uri=http://…/Cliente | blocked-uri=https://x.test/a.png | effective-directive=img-src | violated-directive=img-src | source-file=… | line-number=… | disposition=report
```

Con `docker compose logs -t` cada línea lleva además el prefijo `app-1  | <timestamp Docker>`; el script lo soporta. El nivel por defecto de logging es `Warning`, así que estas líneas siempre se registran.

**Qué se mide y cómo — `scripts/ops/csp-observacion.ps1`** (solo lectura; abre el log con `FileShare.ReadWrite`, se puede correr con la app escribiendo):

```powershell
powershell -NoProfile -File scripts\ops\csp-observacion.ps1 -Ruta D:\csp-obs -Desde 2026-11-16 -Hasta 2026-11-27 -DiasMinimos 10 -ExigirCentinela
```

- Agrupa las violaciones **propias** por directiva, URI bloqueada (sin query), página de origen (`document-uri` sin query y con ids colapsados: `/Venta/Details/{id}`), combinación de las tres, día y disposición.
- Separa el **ruido conocido** (no cuenta, se lista aparte para revisarlo): esquemas de extensiones y del navegador (`chrome-extension:`, `moz-extension:`, `safari-extension:`, `safari-web-extension:`, `ms-browser-extension:`, `webkit-masked-url:`, `about:`, `chrome:`, `resource:`, `view-source:`…) en `blocked-uri`, `source-file` o `document-uri`, y `data:` fuera de `img-src`/`font-src` (la política ya permite `data:` en imagen y fuente). `-RuidoExtra <regex>` agrega ruido propio del negocio, **solo con una justificación escrita** en el acta de la ventana. Una violación `inline` o `eval` sin `source-file` de extensión es **propia**.
- Un reporte que no se puede interpretar cuenta como propio (no hay forma de que una violación quede oculta por un formato inesperado).
- **Exit code**: `0` solo si hay `≤ -MaxPropias` (por defecto 0) violaciones propias **y** la medición es válida; `1` hay propias; `2` uso incorrecto; `3` **medición inválida** (sin archivos, sin ninguna línea de log en el rango, menos días con actividad que `-DiasMinimos`, o falta el centinela con `-ExigirCentinela`). Un log vacío o un rango equivocado nunca dan verde.
- **Centinela**: al iniciar la ventana, desde un navegador real con la sesión abierta y la consola de desarrollo, se provoca **una** violación inofensiva: `document.body.appendChild(Object.assign(document.createElement('img'),{src:'https://csp-centinela.invalid/c.png'}))`. El script la reconoce (no es ruido ni propia) y, con `-ExigirCentinela`, exige verla: prueba que **navegador → `/csp-report` → log** funciona de punta a punta. Sin eso, «0 violaciones» podría significar «el circuito está roto».
- Límites: las violaciones sobre el límite de 30/min por IP reciben 429 y **no quedan en el log** (los conteos son una cota inferior; basta una propia para frenar el GO). Los logs pueden contener *query strings* de las URL (el script no las muestra, pero el archivo las tiene): tratarlos como datos sensibles, guardarlos fuera del repo y no compartirlos.
- Revisión semanal sugerida durante la ventana (el mismo comando con el rango acumulado) y **antes de cada reinicio** archivar el log (§5, paso P4).

## 3. Criterios GO / NO-GO (numéricos y verificables)

La activación solo se propone al propietario si se cumplen **todos** los puntos GO. Cada uno tiene evidencia concreta que se adjunta al acta.

| # | Criterio | Umbral | Cómo se verifica |
|---|---|---|---|
| G1 | **Duración** de la observación en `ReportOnly` con uso real | **≥ 10 días hábiles** consecutivos con la app en uso (dos semanas laborales completas) **y** que el período incluya **un cierre/inicio de mes**. Si los 10 días no lo incluyen, se extiende hasta incluirlo | `csp-observacion.ps1 -DiasMinimos 10`; fechas del acta |
| G2 | **Violaciones propias** | **0** en todo el período (después de descartar ruido documentado) | `csp-observacion.ps1 … -MaxPropias 0` ⇒ exit 0 sobre **todos** los logs archivados del período |
| G3 | **El circuito de reporte funciona** | centinela visto ≥ 1 vez al inicio, y de nuevo en el último día | `-ExigirCentinela` |
| G4 | **Cobertura de uso**: las 12 áreas críticas usadas por usuarios reales al menos **una vez cada una por cada rol que las usa** (lista abajo) | 12/12 áreas, todos los roles reales | planilla de uso de la ventana (la completa el responsable; no se infiere del log) |
| G5 | **Navegadores y dispositivos reales** | **todo navegador/SO/dispositivo en uso real** (respuesta del propietario a §9) quedó cubierto al menos 1 día completo de la ventana; **0** violaciones propias en cada uno | planilla + log. El `/csp-report` no trae el user-agent: el responsable anota qué equipo usó cada día |
| G6 | **SignalR / notificaciones** | conectado (campanita sin error) en ≥ 1 sesión de cada navegador de G5, y `csp-humo-lectura.js` OK | humo + chequeo manual |
| G7 | **Impresión y descargas** | OK en el equipo/impresora real: Cotización → Imprimir, Factura → Imprimir, un PDF y un Excel de Reportes, un contrato/recibo | chequeo manual guiado (§5, V6) |
| G8 | **Humo automatizado** previo | `csp-humo-lectura.js --esperar reportonly` = exit 0 contra el entorno real el día de la activación | salida del script |
| G9 | **Respaldo y configuración previa** | respaldo de datos con `Probar` OK de las últimas 24 h; copia fechada del `.env` (Docker) o de la línea de comando de arranque (local) guardada fuera del repo; comando de reversión copiado en el acta | archivos + acta |
| G10 | **Reversión ensayada** | el ensayo de §7 (o su repetición sobre el entorno real con `ReportOnly`→`ReportOnly`) con tiempo de reversión **≤ 5 min** de punta a punta | §7 |
| G11 | **Autorización** | autorización escrita del propietario, ventana acordada (día/hora, preferentemente fuera del horario de caja), responsable y suplente | acta |

**Áreas críticas de G4** (usar cada una de forma real, con el flujo completo, no solo abrir la pantalla): (1) Login/inicio y menú; (2) Venta: crear, confirmar, facturar, comprobante; (3) Cotizador/cotización: simular, guardar, imprimir, PDF; (4) Caja: apertura, movimientos, **cierre**/arqueo; (5) Crédito: alta/configuración, cobro de cuotas, recibo; (6) Mora: Index, Alertas, Logs, configuración; (7) Cliente: alta (modal), edición, documentos (subida/visualización); (8) Catálogo/Producto: alta/edición (modal AJAX), precios, categorías/marcas; (9) Proveedor y órdenes de compra; (10) Reportes y **exportaciones** Excel/PDF; (11) Devoluciones, garantías, unidades/stock; (12) Seguridad y configuración: usuarios, roles, permisos, Documentos (vista previa de plantillas), medios de pago. Más: tickets/notas rápidas y notificaciones (layout compartido).

**NO-GO (cualquiera):** una violación propia sin explicar; duración < G1; centinela ausente; un navegador/dispositivo en uso real sin cubrir; una función crítica que se rompió y se corrigió durante la ventana (la ventana **reinicia el reloj de G1** desde la corrección); respaldo no verificado; ventana sin responsable/suplente. Si una violación propia se corrige en código o plantilla, se vuelve a medir **desde cero G1 y G2**: una corrección cambia lo observado.

## 4. Qué NO cubre la evidencia de QA (lo que solo la ventana real puede mostrar)

La evidencia previa (suite E2E, 44 escenarios con interacciones, recorrido de 7 roles × 3 navegadores, HTTPS + SignalR con Caddy local; ver `csp-enforce-pendientes.md`) es fuerte pero **no cubre**:

- **Safari real e iOS/Android físicos**: el WebKit de Playwright no es Safari (visor PDF, `print()`, extensiones, *frame-ancestors*). Si el negocio usa Mac/iPhone/iPad/Android, esos equipos son parte obligatoria de G5.
- **El servidor y el nombre de host reales** (`ERP_DOMAIN`), **Caddy con certificados definitivos** (la CA raíz instalada en cada cliente) y `frame-ancestors`/`wss://` tras el proxy real; hoy el entorno real corre sin Caddy por `http://127.0.0.1`. Pasar a Docker/Caddy **cambia el origen y reinicia la evidencia de G5/G6**: la ventana debe hacerse en la modalidad en la que se va a operar.
- **Fuentes de Google** (`fonts.googleapis.com`/`fonts.gstatic.com`, permitidas por la política) con salida a Internet del servidor/cliente real: en QA no hubo salida; un bloqueo de red no es una violación CSP, pero una fuente faltante sí se ve en la pantalla.
- **Extensiones de navegador, antivirus/proxys corporativos y gestores de contraseñas reales**: inyectan scripts/estilos; el script solo clasifica su ruido, no lo reproduce.
- **Impresoras reales, visores PDF del sistema y descargas del navegador real**; recibos y contratos reales (el QA no emitió comprobantes con efectos: se verificaron PDF de plantillas/cotización).
- **Datos reales y flujos con efectos** (cobro real, cierre de caja real, subida de documentos de clientes, facturación): QA no los ejecuta contra datos que deban preservarse.
- **Estados raros**: perfiles de permisos personalizados, usuarios nunca probados, pantallas poco usadas.
- **Mercado Libre**: módulo diferido por el propietario (incluye DEF-ML-01); no se validó bajo `Enforce`. Si se usa, entra en G4 o queda fuera del alcance de la activación por escrito.
- **Lo que `ReportOnly` no puede mostrar**: `frame-ancestors` se ignora en Report-Only (la app envía además `X-Frame-Options: SAMEORIGIN`, que sí rige); se cubrió con `Enforce` en QA, no en uso real.
- **Cuando ya se activó**: la ausencia de quejas no es evidencia; la evidencia es el log post-activación (§6).

## 5. Ventana y procedimiento paso a paso

**Roles:** *Responsable* (ejecuta; quien conoce el servidor), *Verificador* (usuario del negocio con rol real: recorre y confirma), *Propietario* (autoriza y es el único que puede decidir «continuar»). **Duración prevista de la ventana: 45 min** (activación + verificación + 15 min de observación). Reversión de emergencia en cualquier momento: §6.

Variables usadas (ajustar a la modalidad): `$URL` = `http://127.0.0.1:18787` (local) o `https://<ERP_DOMAIN>` (Docker); `$LOGS` = `D:\csp-obs` (carpeta fuera del repo).

### Antes: durante toda la fase de observación (modo `ReportOnly`, ya activado con otra autorización)

| Paso | Qué | Comando |
|---|---|---|
| O1 | Activar `ReportOnly` (esto **sí** cambia configuración real: requiere su propia autorización; es no bloqueante) | local: `…\ejecutar-erp-local.ps1 -CspMode ReportOnly`; Docker: `CSP_MODE=ReportOnly` + `docker compose up -d --no-deps app` |
| O2 | Centinela el primer día y el último (§2) | consola del navegador |
| O3 | Archivar el log **a diario** (Docker: se rota a los 30 MB) | local: `Copy-Item D:\git\theburyproject1-run\bin\app.log $LOGS\app-$(Get-Date -f yyyyMMdd).log` · Docker: `docker compose logs --no-color -t --since 24h app > csp-obs/app-$(date +%F).log` |
| O4 | Revisar cada semana | `csp-observacion.ps1 -Ruta $LOGS -Desde <inicio> -Hasta <hoy> -ExigirCentinela` |
| O5 | Llenar la planilla de uso (G4/G5) a medida que se usa cada área y equipo | acta |

### Pre-checks (T-1 día y T-0, 15 min, Responsable)

| Paso | Qué | Comando / criterio |
|---|---|---|
| P1 | Todos los GO de §3 verificados (G1–G10) | `csp-observacion.ps1 -Ruta $LOGS -Desde <inicio> -Hasta <hoy> -DiasMinimos 10 -ExigirCentinela` ⇒ **exit 0** |
| P2 | Respaldo y prueba de restauración de las últimas 24 h | local: `scripts\respaldo-local.ps1 -Accion Respaldar …` y `-Accion Probar …` (ver `despliegue-produccion.md` §7.2) · Docker: `scripts/backup/backup.sh all` (ver `backup-restore.md`) |
| P3 | Guardar la configuración previa | local: anotar el comando de arranque vigente (`-CspMode ReportOnly`) · Docker: `cp .env ~/csp/env-previo-$(date +%F-%H%M)` (fuera del repo, permisos 600; **el `.env` tiene secretos**) |
| P4 | **Archivar el log actual** (se perderá al reiniciar) | local: `Copy-Item …\bin\app.log $LOGS\app-pre-activacion.log` · Docker: `docker compose logs --no-color -t app > csp-obs/app-pre-activacion.log` |
| P5 | Humo automatizado del estado actual (requiere `npm ci` y `npx playwright install chromium` en el árbol desde el que se ejecuta; solo lectura, usuario de prueba del propietario; **si no existe un usuario de prueba, no usar uno real: hacer el humo manual de V3**) | `$env:CSP_HUMO_URL=$URL; $env:CSP_HUMO_USER=…; $env:CSP_HUMO_PASS=…; node scripts/ops/csp-humo-lectura.js --esperar reportonly` ⇒ exit 0 |
| P6 | (Docker) validar el `.env` nuevo **antes** de aplicarlo | `sed -i 's/^CSP_MODE=.*/CSP_MODE=Enforce/' .env` · `scripts/deploy/preflight.sh --image "$ERP_IMAGE" --env-file .env --require-db-up` ⇒ sin FAIL (avisa Enforce) |
| P7 | Aviso a los usuarios (§6.3, plantilla A) y confirmar Verificador, Propietario y suplente conectados | — |

### Activación (≈ 1 min de cambio + reinicio de unos segundos; Responsable)

| Paso | Qué | Comando |
|---|---|---|
| A1 | Pedir a los usuarios que guarden y cierren sesión (el reinicio corta las peticiones en curso y se pierde lo que no esté guardado; las claves de Data Protection persisten (`docs/instalacion-servidor.md`), por lo que las sesiones normalmente sobreviven, pero no se probó en el entorno real) | plantilla A |
| A2 | **Cambiar y reiniciar** | local: detener el proceso (`Stop-Process -Id <PID>`; el PID lo imprime el lanzador, o `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" \| ? CommandLine -match 'TheBuryProyect'`) y `powershell -NoProfile -File scripts\ejecutar-erp-local.ps1 -CspMode Enforce` · Docker: `docker compose up -d --no-deps app` (con `CSP_MODE=Enforce` ya en el `.env`) |
| A3 | Esperar a que responda | local: lo verifica el lanzador · Docker: `docker compose ps` (`app` healthy) |
| A4 | **Verificar la cabecera** (debe ser `Content-Security-Policy`, y **no** `…-Report-Only`) | `curl.exe -s -D - -o NUL $URL/Identity/Account/Login \| findstr /i content-security` (Linux: `curl -ks -D - -o /dev/null … \| grep -i content-security`) |

### Verificación inmediata (≈ 15 min; Verificador + Responsable)

| Paso | Qué | Criterio |
|---|---|---|
| V1 | Humo automatizado esperando Enforce | `node scripts/ops/csp-humo-lectura.js --esperar enforce` ⇒ exit 0: cabecera correcta (y sin la otra), 7 pantallas 200 (`/`, Venta, Venta/Create, Cliente, Caja, Crédito, Reporte), **0 violaciones**, 0 errores de página, SignalR conectado |
| V2 | Login real del Verificador, en **su navegador real**, con la consola de desarrollo abierta (Console + Network) | sin errores rojos de CSP («Refused to …» / «violates the following Content Security Policy directive») |
| V3 | **6 pantallas críticas** (humo manual guiado si no hubo V1): Venta/Create (buscar cliente y producto, agregar), Caja (abrir pantalla y movimientos), Crédito, Cliente (abrir y cerrar el modal de alta), Catálogo (abrir el modal de edición de un producto y cancelar), Reportes (abrir un reporte) | cada pantalla carga completa, los botones responden, los modales abren y cierran |
| V4 | **SignalR** | campanita de notificaciones sin error; en Network, un `WebSocket` a `/hubs/notificaciones` (101 y abierto) |
| V5 | **Consola sin errores** en las 6 pantallas | 0 errores CSP |
| V6 | **Impresión y descargas** en el equipo real | Cotización → Imprimir (vista previa) y Volver; un PDF (cotización) y un Excel (Reportes) se descargan y abren |
| V7 | Mirar el log | `docker compose logs --since 15m app \| grep "CSP violation"` / `Select-String "CSP violation" bin\app.log` ⇒ **ninguna** línea propia (con Enforce cada reporte es algo **bloqueado**) |
| V8 | Decisión | todo OK ⇒ el Propietario dice «continuar»; cualquier falla ⇒ **reversión inmediata** (§6) |

### Monitoreo posterior

- **Primeras 24 h:** el Verificador y un usuario de cada rol usan el sistema normalmente; el Responsable corre `csp-observacion.ps1` sobre el log (archivado, con `-Desde <hoy>`) a las **+1 h, +4 h, fin del día y a la mañana siguiente** (`-MaxPropias 0`; sin `-ExigirCentinela` ni `-DiasMinimos`). Pedir a los usuarios que avisen de **cualquier** botón o pantalla que no responda. Una violación propia o una queja = reversión (§6.1) y análisis.
- **Días 2–7:** una corrida diaria del script y un humo (`--esperar enforce`) al inicio de cada jornada; el cierre de caja y el fin de mes dentro de esa semana deben usarse una vez bajo Enforce (G4 de nuevo, esta vez bloqueando). Al día 7 sin violaciones propias ni quejas, se registra «Enforce estable»; la política **no** se relaja ni se agrega `unsafe-inline` para resolver un reporte: se corrige la causa.
- Mantener archivados los logs de la ventana (G9) al menos 90 días.

## 6. Reversión inmediata

### 6.1 Qué síntoma dispara la reversión (sin esperar a analizar)

Volver a `ReportOnly` **de inmediato** ante **cualquiera** de:

1. Una **función bloqueada** (botón que no responde, modal que no abre, pantalla incompleta o en blanco, formulario que no envía) en cualquier rol.
2. **SignalR caído** (sin notificaciones; WebSocket cerrado o con error CSP) en un navegador de G5.
3. **Impresión o descarga rota** (vista previa vacía, PDF/Excel que no llega).
4. **Una o más violaciones propias** en el log después de la activación en una pantalla crítica (bajo Enforce cada una es algo bloqueado).
5. La **app no arranca** o queda en bucle tras el cambio (typo, §1.4): corregir la línea y repetir; si no se resuelve en 5 min, volver al valor previo guardado en P3.
6. Una queja de un usuario que **no se puede descartar en 5 minutos** como ajena a la CSP.

No son disparadores: ruido de extensiones (`chrome-extension:` etc.), avisos informativos de WebKit sobre `frame-ancestors`, ni un error de red de las fuentes de Google sin salida a Internet.

### 6.2 Procedimiento (objetivo ≤ 5 min; medido en el ensayo: cabecera revertida a los 3,6 s y humo `reportonly` OK a los 24 s, archivando el log antes)

1. **Archivar el log del incidente** (para el análisis; se pierde al reiniciar): local `Copy-Item …\bin\app.log $LOGS\app-incidente-$(Get-Date -f HHmm).log` · Docker `docker compose logs --no-color -t app > csp-obs/app-incidente-$(date +%H%M).log`.
2. **Revertir**: local, detener el proceso y `powershell -NoProfile -File scripts\ejecutar-erp-local.ps1 -CspMode ReportOnly` · Docker, `sed -i 's/^CSP_MODE=.*/CSP_MODE=ReportOnly/' .env && docker compose up -d --no-deps app`. Si el problema es la propia CSP en general (no una pantalla), `Off` también es válido y no deja cabecera; `ReportOnly` es preferible porque sigue recolectando.
3. **Verificar**: la cabecera debe ser `Content-Security-Policy-Report-Only` (A4 al revés) y `node scripts/ops/csp-humo-lectura.js --esperar reportonly` ⇒ exit 0. Confirmar con el usuario afectado que la función volvió.
4. **Analizar** con el log archivado (`csp-observacion.ps1 -Ruta <archivo> `): las filas «propias» dicen exactamente qué directiva, qué recurso y en qué pantalla. Corregir la causa en código/plantilla (nunca relajar la política) y **reiniciar G1/G2**.
5. Registrar el incidente en el acta (hora, síntoma, log, causa, corrección).

### 6.3 Comunicación a los usuarios

- **Plantilla A (antes de la activación):** «Hoy a las HH:MM el sistema se reinicia ~2 minutos para activar una protección de seguridad del navegador. Guardá lo que estés haciendo y cerrá sesión antes de esa hora. Después, avisá enseguida si algún botón, pantalla, impresión o descarga no responde como siempre; indicá la pantalla y la hora.»
- **Plantilla B (reversión):** «Volvimos a la configuración anterior del sistema por una incidencia. Volvé a cargar la pantalla (Ctrl+F5) y seguí trabajando. Si algo todavía falla, avisá la pantalla y la hora.»
- **Plantilla C (cierre):** «La protección quedó activa y estable. Si ves algo raro en cualquier pantalla, avisá.»
- Canal: el que use el negocio (WhatsApp/llamado); el Responsable no cierra la ventana sin confirmar que **al menos un usuario por rol** recibió el aviso.

## 7. Ensayo completo en el stack aislado (medido 2026-11-11)

**Entorno:** stack QA aislado `bury-r2csp` (`docker-compose.yml` + `docker-compose.ci.yml` + un override propio que solo fija `Csp__Mode: ${CSP_MODE:-Off}` leyendo `CSP_MODE` del `.env` de QA, la misma expresión que el compose de producción), imagen `theburyproject/erp:r2-csp`, SQL Server sintético con los 4 seeders E2E, `http://127.0.0.1:18240`, Chromium (Playwright), 7 usuarios de prueba. **Límites del ensayo:** un solo navegador (los otros dos y HTTPS/Caddy se cubrieron en rondas anteriores, ver `csp-enforce-pendientes.md`), sin Caddy, usuario sintético `administrador` para el humo, y la modalidad local medida con `dotnet exec` sobre la misma base sintética (no con el lanzador ni LocalDB reales).

| # | Paso | Resultado medido |
|---|---|---|
| 1 | Build de la imagen + levantar el stack en `ReportOnly` | 2 min 4 s (build en frío), cabecera `Content-Security-Policy-Report-Only` |
| 2 | Cadena de reporte con violaciones **provocadas** (`page.evaluate`: `<img>` y `fetch` a hosts externos, `<script>` inline y externo, un centinela, y 7 reportes sintéticos de extensiones/`about`/`data`) | el log de la app registró 13 `CSP violation`; `csp-observacion.ps1` los separó en **5 propias, 7 ruido, 1 centinela** ⇒ exit 1; frecuencia por directiva/URI (sin query: el `?token=SECRETO` no aparece en el resumen)/página/día correctas. Detectó y corrigió un defecto propio: `blocked-uri=about` (sin `:`) se contaba como propia |
| 3 | `csp-observacion.ps1` con **19 casos sintéticos** (ruido de 14 días, centinela, propia en un día, propia fuera/dentro del rango, rango sin logs, log vacío, solo logs informativos con 2 y 5 días mínimos, reporte ilegible, prefijo de Docker, ruta inexistente, fechas invertidas, carpeta, carpeta vacía, `-RuidoExtra`, `-MaxPropias 1`) | 19/19 con el exit esperado (0/1/2/3). Ningún caso de log vacío, ilegible o sin rango da 0 |
| 4 | **Observación simulada A** (tráfico completo, incluidos los escenarios 15 y 17 que provocan violaciones a propósito) | 31 violaciones «propias» ⇒ exit 1: son exactamente las 5 del control negativo (script inline sin nonce, `onclick`, `<style>`, imagen e iframe externos) y las 26 de la prueba de rate limit; **0** provenientes del tráfico normal. Prueba de que el detector no deja pasar violaciones reales |
| 5 | **Observación simulada B (GO)**: contenedor recreado (log limpio), centinela, `crawl.js` (7 roles, **150** páginas, 0 errores, 0 violaciones), `csp-humo-lectura.js` (exit 0), 42 escenarios sin los 2 provocadores (230 checks, 276 interacciones) | **0 violaciones, 0 errores de página, 0 5xx**; `csp-observacion.ps1 -ExigirCentinela -DiasMinimos 1` ⇒ **exit 0 (GO)**: 1 reporte, el centinela. 35/42 escenarios correctos: 6 de los 7 restantes comprueban cosas que solo existen con `Enforce` (cabecera `Content-Security-Policy` en descargas y páginas de error) y fallan por diseño en `ReportOnly`, y 1 (DocumentosConfig) por falta de datos sembrados (ver paso 8); ninguno tuvo violaciones, errores de página ni 5xx |
| 6 | **Activación**: `CSP_MODE=Enforce` en el `.env` + `restart app` | **la cabecera NO cambió** (`restart` no recrea el contenedor) |
| 7 | **Activación**: `docker compose up -d --no-deps app` | retorno a los 3,1 s; login 200 a los **3,95 s**; cabecera `Content-Security-Policy` (y no la Report-Only) |
| 8 | **Verificación bajo Enforce**: humo `--esperar enforce`; `crawl.js` con el medidor; `escenarios.js --modo enforce` | humo exit 0 en 19 s; crawl **164** páginas × 7 roles, **0** violaciones, 0 errores (109 s); escenarios **44/44** correctos, 245 checks, 327 interacciones, **0** violaciones, 0 `pageerror`, 0 errores de consola, 0 5xx (322 s). Un escenario falló primero (DocumentosConfig sin datos: la base no tenía plantillas/paquetes/reglas) y pasó 15/15 tras `--configurar-documentos`: fallo cerrado por falta de precondición, no de política. En el log, las únicas «violaciones propias» son las del control negativo y el rate limit (los escenarios que las provocan a propósito) |
| 9 | **Fallo provocado bajo Enforce** (recrear `app`, provocar `<script>` inline, `<script src>` y `<img>`/`fetch` externos) | **bloqueado de verdad**: el script inline no se ejecutó (`window.__x` quedó `undefined`); 4 reportes con `disposition=enforce`; `csp-observacion.ps1` ⇒ **exit 1**, directiva/URI/página listadas |
| 10 | **Reversión** a `ReportOnly` (archivar log + editar `.env` + `up -d --no-deps app`) | log archivado en 0,5 s; cabecera `Content-Security-Policy-Report-Only` a los **3,6 s**; `csp-humo-lectura.js --esperar reportonly` exit 0 a los 24 s |
| 11 | **Off** (`CSP_MODE=` vacío) | sin cabecera CSP; `/csp-report` 404; login 200 a los 2,8 s |
| 12 | **Typo** `CSP_MODE=Enforse` | la app no arranca: bucle de reinicio cada ≈ 2,6 s, HTTP 000, el log dice `Failed to convert configuration value 'Enforse' at 'Csp:Mode'`; corregido a `ReportOnly` y `up -d`: login 200 a los **2,4 s** |
| 13 | Modalidad local (`dotnet exec` con `Csp__Mode`, puerto 18241) | `ReportOnly` y `Enforce` aplican la cabecera correspondiente; detener 1,1 s; arrancar 4,0 s y 6,9 s; typo `Enforse`: el proceso termina al arrancar (≈ 15 s) con el mismo mensaje; `-CspMode Enforse` del lanzador es rechazado por la validación antes de ejecutar nada |
| 14 | Contrato de configuración | 4 tests nuevos (`CspActivacionConfiguracionContractTests`): compose pasa `Csp__Mode` solo a `app` con `Off` por defecto, `.env.example` lo trae apagado, preflight y lanzador validan; mutación (quitar la línea del compose) ⇒ falla el test correspondiente. `CspHttpTests` (23) intactos: 27/27 |

Evidencia cruda (fuera del repo): `D:\tmp\mision\r2csp\` (logs `app-*.log`, resúmenes `obs-*.txt` y `enf-*.txt`, `esc-*.json`, casos sintéticos en `sint\`, provocador `provocar.js`, tráfico `obs-traffic2.sh` y `enf-traffic.sh`).

## 8. Herramientas entregadas

| Archivo | Para qué | Seguridad |
|---|---|---|
| `scripts/ops/csp-observacion.ps1` | Resume el log de la ventana y da el GO/NO-GO de G2/G3/G1 (exit 0/1/2/3) | solo lectura |
| `scripts/ops/csp-humo-lectura.js` | Humo de verificación contra el entorno real o QA: cabecera esperada, 7 pantallas, violaciones, errores, SignalR | solo lectura: aborta todo no-GET (salvo login y el *negotiate* de SignalR); credenciales por variables de entorno; se detiene si el usuario no aceptó los términos (el modal escribe) |
| `scripts/ejecutar-erp-local.ps1 -CspMode` | Fija `Csp__Mode` para la app local con validación del nombre | cambio de código del lanzador, sin efecto si no se usa el parámetro |
| `docker-compose.yml` (`Csp__Mode: ${CSP_MODE:-Off}`), `.env.example` (`CSP_MODE=Off`) | Hace operable el cambio por `.env` en el despliegue Docker (antes `app` no recibía la variable) | por defecto `Off` = comportamiento histórico |
| `scripts/deploy/preflight.sh` | Rechaza un `CSP_MODE` inválido antes del deploy | solo lectura |
| `scripts/qa/csp-roles/`, `scripts/qa/csp-medicion/` | Recorrido por roles, escenarios y gate (solo stack QA aislado) | ya existentes |

## 9. Qué hay que preguntarle al propietario (bloquea G5/G11)

1. ¿Qué **navegadores y versiones** se usan realmente (Chrome, Edge, Firefox, Safari)? ¿En qué **sistemas** (Windows, macOS, Linux) y qué **dispositivos** (PC, notebook, tablet, celular iOS/Android)? ¿Alguien accede desde un celular o tablet?
2. ¿Se accede solo desde el equipo del servidor (`http://127.0.0.1:18787`) o también desde otros equipos de la red/VPN? ¿Se va a pasar a Docker + Caddy antes de activar? (la ventana debe hacerse en la modalidad definitiva).
3. ¿Qué **extensiones** corporativas (antivirus web, proxy, traductores, gestores de contraseñas) tienen instaladas los equipos?
4. ¿Cómo se **imprime** (impresora térmica, láser, «guardar como PDF»)? ¿Se usan las pantallas de impresión (cotización, factura) o solo PDF?
5. ¿Hay conexión a Internet en el equipo servidor y en los clientes (fuentes de Google, Mercado Libre)? ¿Se usa **Mercado Libre**?
6. ¿Quiénes usan cada **rol** y quién puede actuar como Verificador y suplente? ¿Existe un **usuario de prueba** del propietario para el humo automatizado (si no, el humo es manual)?
7. **Ventana**: ¿qué día y horario (fuera de la apertura/cierre de caja), y cuántos días de observación acepta (propuesta: ≥ 10 hábiles que incluyan un cierre de mes)?
8. ¿Dónde se guardan las copias del acta, los logs archivados y el `.env`/comando previo (fuera del repo)?
