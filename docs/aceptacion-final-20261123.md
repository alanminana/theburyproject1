# Informe de aceptación final (2026-11-23)

Base: `main` 647207c1 (#358), actualizada el 23/11 (noche) a `main` f39a8416 (#359 a #363 fusionadas; #364 en revisión). Esta PR es solo documentación. El informe original de la mañana se hizo en solo lectura; las comprobaciones de la noche (despliegue en `theburyproject-prod`, §5; pruebas descartables, §12b) se describen donde corresponden y **no** tocaron preproducción ni datos reales. No reabre módulos cerrados: consolida lo ya decidido y probado.

**Convención de evidencia**
- **[V]** verificado en esta pasada (git/gh, lectura de documentos, consulta a la VM; en la actualización de la noche del 23/11 también el despliegue y las pruebas sobre `theburyproject-prod`, descritos en §5).
- **[R]** reportado por documentos o agentes anteriores; no re-ejecutado aquí. Las observaciones hechas por otro agente, aunque se hayan visto en la VM, se marcan [R].
- **[P]** pendiente / no verificable hoy.

## 1. Respuestas directas

| Pregunta | Respuesta |
|---|---|
| ¿Terminado el desarrollo funcional inicial? | **Sí para el alcance de contado y administración** [R, documentado en `aceptacion-erp.md`, `estado-software-pre-instalacion.md`, `hitos-d01-d03…` §16-18]. Los PR #156-#358 están fusionados en `main` [V: `gh pr list`]. **No** está terminado lo diferido (§6) ni crédito, que se mantiene cerrado por decisión. |
| ¿Qué está validado? | Pruebas automáticas, QA en clones y preproducción sintética (§3). **Nada se validó con datos reales de producción.** |
| ¿La nueva instalación funciona? | **Sí, técnicamente [V] (23/11, noche):** `theburyproject-prod` corre la imagen de `main` f39a8416, con 145 migraciones, health 200, login real, Fiscal V2 sin cambios y verificadores sin FALLA (§5). **No está lista para operar**: faltan 2 SuperAdmin reales, datos de empresa/cajas/medios de pago y pruebas humanas con datos reales (P-07). |
| ¿Qué falta para operar con datos reales? | §5. |
| Riesgos aceptados | §6. |
| Hitos diferidos | §7. |
| Condiciones para cerrar el proyecto completo | §8. |

**No se declara el proyecto al 100 %.** Hay hitos abiertos (P-04, P-06 respaldo independiente, P-07/P-02 operativos, D-05, validación legal de la cláusula de mora y seguimientos diferidos de P-08).

## 2. Estado por hito (sin inferencias)

| Hito | Desarrollo | Técnicamente listo | Operativo | Fuente |
|---|---|---|---|---|
| D-01 listas de precios/costeo | Finalizado | Sí | — | `hitos…` §16.4 [R] |
| D-02 referencias/crédito/autorizaciones | Finalizado funcional | Sí (reservas: lector de pantalla real) | — | §16.4 [R] |
| D-03 Tesorería/anticipos/cheque único | Finalizado | Sí | Orígenes de fondos y cajas reales sin cargar | §17 [R] |
| V-02 cheques informativos | Finalizado (alcance informativo) | Sí | — | §18 [R] |
| P-02 fiscal V2 | Implementado y probado | Sí (instalador con `--fiscal-v2-desde`, verificador SQL) | Instalación e instante fiscal **verificados** en `theburyproject-prod` (§5, control 4); **pendiente** la aceptación con operación real (0 ventas V2 hasta hoy). IVA de punitorio con alícuotas mixtas sin tratamiento (contador) | `p02-*`, `cierre-administrativo-20261121` [R] |
| P-03 seguridad de usuarios | Verificado (software) | Sí | Cuentas reales (2 SuperAdmin) sin crear | `estado-software-pre-instalacion` [R] |
| P-04 servidor definitivo | Diferido (diagnóstico ampliado 23/11: `p04-diagnostico…` §5; sin cambios en el anfitrión) | Arnés Win11/Ubuntu fusionado | **No hay servidor físico**; solo la VM `tbp`, que es de la PC de desarrollo | `puesta-en-marcha-estado-20261123` [R] |
| P-05 acceso remoto | Cierre técnico local | Sí | Tailscale real sin confirmar | `p05-cierre-tecnico-local` [R] |
| P-06 respaldos | Local cifrado FINALIZADO (#347); aislamiento prod/preprod fusionado (#362, `f39a8416`); general **En progreso** | Sí | **Sin respaldo independiente del anfitrión** (riesgo aceptado, no mitigado); full automático del 24/11 03:45 **sin verificar**; claves LUKS y paquete de recuperación solo dentro de la VM; remontaje tras reinicio sin probar | §2b, `cierre-administrativo` §2 [R] |
| P-07 puesta en marcha | Técnico listo | Sí (verificadores con exit≠0, 145 migraciones, config inicial validada en QA #354) | **Pendiente** | `p07-configuracion-inicial-checklist` [R] |
| P-08 monitoreo | **Finalizado en el tablero maestro** (informado por el agente principal; no re-verificado aquí) | Sí | Cierre con excepción documentada. Seguimientos **diferidos, no criterios de cierre**: rotar URL de heartbeat; confirmar DOWN/UP externo; token/chat_id Telegram | `cierre-administrativo` §6 [R] |
| D-05 CSP | Preparado y medido: 0 violaciones en 7 roles × 3 tamaños con `Enforce` en stack QA | Sí | **No activado**; falta observación `ReportOnly` en uso real (10 días hábiles) | `csp-enforce-*` [R] |
| Crédito personal | Implementado | — | **Cerrado**: cupos $0, sin planes, montos financiables $0 (#349) | `cierre-credito-cupos-cero-20261122` [R] |

Nota CI: **confirmado externamente** (informado por el agente principal; no consultado por mí tras la primera lectura) que los workflows de `main` 647207c1 finalizaron en verde, incluidos CI, Playwright y FIN SQL Server. En mi consulta inicial estaban en curso.

### 2b. P-06: evidencias del cierre técnico de dos instalaciones (23/11/2026, noche)

Fuente: agente P-06 (PR #362 fusionada en `main` `f39a8416`; `docs/p06-cierre-tecnico-dos-instalaciones.md`). Las pruebas ya aprobadas se citan, no se repiten.

| Control | Evidencia | Tipo |
|---|---|---|
| Proyecto Compose correcto | `COMPOSE_PROJECT_NAME=theburyproject-prod` en el `.env` de prod; `docker compose` desde el clon de prod solo ve `theburyproject-prod`; el guard `assert_backup_dir_matches_project` fusionado en #362 | [R] por el agente P-06 (ejecutó y observó en la VM; no re-ejecutado en esta pasada) |
| Respaldos aislados | El `db` de prod monta `/srv/bury-backups-prod/sql` y el de preprod `/srv/bury-backups/sql`; los logs de cada instalación no mencionan el directorio de la otra; `bury-backup-prod-errors.log` vacío | [R] |
| Cron de producción cada 15 min | Logs de las 20:37 y 20:52 (hora VM, -03) con `dir=/srv/bury-backups-prod`, `exit 0`, `verified true`, `verifyonly=OK`, `sha256=OK`; full del 23/11 15:30 con SHA256 OK | [R] |
| Monitores | `bury-monitor` (preprod) y `bury-monitor-prod` activos; el de prod con configuración, estado y heartbeat propios (sin heartbeat compartido) y vigilando solo `/srv/bury-backups-prod`; sin alertas activas en prod. Preprod conserva activas `backup.offsite-sql/files` (offsite no configurado; preexistente) | [R] |
| Relay | `monitor.py --test-channel` (prod): evento sintético firing + resolved "prueba de canal: no es un incidente real" entregado al relay operativo; `delivery.failed=false` en ambos monitores. Prueba de falla/recuperación de respaldos hecha en un directorio temporal con receptor local (no tocó prod ni el relay) | [R] |
| Paquete cifrado de recuperación | Creado con `recovery-package.sh` y verificado (descifrado autenticado, sha256 OK, paquete alterado rechazado); contiene `.env` y manifiesto; **todavía dentro de la VM** (`~/p06-custodia-prod/{paquete,clave}`, directorios distintos). **No hay custodia externa**: no cuenta como respaldo independiente | [R] |
| Restauración SQL aislada | Aprobada previamente (respaldo real de prod restaurado en SQL descartable, 16 s, base sin datos de negocio; no extrapolar tiempos) | [R] (#362) |
| Preproducción intacta | Contenedores sanos, monitor activo, su cron y su `log.json` con horario propio; no se modificó su LUKS ni su configuración. No se auditó el contenido de sus datos | [R] |
| Espacio | `/` 99 GB, 70 GB libres (27 %); respaldos de prod 37 MB, de preprod 175 MB; `BACKUP_MAX_TOTAL_GB` y `BACKUP_MIN_FREE_GB` siguen en 0 (sin datos reales de negocio no hay medición que justifique un límite) | [R] |

**Observado [R] después (23/11, 22:21 hora VM; hecho por otro agente, no por P-06; visto en `.env`, `monitor-status` y log, no re-ejecutado):** `BACKUP_REMOVABLE_DIR=/mnt/tbp-respaldo-prod` (volumen LUKS `tbp-respaldo-prod`, mismo host); `removable-copy.json` y `removable-verify.json` existen y el log registra «33 archivos sha256 OK». Es una segunda copia **local**: **no cumple** el criterio de respaldo independiente del anfitrión.

**Verificación del full sin intervención:** en la VM quedó un timer transitorio de solo lectura (`p06-verifica-full`, 24/11 03:55) que escribe `~/p06-verificacion-full-<fecha>.txt` con `full.json`, líneas del log, `sha256sum -c`, `RESTORE VERIFYONLY` independiente, aislamiento y estado de monitores. Probado en seco el 23/11 (verifyonly del full del 23/11 15:30: «The backup set on file 1 is valid», SHA256 OK).

**[P] Pendiente de verificación:** full automático de `theburyproject-prod` del **24/11/2026 03:45 (hora argentina)**. Al redactar, el reloj de la VM marcaba 23/11 21:17 y el full no había corrido. Para cerrarlo, comprobar: `monitor-status/full.json` (`exit 0`, `verified true`, `time` posterior a las 03:45), líneas del full en `logs/backup.log`, `sha256sum -c` del `.bak` nuevo y `verifyonly=OK`. No ejecutar un full manual ni reiniciar servicios.

**Estado de P-06: EN PROGRESO.** No se declara Finalizado: falta el criterio de respaldo independiente del anfitrión. El propietario decidió continuar sin nube ni disco externo físico; la pérdida total del equipo es un **riesgo aceptado temporalmente, no mitigado** (decisión cerrada, no se vuelve a abrir). Los dos SuperAdmin reales corresponden a P-07 y no se mezclan con P-06.

## 3. Evidencia disponible: sintética vs navegador real vs producción

| Nivel | Qué hay | Qué NO es |
|---|---|---|
| Sintético (tests, SQL Server efímero) | Suite 8791 OK / 0 fallos / 253 omitidas por entorno (corren en CI) [R]; concurrencia RCSI 0 y 1; instalación limpia 145 migraciones, 37 verificaciones [R] | No prueba comportamiento con datos reales |
| Navegador real sobre clon/preproducción | Playwright 1366/1280/390; axe 0 violaciones; recorridos de ventas, caja, compras, devoluciones, cheques en clones de la base real (Hitos 9-11) [R] | No es producción ni uso real |
| QA por perfil (#356) | Accesos permitidos y denegaciones por rol sin modificar datos [R] | — |
| Producción real | **Ninguno.** | — |

## 4. Matriz mínima de aceptación (reutiliza evidencia; sin repetir)

| Área | Estado | Evidencia a reutilizar | Falta en la instalación nueva (intervención humana) |
|---|---|---|---|
| Autenticación | Aprobado | P-03, `TransicionCuentasCookieHttpTests`, bootstrap Production | Login real con el administrador inicial; rotación/baja de `admin` y cuentas demo |
| Usuarios y permisos | Aprobado | Barrido de 506 acciones × 10 identidades; #353, #356; `p07-verificar-cuentas.sql` | Alta de ≥ 2 SuperAdmin y personal con rol; verificador con `@Cierre = 1` |
| Alta de clientes | Aprobado con limitaciones | #162, #202; alta fija puntaje 1 (cupo 0) | Un alta real. BCRA con tope 6 s (provisorio) |
| Productos y existencias | Aprobado | Stock = Σ movimientos; Kardex sin cortes | Carga inicial real de productos y stock |
| Ventas de contado | Aprobado con limitaciones | Cotización → venta → confirmar → facturar → caja; #165/#182/#203 | Una venta de contado real de punta a punta; medios de pago reales (recargos no aprobados) |
| Caja y movimientos | Aprobado con limitaciones | Concurrencia SQL, cierres/arqueo (invariantes 0 violaciones) | La instalación nueva trae **0 cajas**: crear cajas; un cierre real |
| Compras | Aprobado con limitaciones | QA OC, recepción parcial; #350 | Costo manual (costeo: decisión abierta); una OC real |
| Proveedores | Aprobado | #351 (baja con deuda rechazada), D-03 | Alta de proveedores reales |
| Tesorería | Finalizado | D-03, `TesoreriaSqlServerTests` | Orígenes de fondos reales (hoy 0) |
| Facturación V2 | Implementado | P-02, #355 (tarjeta V2 con servicios exactos), `p02-verificar-instalacion.sql` | Fecha V2 persistida: **verificada** en `theburyproject-prod` (§5, control 4); aceptación operativa pendiente. Facturación fiscal legal es externa al ERP (decidido); el ERP emite comprobante interno |
| Reportes económicos | Aprobado con limitaciones | Ventas, Márgenes, IVA contrastados con SQL | IVA sin netear devoluciones, costo con IVA, envío/armado y recargo fuera de la ganancia: contador |
| Respaldos | Aprobado con limitaciones | Full diario, logs 15 min, `verifyonly` OK, copia local LUKS [R] | **Sin offsite**; claves fuera de la VM |
| Recuperación | Aprobado con limitaciones | Restauración full + 67 logs, `CHECKDB` OK, 44 s desde copia externa en ensayo [R] | Restauración sobre la instalación real; remontaje tras reinicio |

## 5. Validación de la nueva instalación (Sección C): estado

**Actualización 23/11 (noche).** La instalación independiente **existe**: proyecto Compose `theburyproject-prod` en la VM `tbp` (https://tbp-prod:8443, respaldos en `/srv/bury-backups-prod`, cron y firewall propios), instalada sobre `main` 647207c1. Es una instancia **preoperativa**: 0 ventas, 0 clientes, 0 créditos, 0 movimientos de caja, 0 facturas y 1 sola cuenta (`admin`). Preproducción (`theburyproject`) convive en la misma VM y no se tocó.

**Despliegue de la imagen vigente [V].** La imagen activa era `20261123-647207c1` (sin #363). Se construyó `theburyproject/erp:20261124-f39a8416` (árbol limpio, sin `-dirty`; id `10b6ac962b2d`) y se desplegó **solo** `theburyproject-prod` con `scripts/deploy/deploy.sh --backup skip` (sin operaciones reales que respaldar; el último full verificado es del 23/11 15:30 y los logs corren cada 15 min). Resultado: preflight OK, `db-init` OK, `migrate` **145 aplicadas, 0 pendientes**, health live/ready 200, smoke OK (`/Account/Login` 200, `AspNetUsers`=1). Identidad efectiva: `app` y `migrate` ejecutan la imagen nueva y el `md5` de `TheBuryProyect.dll` del contenedor coincide con el de la imagen. Preproducción: los tres contenedores conservan id, hora de arranque e imagen (snapshot antes/después idéntico) y su `health/ready` responde 200.

| # | Control | Cómo | Estado |
|---|---|---|---|
| 1 | Healthchecks | `docker ps` healthy; `live`/`ready` 200 en el gate de `deploy.sh`; monitor de prod sin alertas | **[V]** |
| 2 | Migraciones | `migrate`: 145 aplicadas, 0 pendientes; sin migración nueva en #359-#363 | **[V]** |
| 3 | Verificadores SQL | `p02-verificar-instalacion.sql`, `p07-verificar-credito-cerrado.sql`, `p07-verificar-cuentas.sql` con `sqlcmd -I -b`: los tres **exit 0, «sin FALLA»**. `p07-verificar-cuentas` ejecutado **sin** `@Cierre = 1` (informativo): marca 1 SuperAdmin de ≥ 2 | **[V]**; con `@Cierre = 1` **[P]** (P-07: requiere 2 SuperAdmin reales) |
| 4 | Fecha fiscal V2 | `FISCAL_CRITERIO_V2_DESDE=2026-11-23T18:16:50Z` en `.env` = `Fiscal__CriterioV2Desde` del contenedor = log `[FISCAL] … = V2`. **No se modificó** | **[V]** |
| 5 | Seguridad | `ASPNETCORE_ENVIRONMENT=Production`; HTTPS con CA propia de Caddy; login real de `admin` (302, cookie `Identity.Application`, Dashboard 200) y clave incorrecta rechazada (200 sin cookie); 0 cuentas sintéticas/demo; **`CSP_MODE=Off`** (D-05 no activado) | **[V]**; 2 SuperAdmin **[P]** (P-07) |
| 6 | Datos de prueba vs operativos | 0 cuentas sintéticas activas; 0 ventas/créditos/facturas/caja; preproducción sintética no usada como productiva | **[V]** |
| 7 | Pruebas con intervención humana | Login de administrador hecho **por HTTP** (no sustituye la prueba manual en navegador). Falta: una venta de contado real, un cierre de caja, una restauración de backup elegida por el propietario, rotación de credenciales | **[P]** (datos comerciales reales) |
| 8 | Crédito cerrado | Verificador: 0 créditos, montos máximos financiables > 0 = ninguno, 0 overrides, 0 planes activos; punitorio vigente 0 % / 30 días / gracia 12 (desde 2026-11-23). Cupos configurados: 6 puntajes con cupo informativo, **ningún monto financiable** | **[V]** |
| 9 | Monitores | `bury-monitor-prod` y `bury-monitor` activos; durante el despliegue **0 alertas** y `delivery.failed=false`; prueba de canal al relay entregada (§2b) | **[V]** |

Bloqueos de instalación ya resueltos: la VM se usó con autorización; correo/usuario del administrador inicial existen; directorio de respaldos propio (`/srv/bury-backups-prod`); firewall parametrizable (#358) en uso (`bury-firewall-prod.service`, cadena `BURY-ERP-PROD`) y verificado.

## 6. Riesgos aceptados (no mitigados)

1. Pérdida total del PC (robo, disco, ransomware, borrado de la VM): **sin respaldo independiente del anfitrión** (ni nube ni disco externo). **Aceptado temporalmente por el propietario; no mitigado; decisión cerrada.** Incluye el paquete cifrado de recuperación de producción, que sigue dentro de la VM, y la copia local cifrada de prod (`tbp-respaldo-prod`), que vive en el mismo host.
2. Claves LUKS (`local-disk.key`, `luks-header`) dentro de la VM: perdidas = respaldo local irrecuperable.
3. Anfitrión sin endurecer (UPS, BIOS, Windows, autoarranque, Wi-Fi USB como único enlace, Ethernet desconectado): P-04 diferido. Cierres no limpios: `0x133` causados por `vgk.sys` (Riot Vanguard, 3 de 3 con dump) y `0x50` con `athurx.sys` solo como sospecha de WER (ver `p04-diagnostico-bugcheck-20261123.md` §5). **Aceptado, no mitigado.**
4. Una sola instancia de la app; hora de logs en UTC (esperado).
5. Lector de pantalla real (NVDA/JAWS/VoiceOver) no verificado.
6. IVA de punitorio con alícuotas mixtas, interés de financiación y recargo: tratamiento fiscal sin validar (contador). Facturación fiscal externa.
7. Interés por mora `TasaMoraBase`: unidades contradictorias (diario vs mensual); contado no afectado, crédito cerrado sin exposición. **Decisión tomada:** contrato y pagaré alineados con el punitorio vigente (hoy 0 %) y sin estimaciones monetarias inconsistentes en alertas. **Implementación: PR #363 fusionada y desplegada (§10b). Validación funcional sobre un contrato real: NO realizada** (§12b: tramo crédito→contrato NO VALIDADO) y redacción legal pendiente; mientras no haya esa evidencia el riesgo sigue abierto y crédito debe permanecer cerrado.

## 7. Funcionalidades diferidas

Crédito personal (abierto solo por decisión posterior); facturación electrónica ARCA/AFIP; cheques propios y endosos; seguimiento de acreditación de cheques (retirado en V-02); saldos por origen/arqueo/conciliación bancaria; exportación del estado de cuenta; R-08 (rol Gerente en cuentas por pagar, manual); R-11 (RMA → NC de proveedor); Mercado Libre (fuera de alcance; defecto DEF-ML-01 registrado); Referencias de clientes: pestaña oculta o implementada según D-02 (confirmar vigente); módulo de lotes por listas más allá del cambio directo; pantalla de sucursales (solo «Casa Central»).

## 8. Decisiones comerciales abiertas del propietario

1. Servidor de instalación (físico P-04 / VM / reemplazo de preproducción) y autorización del corte.
2. Identidad del administrador y de los 2 SuperAdmin; custodia de sus claves y de las claves LUKS.
3. Datos reales de empresa (CUIT, domicilio, vencimiento de pagaré, condición fiscal por defecto).
4. Sucursales: ¿alcanza «Casa Central»?
5. Orígenes de fondos y cajas reales.
6. Medios de pago con recargos/descuentos (−5 %/+3 % no aprobados).
7. Precios/costos de envío y armado (hoy $0).
8. Método de costeo de compras (manual hoy).
9. Instante `--fiscal-v2-desde`.
10. Aceptar o mitigar la falta de offsite antes de datos reales.
11. Seguimiento diferido de P-08 (no bloquea su cierre): Telegram (token/chat_id), rotación de URL de heartbeat y observador externo.
12. ~~Interés por mora (A/B/C)~~ decidida; falta evidencia de implementación (§6.7).

## 9. Criterios para declarar el ERP **listo para operar** (contado inicial)

Todos deben cumplirse y quedar evidenciados:

- [ ] CI de `main` verde sobre el SHA instalado.
- [ ] Instalación productiva independiente de la preproducción, con healthchecks OK.
- [ ] 145 migraciones aplicadas; los 3 verificadores SQL con exit 0.
- [ ] Fecha fiscal V2 persistida coincide con la autorizada.
- [ ] ≥ 2 SuperAdmin reales; 0 cuentas demo/sintéticas; clave de `admin` rotada o cuenta desactivada.
- [ ] Datos reales de empresa, cajas y orígenes de fondos cargados.
- [ ] Una venta de contado real de prueba (con anulación/devolución controlada) y un cierre de caja cuadrado.
- [ ] Respaldo diario ejecutado en la instalación nueva, restauración verificada, claves custodiadas fuera de la VM.
- [ ] Decisión explícita del propietario sobre offsite (mitigar o aceptar por escrito).
- [ ] Crédito verificado cerrado (`p07-verificar-credito-cerrado.sql`).

## 10. Criterios adicionales para cerrar el **proyecto completo**

P-04 con servidor físico y anfitrión endurecido; P-06 con offsite real y remontaje probado; seguimientos diferidos de P-08 (canal externo, DOWN/UP, rotación de URL); D-05 con 10 días hábiles en `ReportOnly` real y decisión de `Enforce`; decisiones del contador (IVA/NC/redondeo) cerradas; apertura o descarte definitivo del crédito; revisión de lectores de pantalla.

## 10b. Estado de la validación de la instalación (actualizado 23/11, noche)

La instalación independiente `theburyproject-prod` está **instalada, desplegada con la imagen de `main` f39a8416 y verificada técnicamente** (§5, controles 1-6, 8 y 9 **[V]**). **No** está lista para operar: faltan los controles 3 y 5 con `@Cierre = 1` (2 SuperAdmin reales), el control 7 (pruebas humanas con datos comerciales reales) y los datos de empresa, cajas/orígenes y medios de pago reales (P-07, a cargo del propietario). Ningún criterio de §9 se marca cumplido por anticipado y el ERP no se declara al 100 %. Esos pendientes **no** se mezclan con P-06.

### PR fusionadas relevantes (#359-#363) y PR en revisión (#364)

| PR | Contenido | Estado | Desplegado en `theburyproject-prod` |
|---|---|---|---|
| #359 | P-04: matriz de continuidad y auditor del anfitrión (H07-H09) | Fusionada | Solo documentación y scripts de host; no aplica a la imagen |
| #360 | D-05: sanear log de `/csp-report` y preparar el cierre (sin activar CSP) | Fusionada | **Sí** (código en la imagen `f39a8416`); `CSP_MODE=Off` |
| #361 | `install.sh`: la imagen desplegada no puede ser sustituida por `erp:local` | Fusionada | Corrige el instalador; la imagen activa se verificó por id y md5 |
| #362 | P-06: aislar respaldos de dos instalaciones en la misma VM | Fusionada | Scripts del clon de prod actualizados a `f39a8416` |
| #363 | **Mora contractual A+B** (ver abajo) | Fusionada | **Sí** (código en la imagen `f39a8416`) |
| #364 | P-06: instalador de segunda instancia del monitor y corrección de la documentación obsoleta | **En revisión** (no fusionada al redactar) | No aplica: `bury-monitor-prod` ya está instalado manualmente y **no se reinstala** |

### Corrección contractual A+B (#363): código integrado vs. despliegue verificado

- **Integrado en `main`** [V por `gh`/`git`]: interés por mora del contrato y del pagaré = punitorio **vigente** (`ConfiguracionPunitorio`), 0 % válido, sin respaldos en `TasaMoraBase`/5 %/0,05 %/0,20 %, sin emitir si no hay configuración activa; las alertas dejan de mostrar importe de mora estimado (se conservan capital vencido, cuotas y días de atraso). Sin migración EF, no abre crédito, no fija tasas, no toca operaciones históricas.
- **Desplegado** [V]: la imagen en ejecución `20261124-f39a8416` contiene #363 (ancestro de `f39a8416`; identidad del contenedor comprobada).
- **Probado sobre la instalación real:** **no se puede**: el crédito está cerrado y la base no tiene créditos; el verificador de crédito cerrado pasa y el punitorio vigente es 0 %. **Validación funcional del contrato pendiente:** en el ambiente descartable el tramo crédito→contrato quedó NO VALIDADO (§12b); solo existe la prueba unitaria reportada de #363. 
- **Acción pendiente en base real (de #363):** actualizar el texto de las **plantillas de contrato ya cargadas** (no se modifican por código) y la redacción legal de la cláusula (propietario/asesor).

## 11. Notion

No hay conector de Notion disponible en esta sesión: **no se actualizó**. Texto listo para pegar, solo con estados respaldados por evidencia:

| Hito | Estado propuesto | Respaldo |
|---|---|---|
| Instalación `theburyproject-prod` | **Instalada y verificada técnicamente; no operativa** | §5 (imagen `f39a8416`, 145 migraciones, verificadores exit 0) |
| P-02 | **En progreso** (V2 activa en la instancia, 0 ventas V2; falta aceptación con operación real) | §5 control 4 |
| P-04 | **En progreso / diferido** | diagnóstico §5; sin servidor físico |
| P-06 | **En progreso** (falta respaldo independiente; riesgo aceptado, no mitigado; full automático del 24/11 03:45 por verificar) | §2b |
| P-07 | **En progreso** (faltan 2 SuperAdmin, datos de empresa, pruebas humanas) | §5 controles 3, 5, 7 |
| D-05 | **Preparado, no activado** (`CSP_MODE=Off`) | §5 control 5 |
| Mora contractual A+B (#363) | **Integrado y desplegado; validación funcional (contrato real) y legal pendientes** | §10b, §12b |

## 12b. Pruebas fiscales y comerciales en ambiente descartable (23/11, noche)

Entorno: pila **propia y descartable** de `scripts/qa/p07/run-p07.sh` (proyecto Compose aleatorio, SQL Server de contenedor, datos 100 % sintéticos, imagen construida desde `main` f39a8416), en esta PC, **sin tocar** `theburyproject-prod` ni preproducción; al terminar se destruyó (0 contenedores `buryp07*`). No se registró ninguna venta ni factura ficticia en la instancia reservada.

| Resultado | Detalle |
|---|---|
| **108 comprobaciones OK, 0 FALLA** [V] | R0 3, R1 8, R2 16, R3 4, R4 4, R5 11, R6 9, R7 10, R8 11, R9 5, R10 18, R11 9: salud, cliente, compras/stock, caja, cotización→venta→factura, venta directa y cancelación, devoluciones y notas de crédito, configuración y documentación de crédito, cierre de caja, conciliación entre módulos y matriz de permisos |
| **[NO VALIDADO] tramo crédito → contrato → cobros** | La conversión a crédito en un ambiente sin consulta BCRA/Veraz deja la venta **pendiente de autorización** (razón «No se pudo validar BCRA/Veraz») y **sin crédito**: es el fail-safe vigente (el crédito se crea al guardar la edición de la venta una vez autorizada), no un defecto. El recorrido original no modela ese paso, así que **no se ejecutaron** generación de contrato/pagaré con punitorio 0 %, plan de cuotas y cobros. **No hay evidencia de ejecución de la corrección A+B de #363 sobre un contrato real**; solo su prueba unitaria reportada (9209 OK en la PR). |
| Las conciliaciones finales del recorrido (tras R11.9) no corrieron | El script aborta al depender del crédito |

Acciones sobre el script de QA (local, **no incluidas en esta PR**): el recorrido necesita (a) autorizar la razón BCRA y (b) guardar la edición de la venta para materializar el crédito; luego revalidar contrato con punitorio 0 %.

## 12. Defectos reproducibles hallados en esta pasada

Ninguno nuevo en `main`. Hubo pruebas sintéticas parciales (108 OK, 0 FALLA, §12b); el tramo crédito→contrato→cobros quedó NO VALIDADO. El recorrido de QA necesita un paso de autorización BCRA y de guardado de la venta (script, no aplicación).
