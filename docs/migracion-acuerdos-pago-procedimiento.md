# Procedimiento: migraciones de acuerdos de pago sobre la base real

Migraciones pendientes en `TheBuryProjectDb` (verificado 2026-10-28; base con 61 ventas, 0 acuerdos Activos):

1. `20261028013804_AcuerdoPagoDeudaCongelada` — tabla `AcuerdoPagoCuotasCubiertas`, `AlertaCobranzaId` pasa a NULL. Frena (`THROW 50001`) si hay acuerdos Activos.
2. `20261028021207_AcuerdoPagoIncumplimientoAutomatico` — `ConfiguracionesMora.DiasGraciaIncumplimientoAcuerdo`, `AcuerdosPago.DiasGraciaIncumplimiento` (default 3) y `FechaActivacionComercial`, con backfill solo de acuerdos Activos.

Sin aplicarlas, activar un acuerdo falla en esa base.

## Prueba hecha (2026-10-28, copia aislada)

- Backup `COPY_ONLY, CHECKSUM` + `RESTORE VERIFYONLY` OK: `C:\Users\Public\tbp_backup_pre_acuerdos_mig_20261028.bak`.
- Restaurado como `TheBuryProjectDb_qa_migacuerdos` y migrado con `dotnet ef database update`.
- Conteos antes/después idénticos (ventas 61, créditos 34, cuotas 61, movimientos de caja 42); `DBCC CHECKDB` OK; columnas, índices (PK + 2) y 2 FK verificados; `DiasGracia` = 3 en `ConfiguracionesMora`.

## Script idempotente (corregido 2026-10-28)

`dotnet ef migrations script <from> <to> --idempotent` emite cada migracion en un unico batch `IF NOT EXISTS (...) BEGIN ... END`. Dos patrones fallaban ahi y ya estan corregidos con `migrationBuilder.SqlDiferido(...)` (`Migrations/MigrationBuilderSqlExtensions.cs`, envuelve el SQL en `EXEC(N'...')`; mismo efecto, no cambia IDs ni snapshot, no re-ejecuta migraciones aplicadas):

- backfill/seed que usa una columna agregada en la misma migracion (`Invalid column name`): `AcuerdoPagoIncumplimientoAutomatico`, `AddVentaDetalleIvaSnapshots`, `AddVentaDetalleDescuentoGeneralProrrateado`, `AddVentaDetalleCostoSnapshots`, `AddMovimientoStockCostoSnapshots`, `VentaDetalleSnapshotProducto`, `AddUserSucursalRelationAndPermissionAliases`;
- SQL que referencia columnas que una migracion posterior elimina (falla al **re-ejecutar** el script sobre una base ya migrada): `AddClienteAptitudCredito` (seed), `DropVentaCreditoCuotasLegacy` y `DropColumnasCreditoSinUso` (guardas `THROW`; siguen frenando el batch);
- `AddDefaultsMarcaProducto`: tres `DECLARE @df` en el mismo batch (`The variable name '@df' has already been declared`); variables renombradas.

Verificado contra bases descartables: el script completo (desde cero, y desde `AcuerdoPagoImputaciones`) aplica sin errores, se puede re-ejecutar sin errores, y deja esquema (columnas, indices, FK) y filas semilla identicos a `dotnet ef database update`. Backfill probado: activacion `2026-10-28 02:30 UTC` -> `FechaActivacionComercial` `2026-10-27`.

## Aplicación a la base real (requiere autorización)

1. Cerrar la app (que no haya conexiones) y confirmar el destino: servidor `(localdb)\MSSQLLocalDB`, base `TheBuryProjectDb`.
2. Backup nuevo `COPY_ONLY, CHECKSUM` y `RESTORE VERIFYONLY`.
3. Precondiciones: `SELECT COUNT(*) FROM AcuerdosPago WHERE Estado = 2 AND IsDeleted = 0` debe dar 0; contar ventas/créditos/cuotas.
4. `dotnet ef database update --connection "<cadena a TheBuryProjectDb>"` desde un checkout de `main`.
5. Repetir los conteos, `DBCC CHECKDB` y smoke (crear y activar un acuerdo sobre un clon, nunca sobre datos reales).
6. Rollback: restaurar el backup del paso 2.

## Smoke funcional de acuerdos (2026-10-28, clon aislado)

Clon `TheBuryProjectDb_qa_h2smoke` (backup `COPY_ONLY, CHECKSUM` + `RESTORE VERIFYONLY`, ya migrada la base real), app en `:5199` con la cadena al clon, flujo por los endpoints reales de `MoraController` (`CrearAcuerdo`, `CambiarEstadoAcuerdo`, `RegistrarPagoAcuerdo`). Clon eliminado al terminar; la base real no se toco.

