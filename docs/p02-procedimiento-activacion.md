# P-02: procedimiento de puesta en marcha — activar CriterioFiscal V2 y aplicar P3 (NC fiscal externa)

> Histórico: la VM `tbp` quedó fuera del plan el 2026-11-24; este documento conserva la evidencia tal como se emitió.

Fecha: 2026-11-20. **P3 y P6 AUTORIZADAS por el propietario (2026-11-20).** Estado: **mecanismos implementados y ensayados en un stack aislado (§6); aplicación real PENDIENTE de la puesta en marcha sobre la base nueva de producción** (ver §7: la base de la VM es de preproducción con datos sintéticos y no se usa como productiva). Complementa `docs/deploy-rollback.md`, `docs/despliegue-produccion.md`, `docs/backup-restore.md` y `docs/p02-respuestas-contador-20261119.md`.

Scripts de apoyo (todos validados contra un SQL Server descartable el 2026-11-20):

| Script | Qué hace | Modifica datos |
|---|---|---|
| `scripts/sql/p02-verificar-instalacion.sql` | Controles antes/después (esquema, instalación limpia, versión fiscal, consistencia Neto + IVA = Total, facturas, NC fiscales). | **No** (solo lee; usa la tabla temporal `#r` de tempdb) |
| `scripts/sql/p02-nc-fiscal-externa-up.sql` | Aplica `P02NotaCreditoFiscalExterna` (8 columnas `NULL` + índice único filtrado). | Esquema |
| `scripts/sql/p02-nc-fiscal-externa-down.sql` | Revierte esa migración. **Elimina las columnas `FiscalExterna*` y los vínculos cargados**; solo es reversión limpia mientras no haya vínculos. | Esquema |

Ejecución de los scripts (en el contenedor SQL, con la contraseña del `.env`, nunca escrita en el repositorio):
`docker cp <script> <db>:/tmp/x.sql && docker exec <db> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d TheBuryProjectDb -I -b -i /tmp/x.sql`

## 1. Antes de empezar (común a P3 y V2)

1. **Estado de la instalación** — correr `p02-verificar-instalacion.sql` en solo lectura. Esperado en la instalación nueva: `Ventas/Créditos/Movimientos/Facturas = 0`; ninguna versión fiscal distinta de 1 ó 2. Si hay operaciones, ¿son todas posteriores a la puesta en marcha? Completar `@PuestaEnMarcha` (UTC) para que el script lo compruebe.
2. **Migraciones pendientes** — el servicio `migrate` las aplica y termina en 0 sólo con 0 pendientes (`docs/deploy-rollback.md` §2). Una base con historial y pendientes **no** se migra sin habilitación explícita (`Database:AllowMigrations=true` o `--migrate`).
3. **Respaldo verificado** — `scripts/backup/backup.sh` (`RESTORE VERIFYONLY` + sha256) y `scripts/backup/restore-test.sh` sobre ese respaldo. Anotar la ruta y el hash. Sin respaldo verificado no se continúa.
4. **Ventana** — sin operaciones de Caja abiertas en curso (cerrar turnos o acordar ventana).

## 2. P3 — aplicar la migración `P02NotaCreditoFiscalExterna` **[AUTORIZADA por el propietario, 2026-11-20]**

Migración aditiva: 8 columnas `NULL` en `NotasCredito` e índice único filtrado `IX_NotasCredito_FiscalExterna`. No cambia ni lee filas existentes. Probada: aplicar → revertir (las filas se conservan, `__EFMigrationsHistory` vuelve a `20261118172846_D03_TesoreriaOrigenesAnticipos`) → reaplicar.

