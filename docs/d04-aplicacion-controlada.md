# D-04 — procedimiento de aplicación controlada a la base real

**Estado: PREPARADO, NO APLICADO.** La migración `20261108185231_D04AperturaCajaUnicaAbierta` no se aplicó a `TheBuryProjectDb`. Requiere aprobación expresa del propietario. Fusionar el código (#267) no la aplica: el guard (#268) y la protección del CLI de EF lo impiden (ver «Qué impide aplicarla sin querer»).

## Qué cambia en el esquema
Solo índices de `AperturasCaja` (ninguna fila se modifica, borra ni recalcula):
- se elimina `IX_AperturasCaja_CajaId (CajaId)`;
- se crea `IX_AperturasCaja_CajaId_Cerrada (CajaId, Cerrada)`;
- se crea el índice **único filtrado** `UX_AperturasCaja_CajaId_Abierta (CajaId) WHERE Cerrada = 0 AND IsDeleted = 0`;
- se registra la migración (137 registros, antes 136).

Efecto funcional: la base rechaza una segunda apertura vigente de la misma caja (error 2601). La aplicación ya lo evitaba con locks (H-05/H-11); esto es defensa en profundidad. **El código nuevo no depende del índice**: se puede aplicar antes o después del despliegue.

## Artefactos
- `scripts/sql/d04-preflight-apertura-unica-caja.sql` — solo lectura.
- `scripts/sql/d04-migracion-apertura-unica-caja.sql` — transaccional, con bloqueo de aplicación, `DryRun`, idempotente y con guardas (base de destino, historial exacto de 136 migraciones, índices en el estado previo, **cajas con más de una apertura vigente → lista y aborta sin cambiar nada**). Incluye el bloque *Rollback*.
- Ambos se ejecutan con `sqlcmd -I` (el índice filtrado exige `QUOTED_IDENTIFIER ON`).

## Ensayo realizado (clon restaurado de un respaldo `COPY_ONLY` de la base real; nunca sobre la base real)
| Caso | Resultado |
|---|---|
| Preflight | 136 migraciones, 0 aperturas, 0 duplicados, índices previos correctos |
| `BaseEsperada` distinta | aborta (51051), sin cambios |
| `DryRun=1` | aplica y **revierte** (136 registros, índices previos) |
| Aplicación | 137 registros, los 2 índices nuevos, el viejo eliminado |
| Reaplicación | «ya está registrada… no se hace nada» |
| Dos aperturas vigentes de una caja (antes de aplicar) | aborta (51057), lista la caja, sin cambios |
| Historial alterado (falta una migración) | aborta (51054), sin cambios |
| Reversión (bloque *Rollback*) | vuelve a 136 y al índice original |
Además: 3 pruebas de SQL Server (`D-04 [IDX-1..3]`, incluye 8 inserciones simultáneas → 1 vigente) y 667 pruebas de Caja/Apertura en SQLite.

## Procedimiento propuesto (con la aplicación detenida y sin turno de caja en curso)
1. **Preflight** (solo lectura):
   `sqlcmd -S "(localdb)\MSSQLLocalDB" -d TheBuryProjectDb -E -C -I -b -f 65001 -i scripts\sql\d04-preflight-apertura-unica-caja.sql`
   Debe dar `BaseOk=1`, `Migraciones=136` (última `20261106072023_H01NumeroOrigenVenta`), `D04Registrada=0`, `CajasConDuplicadas=0`, índices `IX_AperturasCaja_CajaId` + PK y `ConexionesAjenas=0`. Si hay duplicados: **detenerse** y decidir cuál cerrar (no se depura automáticamente).
2. **Respaldo** inmediatamente antes:
   `BACKUP DATABASE TheBuryProjectDb TO DISK='D:\tmp\d04-previo-<fecha>.bak' WITH COPY_ONLY, CHECKSUM, INIT;`
   `RESTORE VERIFYONLY FROM DISK='D:\tmp\d04-previo-<fecha>.bak' WITH CHECKSUM;`
   Guardar la huella del preflight (conteos de Cajas, MovimientosCaja, Ventas, Cuotas, Creditos).
3. **Ensayo**: mismo comando de aplicación con `-v DryRun=1 -v BaseEsperada=TheBuryProjectDb`; debe imprimir «DryRun=1: se revierte» sin error.
4. **Aplicación**: `-v DryRun=0`; debe terminar en «Aplicado.».
5. **Verificación**: repetir el preflight (137 registros, `D04Registrada=1`, índices nuevos, huella idéntica); arrancar la aplicación con el código nuevo; abrir y cerrar una caja de prueba y comprobar que una segunda apertura de la misma caja sigue rechazándose con el mensaje de negocio.
6. **No** habilitar `Database:AllowMigrations` ni usar `--migrate`: la aplicación se hace solo con el script, que es explícito y auditable.

## Recuperación
- **Preferida:** restaurar el respaldo del paso 2 (`ALTER DATABASE … SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE … WITH REPLACE;`). Se pierde lo registrado después del respaldo.
- **Solo los índices:** bloque *Rollback* al final del script (inocuo para los datos; después no hay nada que revertir en el código).

## Qué impide aplicarla sin querer
| Camino | Protección |
|---|---|
| Arranque normal de la app (`InitializeOnStartup=true`) | `MigracionesGuard`: base con historial y pendientes → exit 1, sin migrar ni sembrar |
| `--configurar-documentos` | mismo guard antes de `MigrateAsync` |
| `dotnet ef database update` (con o sin `--connection`) | `AppDbContextFactory` rechaza el destino `TheBuryProjectDb` (o sin catálogo explícito) sin `Database:AllowMigrations=true`, y un interceptor de conexión (`GuardConexionRealInterceptor`) bloquea **abrir** la conexión aunque `--connection` cambie el destino después; sin conexión explícita usa `TheBuryProjectDb_DesignTime`. Verificado: los tres casos fallan y el historial real sigue en 136 |
| `--migrate` / Docker (`migrate` service) | explícito por diseño; apunta al SQL del stack, no a la LocalDB de desarrollo. Un deploy aplicaría D-04: **no desplegar sin aprobación** |
| Variable de usuario `Database__InitializeOnStartup=False` | primera capa ya presente en esta máquina |

## Pendiente de aprobación
- [ ] Autorizar la ventana de aplicación (app detenida, sin turno abierto).
- [ ] Confirmar la ruta del respaldo.
