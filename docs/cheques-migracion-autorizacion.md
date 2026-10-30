# Cheques recibidos (H10): migración para autorizar

**Migración:** `20261030030301_H10ChequesRecibidos` · **Depende de:** `20261029012048_H4E2NotaCreditoCanje` (última aplicada en la base real) · **Estado:** generada y verificada en un clon; **no aplicada a la base real `TheBuryProjectDb`**.

## 1. Qué cambia

Solo DDL, sin SQL crudo, sin `UPDATE` de datos y sin cambios en filas existentes. Todas las columnas nuevas admiten `NULL`.

| Tabla | Cambio | Para qué |
|---|---|---|
| `MovimientosCaja` | columna `MovimientoOrigenId int NULL` | La acreditación (o el rechazo post-acreditación) de un cheque asentada en la caja vigente apunta al ingreso original, que vive en una caja ya cerrada. |
| `MovimientosCaja` | FK autorreferenciada `FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId` (`ON DELETE NO ACTION`) | Integridad del enlace. |
| `MovimientosCaja` | índice único filtrado `IX_MovimientosCaja_MovimientoOrigenId_Concepto` sobre `(MovimientoOrigenId, Concepto)` donde `MovimientoOrigenId IS NOT NULL` | Garantía de base de datos: un cheque tiene como máximo un asiento de acreditación y uno de rechazo (idempotencia aun si falla la lógica de aplicación). |
| `DatosCheque` | columnas `AcreditadoAt datetime2 NULL`, `AcreditadoPor nvarchar(50) NULL`, `RechazadoAt datetime2 NULL`, `RechazadoPor nvarchar(50) NULL`, `MotivoRechazo nvarchar(500) NULL` | Trazabilidad de la acreditación y del rechazo (usuario, fecha, motivo). |

Los conceptos nuevos `AcreditacionCheque = 13` y `RechazoCheque = 14` son valores de un enum guardado como entero: no requieren cambio de esquema.

Como el índice único es filtrado, no afecta a los 42 movimientos existentes (todos con `MovimientoOrigenId` nulo). No se agrega ninguna restricción sobre datos viejos.

## 2. Datos existentes en el clon de la base real (restaurado el 2026-10-30)

Barrido SQL previo (`D3`/`D6`), sobre una copia `COPY_ONLY` de `TheBuryProjectDb`:

| Control | Resultado |
|---|---|
| Ventas con medio `Cheque` | **0** |
| Filas en `DatosCheque` | **0** |
| D3: ventas cuyo cheque no coincide con el total a cobrar (fallarían al confirmar) | **0** |
| D6: banco + número duplicado entre ventas vigentes | **0** |
| D6: cheques con emisión futura o vencimiento pasado | **0** |
| Movimientos de caja de cheque recibido (`VentaCheque`) | **0** |
| Cobros de cuota con cheque (`CobroCuota` con `TipoPago = Cheque`) | **0** |

Conclusión: ninguna venta ni cobro existente cambia de comportamiento. Las reglas nuevas solo aplican a lo que se cargue desde ahora. El barrido es el script de solo lectura `docs/cheques-barrido-datos.sql` (`sqlcmd -I`); conviene correrlo también sobre la base real antes de aplicar.

## 3. Verificación Up / Down en el clon

Se aplicó sobre el clon `_h10_cheques` (restaurado desde un backup `COPY_ONLY` con `CHECKSUM`; `RESTORE VERIFYONLY` correcto), con los scripts de esta carpeta:

| Paso | Columnas nuevas | Índice | FK | Última migración | Movimientos / DatosCheque / Aperturas / Ventas |
|---|---|---|---|---|---|
| Antes | no | no | no | `…H4E2NotaCreditoCanje` | 42 / 0 / 25 / 61 |
| Up (`cheques-migracion-h10-up.sql`) | 6 | sí | sí | `…H10ChequesRecibidos` | 42 / 0 / 25 / 61 |
| Up repetido (idempotente) | sin cambios ni errores | — | — | igual | igual |
| Down (`cheques-migracion-h10-down.sql`) | 0 | no | no | `…H4E2NotaCreditoCanje` | 42 / 0 / 25 / 61 |
| Up final | 6 | sí | sí | `…H10ChequesRecibidos` | 42 / 0 / 25 / 61 |