1. **Comprobar versión de esquema**: `p02-verificar-instalacion.sql` debe informar «P3: esquema coherente = no aplicada» y última migración `20261118172846_D03_…` (144 migraciones).
2. **Respaldo** (§1.3).
3. **Aplicar** por el camino normal de despliegue (`scripts/deploy/deploy.sh`, que hace backup → `migrate` → health → smoke) o, si se prefiere SQL explícito, `p02-nc-fiscal-externa-up.sql`.
4. **Comprobación posterior**: el script de verificación debe mostrar «aplicada», 8 columnas, 1 índice, 145 migraciones, y todos los controles en OK. Probar en la app: Detalle de una devolución aprobada → «Vincular comprobante fiscal» (con un usuario Administrador).
5. **Recuperación**:
   - *Sin vínculos cargados*: `p02-nc-fiscal-externa-down.sql` (transaccional, `XACT_ABORT`) y volver a la imagen anterior con `scripts/deploy/rollback.sh` (la imagen anterior no conoce las columnas, que además son nulables: **la imagen anterior funciona con el esquema nuevo**, por eso el orden seguro es revertir imagen primero y, sólo si hace falta, esquema después).
   - *Con vínculos cargados*: **no** ejecutar el `down` (perdería los datos fiscales). Restaurar desde el respaldo del paso 2 sólo si el propietario lo decide; de lo contrario revertir sólo la imagen.
6. Registrar fecha/hora de aplicación y hash del respaldo.

**Qué NO cambia P3 al aplicarla:** el Reporte de IVA no netea, ningún importe se altera. Ver §4.

## 3. P6 — activar `CriterioFiscalV2` en producción **[AUTORIZADA por el propietario, 2026-11-20]**

V2 cambia importes cobrados (redondeo comercial C1/C2; descuento de efectivo solo sobre productos, C5), por eso rige **desde la puesta en marcha real de la instalación nueva, nunca retroactivamente**. La fecha de corte es la de puesta en marcha, no una fecha inventada.

**Mecanismo (implementado):** clave de configuración `Fiscal:CriterioV2Desde` = instante ISO-8601 **con zona** (`2026-12-01T03:00:00Z` = 00:00 de Argentina). En Docker se define en el `.env` como `FISCAL_CRITERIO_V2_DESDE` (pasa a `Fiscal__CriterioV2Desde` de `app`). Reglas:

- **Vacía o ausente = V1, sin ningún cambio** (una instalación con base histórica no se ve afectada; es el estado de todas las instalaciones hoy).
- **La versión se resuelve una sola vez, al arrancar el proceso:** V2 si el instante ya llegó, V1 si no (y entonces hasta el próximo reinicio). **No cambia en caliente**, así que ningún cálculo ni operación en curso puede mezclar versiones (hallazgo de la revisión independiente: el cotizador lee la versión más de una vez). El despliegue de puesta en marcha reinicia `app`, por lo que **rige desde ese despliegue**. Si el instante es futuro, el arranque lo avisa (`[FISCAL] AVISO …`).
- **Un valor inválido impide arrancar** y `scripts/deploy/preflight.sh` lo detecta antes del despliegue (formato, calendario real, año ≥ 2026, zona ≤ ±14 h; `Z` en mayúscula). Nunca se ignora ni se adivina.
- **Cada venta se estampa con la versión vigente al crearse y no se reescribe:** las operaciones V1 se conservan tal cual. La versión **no** se reconfigura con `Ensayo:CriterioFiscalVersion` (solo `Development`).
- **No se quita ni se adelanta una vez que existen ventas V2** (volvería a estamparse V1). Se conserva en el `.env` y en el registro de puesta en marcha.
- `VersionInicial` (=1) sigue siendo el valor por defecto de `Venta.CriterioFiscalVersion` y el criterio de lectura de lo histórico: no se toca.

**Pasos (en este orden; todos ensayados en `scripts/qa/p02/ensayo-puesta-en-marcha.sh`, ver §6):**

