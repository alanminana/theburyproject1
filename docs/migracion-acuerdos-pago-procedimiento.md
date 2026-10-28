# Procedimiento: migraciones de acuerdos de pago sobre la base real

Migraciones pendientes en `TheBuryProjectDb` (verificado 2026-10-28; base con 61 ventas, 0 acuerdos Activos):

1. `20261028013804_AcuerdoPagoDeudaCongelada` — tabla `AcuerdoPagoCuotasCubiertas`, `AlertaCobranzaId` pasa a NULL. Frena (`THROW 50001`) si hay acuerdos Activos.
2. `20261028021207_AcuerdoPagoIncumplimientoAutomatico` — `ConfiguracionesMora.DiasGraciaIncumplimientoAcuerdo`, `AcuerdosPago.DiasGraciaIncumplimiento` (default 3) y `FechaActivacionComercial`, con backfill solo de acuerdos Activos.

Sin aplicarlas, activar un acuerdo falla en esa base.

## Prueba hecha (2026-10-28, copia aislada)

- Backup `COPY_ONLY, CHECKSUM` + `RESTORE VERIFYONLY` OK: `C:\Users\Public\tbp_backup_pre_acuerdos_mig_20261028.bak`.
- Restaurado como `TheBuryProjectDb_qa_migacuerdos` y migrado con `dotnet ef database update`.
- Conteos antes/después idénticos (ventas 61, créditos 34, cuotas 61, movimientos de caja 42); `DBCC CHECKDB` OK; columnas, índices (PK + 2) y 2 FK verificados; `DiasGracia` = 3 en `ConfiguracionesMora`.

## No usar el script idempotente

`dotnet ef migrations script ... --idempotent` falla en la segunda migración: el `UPDATE` del backfill queda en el mismo batch que el `ALTER TABLE ... ADD` (`Invalid column name 'FechaActivacionComercial'`). Aplicar siempre con `dotnet ef database update` (cada comando va en su propio batch) o con el servicio `migrate` de Docker.

## Aplicación a la base real (requiere autorización)

1. Cerrar la app (que no haya conexiones) y confirmar el destino: servidor `(localdb)\MSSQLLocalDB`, base `TheBuryProjectDb`.
2. Backup nuevo `COPY_ONLY, CHECKSUM` y `RESTORE VERIFYONLY`.
3. Precondiciones: `SELECT COUNT(*) FROM AcuerdosPago WHERE Estado = 2 AND IsDeleted = 0` debe dar 0; contar ventas/créditos/cuotas.
4. `dotnet ef database update --connection "<cadena a TheBuryProjectDb>"` desde un checkout de `main`.
5. Repetir los conteos, `DBCC CHECKDB` y smoke (crear y activar un acuerdo sobre un clon, nunca sobre datos reales).
6. Rollback: restaurar el backup del paso 2.