Los conteos antes y después son idénticos en cada paso. La suite de concurrencia del circuito (6 casos) corrió en SQL Server con `READ_COMMITTED_SNAPSHOT` apagado y encendido.

> **Importante al ejecutar los scripts con `sqlcmd`:** usar la opción `-I` (`QUOTED_IDENTIFIER ON`). Sin ella falla la creación del índice filtrado (error 1934) y el script, que va dentro de una transacción, no deja nada aplicado.

## 4. Procedimiento para aplicar en la base real (lo ejecuta el propietario)

1. **Parar o poner en mantenimiento la aplicación** (una sola instancia): no debe haber cajas abriéndose ni cobros en curso.
2. **Backup verificado:**
   ```sql
   BACKUP DATABASE [TheBuryProjectDb] TO DISK = N'<ruta>\TheBuryProjectDb_pre_h10.bak' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;
   RESTORE VERIFYONLY FROM DISK = N'<ruta>\TheBuryProjectDb_pre_h10.bak' WITH CHECKSUM;
   ```
   Recomendado: restaurar esa copia en una base aparte y comparar conteos (`MovimientosCaja`, `DatosCheque`, `AperturasCaja`, `Ventas`).
3. **Barrido y conteos antes:** correr `docs/cheques-barrido-datos.sql` (debe seguir sin ventas afectadas) y `SELECT COUNT(*) FROM MovimientosCaja; … DatosCheque; … AperturasCaja; … Ventas;` y la última fila de `__EFMigrationsHistory`.
4. **Aplicar** `docs/cheques-migracion-h10-up.sql` con `sqlcmd -S <servidor> -d TheBuryProjectDb -E -I -b -i cheques-migracion-h10-up.sql`. El script es idempotente (cada bloque pregunta por `__EFMigrationsHistory`) y transaccional. Alternativa equivalente: `dotnet ef database update 20261030030301_H10ChequesRecibidos`.
5. **Conteos después** (deben ser idénticos) y verificar: seis columnas nuevas, índice `IX_MovimientosCaja_MovimientoOrigenId_Concepto`, FK `FK_MovimientosCaja_MovimientosCaja_MovimientoOrigenId`, última migración `…H10ChequesRecibidos`.
6. **Desplegar la versión de la app que incluye este cambio** y probar el circuito con un cheque de prueba (venta → confirmar → «Cheques pendientes» → acreditar/rechazar).

La app y la migración deben desplegarse juntas: la versión nueva lee las columnas nuevas y fallaría sin ellas; la versión anterior ignora las columnas nuevas y sigue funcionando.

## 5. Rollback

- **Antes de que exista algún acreditado/rechazado:** aplicar `docs/cheques-migracion-h10-down.sql` (`-I -b`). Quita FK, índice y las seis columnas. Es transaccional.
- **Después de usar el circuito:** el Down **elimina** la trazabilidad (quién acreditó o rechazó, cuándo y por qué) y desvincula los asientos de acreditación de sus cheques originales; los movimientos nuevos (`AcreditacionCheque`, `RechazoCheque`) quedarían en caja sin origen. Por eso, con datos reales cargados, el rollback correcto es **restaurar el backup del paso 2**, no el Down.
- Revertir la aplicación a la versión anterior sin tocar la base es seguro mientras no se haya usado el circuito nuevo.

## 6. Qué NO hace esta migración

No cambia filas existentes, no recalcula saldos, no migra el estado de cheques viejos (no hay), no crea permisos y no toca el módulo de cheques emitidos a proveedores (`Cheque`/`ChequeService`).