1. Comprobaciones de §1 sobre la **base nueva de producción** (no sobre una base de preproducción con datos sintéticos: `p02-verificar-instalacion.sql` debe mostrar 0 ventas/créditos/facturas/movimientos antes de la puesta en marcha y ningún dato de ensayo; la base de preproducción de la VM tiene clientes y usuarios sintéticos y **no** sirve como base productiva).
2. **P3 primero o junto con V2** (§2): un solo corte de importes y de esquema.
3. Respaldo verificado (`deploy.sh --backup auto` lo hace antes de migrar) y `restore-test.sh` en verde.
4. Definir en el `.env` **la fecha real**: `FISCAL_CRITERIO_V2_DESDE=<instante de la puesta en marcha, con zona>`; el valor puede ser el del propio despliegue o anterior a él (nunca posterior a la primera venta real que deba ser V2).
5. `scripts/deploy/deploy.sh --image <release> --backup auto` (preflight → backup → migrate (P3) → app → health → smoke).
6. **Verificación posterior inmediata**, con la primera venta real (o una venta de prueba acordada que luego se anula): `p02-verificar-instalacion.sql` con `@PuestaEnMarcha` completado debe dar sin `FALLA`; la venta debe estar en versión 2 (`select CriterioFiscalVersion from Ventas`), `Subtotal + IVA = Total`, factura igual a la venta. **Fecha efectiva de V2 = `MIN(FechaVenta)` de las ventas con versión 2** (queda en el informe del script) y, además, el valor de `FISCAL_CRITERIO_V2_DESDE` del `.env`: ese es el registro de fecha efectiva y versión utilizada.
7. **Recuperación si falla el despliegue:** `scripts/deploy/rollback.sh --to-image <anterior> --confirm-compatible` (solo imagen; no toca la base). **Con ventas V2 ya creadas, la imagen a la que se vuelve debe conocer V2** (cualquier build posterior a #329): una imagen que no la registre falla cerrada al leer esas ventas (nunca recalcula con otras reglas). Si todavía no hay ventas V2, cualquier imagen anterior sirve y se puede dejar `FISCAL_CRITERIO_V2_DESDE` vacío.

**Qué no hacer:** no usar `Ensayo:CriterioFiscalVersion` en producción; no reescribir `CriterioFiscalVersion` de ventas existentes; no definir la clave en una instalación con base histórica de la versión anterior del formulario de venta (fecha guardada como «medianoche argentina», ver `docs/p02-respuestas-contador-20261119.md` §9).

### 3.1 Configuración de punitorios en la puesta en marcha (C10)

Con la gracia real (`docs/punitorios-gracia-real-c10.md`) la instalación nueva debe cargar, por la pantalla de Configuración de punitorios (permiso `configuracion.managepunitorio`, nunca sembrada por código ni por script), una versión vigente con `DiasGracia = 12` y la tasa/período que defina el propietario, y `ConfiguracionMora.DiasGracia` para la bandeja de mora. Sin esa versión el punitorio queda «sin configuración» y no se calcula nada: no hay valor por defecto.

## 4. Neteo del IVA por NC fiscal externa — diseño preparado, **sin activar** (P3 + período)

Hoy el Reporte de IVA no netea devoluciones y su nota lo advierte. Diseño para cuando el contador defina el período (pregunta 3 de la consulta) y el propietario autorice:

- **Fuente única:** sólo restan las `NotaCredito` con vínculo fiscal externo válido (`FiscalExternaNumero` no nulo); nunca una devolución sin NC fiscal registrada.
- **Importe a restar:** el IVA informativo proporcional al **importe de la NC fiscal** (`FiscalExternaImporte`), con el desglose por alícuota de la venta original (misma regla que el comprobante), no el de la devolución interna. Con NC fiscal parcial: proporcional.
- **Período:** el de la fecha de la NC fiscal (`FiscalExternaFecha`) **o** el de la venta original — decisión pendiente del contador; el diseño parametriza esa fecha en un único punto.
- **Sin doble descuento:** el neteo lee sólo `FiscalExterna*`; la suma de NC fiscales de una venta ya está acotada por el total de la venta (validación del vínculo) y el índice único impide registrar dos veces un comprobante. Las devoluciones internas siguen restando de Ventas Netas/Ganancia (reportes de gestión), sin tocar el IVA.
- **Pruebas a escribir al activarlo:** devolución parcial con NC parcial; NC en otro período; NC mayor que lo permitido (rechazada por el vínculo); venta con alícuotas mixtas; dos NC sobre la misma venta.

## 5. Residuos conocidos (documentados, sin cambio)

- **Carrera entre NC distintas de la misma venta: CERRADA (2026-11-20).** El vínculo modifica la venta en el mismo guardado, de modo que dos vínculos simultáneos sobre la misma venta chocan por el token de concurrencia de `Ventas`; el perdedor falla y su reintento revalida contra lo ya acreditado. Prueba `Registrar_DosNcDistintasDeLaMismaVentaEnCarrera_NoSuperanElTotalDeLaVenta` (falla sin el fix) y `…DobleEnvioConcurrenteSobreLaMismaNc…`; revisión independiente de Codex sin hallazgos sobre este punto. Efecto colateral aceptado: una edición de esa venta en curso en el mismo instante recibiría un conflicto de concurrencia y debe reintentarse.
- **Actualización 2026-11-23 (validación Agente 3):** con envío + armado y recargo/descuento de medio de pago, la misma diferencia de desglose V1 se midió hasta **$0,81** (emisión real con `FacturarVentaAsync`, prueba `EmisionDeFactura_ContadoConServiciosYCentavos_…`); el total, `Neto + IVA = Total` y el ingreso de Caja coinciden siempre, y V2 da 0 en los mismos casos. Misma conclusión: V1 inmutable, informativa, sin versión nueva.
- **Hallazgo histórico V1 (medido 2026-11-20):** con ajuste de medio de pago (recargo o descuento) y alícuotas mixtas, el neto guardado en la venta puede diferir del reconstruido al leer el comprobante. **Medido hasta $0,71** (casos de la prueba `IntegridadFiscal_AjusteConAlicuotasMixtas_…`: 0,71 / 0,64 / 0,30 / 0,06); **no hay una cota universal demostrada**: en casos de uso razonables es de centavos (< $1), pero un contraejemplo extremo calculado en revisión independiente (producto exento de $1.000,99 + servicio al 21 % de $1 + recargo del 999 %, que la configuración permite) da $1,88. Crece con el porcentaje de ajuste; con los ajustes reales del negocio (−5 % / +3 %) se midió ≤ $0,71. Impacto real: **ninguno sobre lo cobrado** (el total coincide con el oráculo independiente y `Neto + IVA = Total` en venta, comprobante y reporte); es una diferencia de desglose neto/IVA, informativa. V2 la tiene en **0** (prueba). V1 es inmutable y no se corrige; **no** se crea una versión fiscal nueva por esta diferencia (no la justifica ni está autorizada). Un comprobante de venta V1 se interpreta idéntico aunque la versión vigente sea V2 (se resuelve por la versión persistida en la venta; prueba incluida). Nota: la cifra «~$0,70» de documentos anteriores queda corregida a «máx. medido 0,71 con los ajustes reales; sin cota universal».

## 6. Ensayo general (2026-11-20, VM `tbp`, stack aislado) — evidencia de ejecución

`scripts/qa/p02/ensayo-puesta-en-marcha.sh` (con `ensayo_venta.py`) recorre en un stack descartable `tbp-p02ens-<hex>` (proyecto Compose, red, volúmenes, subred y puerto propios; datos y secretos sintéticos; se niega a correr si existe un `.env` en el checkout) la secuencia exacta de la puesta en marcha, con las herramientas reales (`deploy.sh`, `preflight.sh`, `backup.sh`, `restore-test.sh`, `rollback.sh`). Release A = imagen de preproducción existente (`20261119-02c01635-dirty`, 144 migraciones, sin P3); release B = imagen construida de este árbol. Resultado de la corrida: **27 controles OK; 1 «FALLA» esperado** (el control «ventas anteriores a la puesta en marcha» detectó las 2 ventas V1 que el ensayo crea a propósito para probar que V1 se conserva; en producción debe dar 0 — el control se ajustó para exigir exactamente ese resultado). Log completo: `D:\git\QA-AUDITORIA\p02-ensayo-20261120\tanda-f-20261120\ensayo.log` (y en la VM `~/ens-p02h/evidencia/`).

| Paso | Resultado |
|---|---|
| Instalación nueva con A (base vacía) | `deploy.sh` exit 0; 144 migraciones; 0 ventas/créditos/facturas; P3 no aplicada; ningún control en `FALLA` |
| Venta bajo A | S1 facturada, versión fiscal 1 (total 1.000,51: V1 no redondea comercialmente) |
| Preflight con `FISCAL_CRITERIO_V2_DESDE` inválida (`2026-02-31T03:00:00Z`, `2026-12-01T03:00` sin zona) | falla (exit ≠ 0) antes del despliegue |
| Despliegue de B: backup previo → migración P3 → app → health → smoke, con fecha de V2 futura | exit 0; backup creado; **145 migraciones, P3 aplicada (8 columnas + índice, esquema coherente)**; S1 conserva versión 1 y total; el arranque informa V1 (`[FISCAL] … V1`); S2 se estampa V1 |
| Backup recuperable | `restore-test.sh` restaura el último backup en una base temporal y valida (exit 0) |
| Puesta en marcha: `FISCAL_CRITERIO_V2_DESDE` = ahora, redeploy | el arranque informa `= V2`; S3 facturada con **versión 2 y total 1.001,00** (1.000,51 → redondeo comercial); S1/S2 siguen en V1 sin cambio de importe; `Subtotal + IVA = Total` y facturas consistentes; fecha efectiva de V2 informada por el script |
| Rollback de imagen a A (sin tocar la base) | `rollback.sh` exit 0, app sana; base intacta (P3 sigue aplicada, 3 ventas); detalle de la venta V1 abre; **el detalle de la venta V2 con A no abre (302)**: A no conoce V2 y falla cerrada |
| Roll-forward a B | exit 0; el detalle de la venta V2 abre |
| Limpieza | el stack se destruyó sin restos (0 contenedores/volúmenes/redes del proyecto); el stack de preproducción de la VM no se tocó |

**Conclusión operativa:** con ventas V2 creadas, el rollback de imagen **solo** es seguro hacia una imagen que conozca V2 (cualquier build posterior a #329); hacia una anterior, las ventas V2 quedan ilegibles hasta volver a B. Antes de la primera venta V2 cualquier imagen anterior sirve.

## 7. Puesta en marcha real — estado, bloqueos externos y runbook exacto

**Estado:** mecanismos de P3 y P6 implementados y ensayados (§6). **No se aplicaron a ninguna base real**: la base que hoy corre en la VM (`theburyproject-db-1`) es de **preproducción con datos sintéticos** (5 clientes «PreprodSintetico», usuarios `p03.*` y `admin@tbp.local`; 144 migraciones, 0 ventas). No cumple «instalación limpia» ni puede mezclarse con datos productivos, y no se usará como base de producción.

**Bloqueos externos reales (dependen del propietario, no de la ingeniería):**
1. Una **base de producción nueva** con credenciales reales (`.env` de producción: contraseñas SQL, administrador inicial real, dominio/certificado, licencia de AutoMapper), en un proyecto Compose y volúmenes propios (no los de preproducción).
2. La **fecha real de puesta en marcha** (instante con zona). No se inventa ni se supone.
3. Copia externa de respaldos (`BACKUP_REMOTE`, `BACKUP_REQUIRE_OFFSITE=true`): el offsite real sigue sin verificar (ver `docs/backup-restore.md`).

**Runbook (cuando existan 1–3):** en el host de producción, en un directorio y proyecto nuevos (p. ej. `theburyproject-prod`; nunca los volúmenes de preproducción):

```
# 0) .env de producción creado y revisado (secretos reales, BACKUP_REQUIRE_OFFSITE=true).
#    FISCAL_CRITERIO_V2_DESDE=<instante real de puesta en marcha con zona>   # misma fecha que el registro de puesta en marcha
# 1) preflight (valida formato de la fecha, secretos, red, imagen)
scripts/deploy/preflight.sh --image <release> --env-file .env --project theburyproject-prod
# 2) instalación: db → db-init → migrate (aplica TODAS las migraciones, incluida P3) → app
scripts/deploy/deploy.sh --image <release> --env-file .env --project theburyproject-prod --backup skip   # base nueva: sin backup previo
# 3) verificación de instalación limpia y esquema (solo lectura): esperado 145+ migraciones, P3 aplicada, 0 ventas/créditos/facturas
docker exec -i theburyproject-prod-db-1 /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d <db> -W -s'|' -I -b -i /tmp/verif.sql   # p02-verificar-instalacion.sql
# 4) respaldo recuperable: backup + restore-test
COMPOSE_PROJECT_NAME=theburyproject-prod scripts/backup/backup.sh all && COMPOSE_PROJECT_NAME=theburyproject-prod scripts/backup/restore-test.sh
# 5) rollback ensayado: la imagen anterior (si existe) y rollback.sh --confirm-compatible, como en §6
# 6) primera venta real/acordada: versión 2, Subtotal + IVA = Total, factura igual; registrar la fecha efectiva de V2
```

Como la base es **nueva**, P3 se aplica con todas las migraciones del `migrate` (no hace falta ejecutar `p02-nc-fiscal-externa-up.sql`; ese script y el `down` son para una base existente de 144 migraciones, como en el ensayo). Con `FISCAL_CRITERIO_V2_DESDE` definido en el primer arranque, **V2 rige desde la primera venta**. El neteo de IVA por NC fiscal **no se activa** (pendiente del contador, §4).


### 6.1 Validación en base nueva (2026-11-21)

`scripts/qa/p02/validacion-base-nueva.sh --image <imagen>`: instalación limpia en Production con V2 desde el primer arranque, 145 migraciones, base sin datos, gracia de 12 días por versionado y primera venta V2; 35 OK / 0 FALLA. Ver `docs/cierre-p03-p07-p02-20261121.md`.

`validacion-base-nueva.sh` incluye desde 2026-11-21 el control de cuentas `scripts/sql/p07-verificar-cuentas.sql` (37 OK / 0 FALLA). Orden de puesta en marcha, cuentas, parámetros y reversión: `docs/puesta-en-marcha-p07-p02.md`.

### 6.2 Validación fiscal V2 de contado (2026-11-23)

Sobre `main` 3de0402e, con servicios reales sobre SQLite en memoria (prueba sintética: **no es aceptación fiscal de producción**). Un cambio de código mínimo (la ruta de tarjeta, ver tabla). Nuevas pruebas en `FiscalTarjetaServiciosV2P02Tests` y `PunitorioGraciaRealTests`:

| Escenario | Resultado |
|---|---|
| Tarjeta (plan con recargo) + armado + envío + centavos, V1 y V2, producto al 21 % y al 10,5 % | **Defecto encontrado y corregido** (revisión independiente de Codex): con V2 y alícuotas mixtas (p. ej. producto 1.234,57 al 10,5 % + servicios 650 + recargo 10 %), `GuardarDatosTarjetaAsync` discriminaba el recargo sin los servicios exactos y el neto persistido (1.819,49) difería un centavo del comprobante y el reporte (1.819,50); el total no cambiaba. Ahora pasa los servicios exactos (solo V2; V1 los ignora). Después: venta = comprobante = Reporte de IVA al centavo; `Neto + IVA = Total` |
| Emisión real de factura de contado (Efectivo −5 %, Transferencia +3 %; IVA 21 % y 10,5 %; centavos; armado + envío) | V2: factura = venta persistida = comprobante = reporte = ingreso de Caja, sin reescribir importes al facturar. V1: total exacto; desglose neto/IVA del comprobante difiere hasta $0,81 (hallazgo V1 documentado) |
| Devolución total V2 con descuento de Efectivo + servicios | Reintegra exactamente lo cobrado por productos (`Total − servicios`); egreso de Caja = total de la devolución |
| Punitorio 0 % / 30 días / 12 de gracia | $0 en cualquier atraso, sin error |
| `FISCAL_CRITERIO_V2_DESDE` ausente / inválida | ya cubierto (`ActivacionCriterioFiscalTests`, `preflight.sh`): ausente = V1; inválida = no arranca |

Riesgo operativo residual (no es defecto de código): `FISCAL_CRITERIO_V2_DESDE` ausente o vacía es una configuración válida (rige V1), así que `preflight.sh` solo lo informa («ausente o vacío (rige CriterioFiscal V1)») y la app arranca igual: una omisión del `.env` no se bloquea. La versión realmente aplicada se confirma con `p02-verificar-instalacion.sql` tras la primera venta (§3 paso 6, obligatorio).