| Caso | Resultado |
|---|---|
| Credito 4: crear (entrega 10.000 + 2 cuotas) y activar | Borrador -> Activo; `AcuerdoPagoCuotasCubiertas` con la cuota 1 (48.285,60 capital, 0 punitorio); `FechaActivacionComercial` = 2026-10-28, gracia 3 |
| Cobrar entrega + cuota 1 + cuota 2 (`pasoEsperado` 0,1,2) | 3 `PagosCuota` (10.000 + 19.142,80 + 19.142,80) con 3 `MovimientosCaja`; cuota 1 del credito Pagada (48.285,60); 3 imputaciones Aplicadas; acuerdo -> **Cumplido** |
| Credito 1017 (cuota 2 vencida con dato temporal del clon): activar con entrega 1.000 impaga; `FechaActivacionComercial` = hoy-3 y reinicio | sigue Activo (limite de gracia: `hoy > activacion + 3` es falso) |
| Igual con `FechaActivacionComercial` = hoy-4 y reinicio (job al minuto de arrancar) | **Incumplido** automatico, motivo "La entrega inicial vencio el 24/10/2026 ... 3 dias de gracia"; un pago posterior sobre ese acuerdo no cobra nada |
| Nuevo acuerdo sin entrega sobre el mismo credito, cuota 1 del acuerdo vencida hace 5 dias | **Incumplido** automatico por cuota |

Auditoria (`SeguridadEventosAuditoria`, modulo Mora): `CrearAcuerdoPago`, `CambiarEstadoAcuerdo` (usuario admin) e `IncumplimientoAutomatico` (usuario Sistema) para cada acuerdo, mas 3 `DocumentoGenerado REC-*` por los cobros.

Aislamiento de operaciones ajenas (conteo + `CHECKSUM_AGG(BINARY_CHECKSUM(*))` antes/despues, excluyendo creditos 4 y 1017): `Creditos`, `Cuotas`, `PagosCuota`, `Ventas`, `MovimientosCaja` preexistentes y `AperturasCaja` identicos; `MovimientosCaja` +3 (= los 3 cobros, 48.285,60). Cambios esperados fuera de esos creditos: la alerta de mora id 1 (credito 1012, ya totalmente pagado en el clon) paso a Resuelta y el cliente 1 (titular del credito 4) recibio la actualizacion de scoring, ambos por la regularizacion que dispara el cobro.

Datos temporales solo en el clon: vencimiento de la cuota 1035 (credito 1017) y `FechaApertura` de la apertura 1021 (caja de admin) para que no estuviera vencida.

Fuera de alcance / no validado: pantalla (la vista no ofrece alta de acuerdo en esta version: `GET CrearAcuerdo` redirige con "todavia no esta disponible"), cobro con medios distintos de efectivo, acuerdo con punitorio > 0 (el clon tenia punitorio 0 en esos creditos).


## Smoke funcional de acuerdos (2026-10-28, clon de la base real ya migrada)

Clon `TheBuryProjectDb_qa_h2smoke` (eliminado), app contra el clon y endpoints reales de `MoraController` (`CrearAcuerdo`, `CambiarEstadoAcuerdo`, `RegistrarPagoAcuerdo`). Base real intacta.

| Caso | Resultado |
|---|---|
| Crear (entrega 10.000 + 2 cuotas) y activar | Borrador → Activo; deuda de la cuota 1 congelada; `FechaActivacionComercial` = 2026-10-28; gracia 3 días |
| Cobrar entrega y cuotas 1 y 2 | 3 pagos y 3 movimientos de caja; 3 imputaciones Aplicadas; cuota original Pagada; acuerdo Cumplido |
| Entrega impaga, activado hace 3 días (límite de gracia) | Sigue Activo |
| Mismo caso con 4 días | Incumplido automático (job); un pago posterior no reactiva |
| Acuerdo sin entrega con cuota vencida | Incumplido automático por cuota |

Auditoría: `CrearAcuerdoPago`, `CambiarEstadoAcuerdo` (usuario), `IncumplimientoAutomatico` (Sistema) y `DocumentoGenerado REC-*` por cada cobro.

Operaciones ajenas: créditos, cuotas, pagos, ventas, movimientos de caja previos y aperturas idénticos (conteos, sumas y checksums). Únicos cambios fuera de los créditos del acuerdo, ambos disparados por el cobro: una alerta de mora resuelta y el scoring del cliente.

Datos temporales solo en el clon: se adelantaron fechas de activación/vencimiento por UPDATE para forzar el incumplimiento.

No validado: pantalla (se usó POST directo, sin capturas), cobros con medios distintos de efectivo y acuerdos con punitorio mayor que 0 (los créditos usados tenían punitorio 0).
