# Discrepancia de conteo de ventas (0 vs 61) — 2026-11-07

**Conclusión:** no hay pérdida de datos. El "61" es el conteo anterior a la limpieza de datos de prueba del 2026-11-05; el "0" es el estado actual de la base real. Los clones de QA tienen conteos propios (67 en `qa_export_20261107`).

## Evidencia (solo lectura)

| Fuente | Fecha | Ventas | Lectura |
|---|---|---|---|
| `docs/migracion-acuerdos-pago-procedimiento.md`, `docs/agentes/STATE.md` (filas de migraciones y acuerdos) | 2026-10-26 / 28 | 61 (34 créditos, 61 cuotas, 42 movimientos de caja) | Base real con datos de prueba acumulados |
| Limpieza de datos de prueba (script de #231; `Clientes=eliminar Proveedores=eliminar Productos=conservar Auditoria=conservar ReiniciarNumeraciones=0`), autorizada por el usuario | 2026-11-05 | → 0 | Respaldo COPY_ONLY previo con CHECKSUM: `D:\tmp\limpieza-REAL-previo-TheBuryProjectDb-20261105-151520.bak` |
| `docs/auditoria-funcional-2026-11-05.md`, `docs/h01-aplicacion-segura.md`, `docs/vendedor-sin-ventas-authorize.md` | 2026-11-05..07 | 0 | Todos describen la base ya limpia |
| `SELECT COUNT(*)` sobre `TheBuryProjectDb` (LocalDB), 2026-11-07 | 2026-11-07 | **0** (0 eliminadas lógicamente, 0 clientes, 136 migraciones) | Estado actual |
| `SELECT COUNT(*)` sobre `qa_export_20261107` (clon de QA) | 2026-11-07 | 67 | Datos generados por las pruebas de exportación; no es la base real |

## Qué quedó mal documentado

`docs/agentes/STATE.md` (la página que lee el coordinador) no registraba la limpieza y conservaba las cifras de 61 ventas sin aclarar que eran previas. Se agregó una fila de "Estado de datos" y se actualizó la fecha. Los documentos históricos fechados (`migracion-acuerdos-pago-procedimiento.md`) describen correctamente su momento y no se reescriben.

## Cómo verificar de nuevo

```sql
SELECT COUNT(*) FROM TheBuryProjectDb.dbo.Ventas;               -- 0 esperado hasta el primer uso real
SELECT COUNT(*) FROM __EFMigrationsHistory;                       -- 136
```

Para recuperar el estado previo (solo con autorización): restaurar el `.bak` indicado a una base **distinta** y contar allí; no restaurar sobre la real.

## Límite de esta verificación

No se restauró el `.bak` previo para contar sus 61 ventas; el 61 se apoya en los documentos del 2026-10-28 (que registran conteos antes y después de la migración) y en la memoria operativa de la limpieza. Si se necesita certeza adicional, restaurarlo en una base temporal con otro nombre (no destructivo).
