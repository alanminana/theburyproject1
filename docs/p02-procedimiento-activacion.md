# P-02: procedimiento de puesta en marcha — activar CriterioFiscal V2 y aplicar P3 (NC fiscal externa)

Fecha: 2026-11-20. **Estado: PREPARADO, no ejecutado.** Nada de este documento se aplicó a la VM, a la base de preproducción ni a ninguna base real. Cada paso marcado **[AUTORIZACIÓN]** requiere decisión del propietario. Complementa `docs/deploy-rollback.md`, `docs/despliegue-produccion.md`, `docs/backup-restore.md` y `docs/p02-respuestas-contador-20261119.md`.

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

## 2. P3 — aplicar la migración `P02NotaCreditoFiscalExterna` **[AUTORIZACIÓN]**

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

## 3. P6 — activar `CriterioFiscalV2` en producción **[AUTORIZACIÓN]**

Recomendación: **desde el inicio operativo de la instalación nueva, nunca retroactivamente.** V2 cambia importes cobrados (redondeo comercial C1/C2; descuento de efectivo sólo sobre productos, C5), por eso la fecha de corte es la de puesta en marcha real, no una fecha inventada.

**Mecanismo.** `CriterioFiscalRegistry.VersionVigente` (`Helpers/CriterioFiscal.cs`) devuelve hoy `VersionInicial` (= 1) salvo un escenario de pruebas o el override de ensayo, que **sólo** admite `Development` (en `Production`/`Staging`/`Testing` aborta el arranque). Producción **no** usa ese override. Activación real = un cambio de código revisado y desplegado:

- en `VersionVigente`, cambiar el respaldo `?? VersionInicial` por `?? VersionDosRedondeoComercial`;
- **no** tocar la constante `VersionInicial`: también es el valor por defecto de `Venta.CriterioFiscalVersion`, lo que usa `VentasNetas` para operaciones sin versión y el criterio con el que se leen las ventas históricas V1;
- actualizar la prueba «producción sin V2 vigente» (`FiscalVersionadoP02Tests`) para que afirme la nueva versión vigente;
- cada venta nueva se estampa con la versión vigente al crearse (`venta.CriterioFiscalVersion = VersionVigente`) y esa versión no se reescribe: las operaciones V1 se conservan tal cual.

**Pasos (en este orden):**

1. Todas las comprobaciones de §1. Instalación limpia verificada (0 operaciones o todas posteriores a la puesta en marcha).
2. **Verificar P3** si se la va a usar (§2) — son independientes, pero conviene un solo corte de importes.
3. Respaldo verificado.
4. PR con el cambio de una línea + prueba, revisión de Codex, CI en verde, fusión normal.
5. Despliegue por `scripts/deploy/deploy.sh` (sin migración nueva: `migrate` es no-op).
6. **Verificación posterior inmediata**, con una venta real de prueba acordada (o la primera venta real): `p02-verificar-instalacion.sql` debe mostrar `Ventas con versión fiscal 2 ≥ 1`, `Ventas V2 con Subtotal + IVA <> Total = 0`, facturas consistentes y la **fecha efectiva de V2** (primera venta con versión 2, en UTC). Anotar esa fecha y la versión en el registro de puesta en marcha (es el «registro de fecha efectiva y versión utilizada»: la versión queda en cada venta, la fecha efectiva es `MIN(FechaVenta)` de las ventas V2).
7. **Recuperación si falla el despliegue:** `scripts/deploy/rollback.sh` (imagen anterior, sin tocar la base). Las ventas ya creadas con versión 2 siguen siendo legibles por la imagen anterior? **No**: una imagen sin V2 registrada lanza `CriterioFiscalDesconocidoException` al calcular sobre una venta V2 (falla cerrada, nunca recalcula con otras reglas). Por eso: si ya existen ventas V2, el rollback de imagen debe ser a una imagen que **conozca** V2 (cualquier build posterior a #329); si no existen, cualquier imagen sirve. Verificarlo con el script antes de decidir.

**Qué no hacer:** no usar `Ensayo:CriterioFiscalVersion` en producción; no reescribir `CriterioFiscalVersion` de ventas existentes; no activar V2 sobre una base con ventas históricas de la versión anterior del formulario de venta (fecha guardada como «medianoche argentina», ver `docs/p02-respuestas-contador-20261119.md` §9).

## 4. Neteo del IVA por NC fiscal externa — diseño preparado, **sin activar** (P3 + período)

Hoy el Reporte de IVA no netea devoluciones y su nota lo advierte. Diseño para cuando el contador defina el período (pregunta 3 de la consulta) y el propietario autorice:

- **Fuente única:** sólo restan las `NotaCredito` con vínculo fiscal externo válido (`FiscalExternaNumero` no nulo); nunca una devolución sin NC fiscal registrada.
- **Importe a restar:** el IVA informativo proporcional al **importe de la NC fiscal** (`FiscalExternaImporte`), con el desglose por alícuota de la venta original (misma regla que el comprobante), no el de la devolución interna. Con NC fiscal parcial: proporcional.
- **Período:** el de la fecha de la NC fiscal (`FiscalExternaFecha`) **o** el de la venta original — decisión pendiente del contador; el diseño parametriza esa fecha en un único punto.
- **Sin doble descuento:** el neteo lee sólo `FiscalExterna*`; la suma de NC fiscales de una venta ya está acotada por el total de la venta (validación del vínculo) y el índice único impide registrar dos veces un comprobante. Las devoluciones internas siguen restando de Ventas Netas/Ganancia (reportes de gestión), sin tocar el IVA.
- **Pruebas a escribir al activarlo:** devolución parcial con NC parcial; NC en otro período; NC mayor que lo permitido (rechazada por el vínculo); venta con alícuotas mixtas; dos NC sobre la misma venta.

## 5. Residuos conocidos (documentados, sin cambio)

- **Carrera entre NC distintas de la misma venta:** el límite «suma de NC fiscales ≤ total de la venta» se valida leyendo antes de guardar; dos vínculos simultáneos sobre NC distintas de la misma venta podrían superarlo. Mitigación existente: acción manual del Administrador, baja frecuencia, auditada. La carrera sobre la **misma** NC sí está cerrada (token de concurrencia; prueba `Registrar_DobleEnvioConcurrenteSobreLaMismaNc_ElSegundoNoPisaElPrimero`).
- **Hallazgo histórico V1:** diferencia de hasta ~$0,70 entre el neto guardado y el reconstruido (ajuste de medio de pago con alícuotas mixtas). V1 es inmutable: no altera el total cobrado ni `Neto + IVA = Total`, y V2 no la tiene.
