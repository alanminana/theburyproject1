# H-01 — propuesta de aplicación segura a la base real

**Estado: APLICADA a `TheBuryProjectDb` el 2026-11-06 con autorización expresa del propietario** (junto con la baja de `clientes.delete` del rol Gerente). Ver «Registro de aplicación» al final.

## Qué cambia en el esquema
Una sola columna: `Ventas.NumeroOrigen nvarchar(20) NULL` + el registro `20261106072023_H01NumeroOrigenVenta` en `__EFMigrationsHistory`. No modifica, borra ni recalcula ningún valor: **ninguna venta existente se renumera** (las anteriores conservan su número y quedan con `NumeroOrigen` NULL). Aditiva y de bajo riesgo para la versión anterior de la aplicación (ignora la columna).

## Orden obligatorio
1. Aplicar la migración a la base real (este documento).
2. Recién después fusionar #239 y desplegar. **El código nuevo falla en cualquier consulta de `Ventas` si la columna no existe**; por eso no se fusiona antes.

## Artefacto
`scripts/sql/h01-migracion-numero-origen-venta.sql` (mismo diseño que el de H12): transaccional, con bloqueo de aplicación, ensayo (`DryRun=1`), idempotente y con guardas que **abortan sin cambiar nada**: nombre de base (`BaseEsperada`), historial exacto de las 135 migraciones previas (faltantes/extras/posteriores), columna ya existente sin registro, registro sin columna, tipo/longitud/nulabilidad de la columna y verificación final dentro de la transacción.

## Ensayo realizado (sobre un clon restaurado del respaldo limpio, nunca sobre la base real)
| Caso | Resultado |
|---|---|
| `DryRun=1` | agrega y registra; **revierte** (0 columnas, 135 registros después) |
| Aplicación | columna creada, 136 registros |
| Reaplicación | «ya está registrada y la columna existe… no se hace nada» |
| Base distinta de `BaseEsperada` | aborta (51041), sin cambios |
| Historial alterado (falta H12) | aborta (51044), sin cambios |
| Reversión (bloque *Rollback*) | columna eliminada y registro borrado |

## Procedimiento propuesto (con la aplicación detenida)
1. **Respaldo** inmediatamente antes: `BACKUP DATABASE TheBuryProjectDb TO DISK='D:\tmp\h01-previo-…bak' WITH COPY_ONLY, CHECKSUM, INIT;` y `RESTORE VERIFYONLY … WITH CHECKSUM;`.
2. **Huella previa** (conteos de `Ventas`, `VentaDetalles`, `MovimientosCaja`, `MovimientosStock`, `Cuotas`, `Creditos`, `__EFMigrationsHistory`) para comparar luego.
3. **Ensayo**: `sqlcmd -S <srv> -d TheBuryProjectDb -I -b -f 65001 -i scripts/sql/h01-migracion-numero-origen-venta.sql -v DryRun=1 BaseEsperada=TheBuryProjectDb` — debe imprimir «DryRun=1: se revierte» sin error.
4. **Aplicación**: igual con `DryRun=0`; debe terminar en «Aplicado.».
5. **Verificación**: columna presente (`nvarchar(20)`, NULL), 136 registros en el historial, huella idéntica salvo el historial; arrancar la aplicación y abrir una venta.
6. Fusionar #239 y desplegar; probar una venta nueva por el asistente (`COT-` → confirmar → `VTA-` con «Origen COT-…»).

## Recuperación
- **Preferida:** restaurar el respaldo del paso 1 (`ALTER DATABASE … SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE … WITH REPLACE;`). Se pierde lo registrado después del respaldo.
- **Solo la columna:** bloque *Rollback* al final del script; válido mientras ninguna venta renumerada deba conservar su `NumeroOrigen`; luego hay que volver a una versión de la aplicación **sin** H-01.

## Riesgos y dependencias
- Con H-01, las ventas **nuevas** del asistente cambian de número al emitir contrato/pagaré o confirmar: cualquier proceso externo que guarde el `COT-` impreso en un borrador verá otro número definitivo (el `COT-` queda en `NumeroOrigen` y en pantalla).
- No hay datos que migrar (la base real no tiene ventas, según la limpieza del 2026-11-05); si las hubiera, no se tocan.
- Antes de aplicar conviene confirmar que la base real sigue en 135 migraciones (el script lo verifica y aborta si no).

## Registro de aplicación (2026-11-06)
- **Respaldo previo:** `D:	mp\h01op\h01-previo.bak` (COPY_ONLY, CHECKSUM, `RESTORE VERIFYONLY` válido). Restaurado como base aislada `TheBuryProjectDb_h01ens`, donde se ensayó todo (DryRun, aplicación, reaplicación, guarda de base) antes de tocar la real.
- **Estado previo real:** 135 migraciones (última `H12CostoServiciosVenta`), sin la columna, 0 conexiones, 0 ventas, 11 usuarios, 625 filas de `RolPermisos` (622 activas), sin claims `clientes.delete` en `AspNetRoleClaims`/`AspNetUserClaims` (la baja del dato basta).
- **Aplicación real:** `h01-migracion-numero-origen-venta.sql` (DryRun=1 y luego DryRun=0) y `gerente-sin-clientes-delete.sql` (DryRun=1 y luego DryRun=0), ambos con `BaseEsperada=TheBuryProjectDb`.
- **Resultado:** 136 migraciones (última `H01NumeroOrigenVenta`); `Ventas.NumeroOrigen` `nvarchar(20)` NULL; `Gerente clientes.delete` dado de baja (Id 299, 622→621 activas); Administrador y SuperAdmin conservan el permiso; Gerente conserva `ventas.cancel`. Las huellas (Ventas, VentaDetalles, MovimientosCaja, MovimientosStock, Cuotas, Creditos, Clientes, Usuarios, Productos) idénticas; `DBCC CHECKDB` sin errores.
- **Revisión independiente (Codex, solo lectura):** sin hallazgos bloqueantes. Observación: el seeder de `main` ya excluye `clientes.delete` para Gerente y no reactiva filas dadas de baja.
