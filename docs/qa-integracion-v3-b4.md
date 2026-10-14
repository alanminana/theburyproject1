# Integración QA V3 (2026-10-14)

Consolida las correcciones verificadas de QA V2 sobre `main` actual (runtime 10.0.13, PR #71) y corrige B4.
**No incluye** la PR #70 (ni los commits `446fb1da` / `95044284`), ni las vistas Mora ClientesMora/FichaCliente,
ni QA-FIN-RESIDUAL.

## Contenido
| Área | Corrección |
|---|---|
| Seguridad (P0) | Usuarios desactivados pierden acceso (login y sesiones abiertas) |
| Productos P0 | `movimientos.create` y `precios.apply` exigidos en el servidor |
| Productos P1 | Códigos, stock, alcance de precios, reversión, historial, advertencia, UI; **B4** (abajo) |
| Finanzas P0 | Cancelación con pagos aplicados rechazada; contramovimiento sin ambigüedad |
| Finanzas concurrencia | Lock compartido de crédito entre cobros y cancelaciones (sin deadlock) |
| Mora (configuración) | Preservación de campos, concurrencia `RowVersion`, validaciones |

## B4: idempotencia de movimientos de stock (`RequestId`)
Defectos hallados en la auditoría y su corrección:

1. **Mismo `RequestId` con otra intención devolvía éxito de la operación anterior.** Ahora se compara producto, tipo,
   cantidad solicitada (para `Ajuste`, el stock absoluto), motivo, referencia y orden de compra; si difiere se rechaza
   con un error explícito. Espacios y nulos/vacíos se normalizan.
2. **Formularios que regeneraban el `RequestId` tras un error con redirect.** Los formularios inline (ficha de
   inventario y modal) reutilizan el identificador del envío fallido (TempData `StockRequestIdPendiente`); tras un
   éxito vuelven a generar uno nuevo.
3. **Resultado incierto (timeout / corte en guardado o commit).** No se repite a ciegas: se revierte el intento y se
   consulta por `RequestId` si el movimiento quedó registrado; si se confirma se devuelve, si no se informa que el
   reenvío es seguro.
4. **Recuperación de la carrera contra el índice único.** Ya no se desvincula todo el `ChangeTracker`: solo el
   producto y el movimiento del intento. También cubre el caso sin transacción propia.

Sin `RequestId` (ventas, OC, scripts, conciliación de unidades) el comportamiento no cambia; los movimientos históricos
quedan con `RequestId` NULL.

### Migración `20261014175359_QaPrdP1MovimientoStockRequestId`
Aditiva: columna `RequestId uniqueidentifier NULL` e índice único filtrado `IX_MovimientosStock_RequestId`
(`RequestId IS NOT NULL`). No modifica ni elimina filas. Aparece una sola vez y el snapshot es consistente.
La aplican `Database:InitializeOnStartup` (`DbInitializer`, `Program.cs`) y el servicio `migrate` de Docker.

## Verificación
**C# (`dotnet test`, Release, SDK 10.0.402):** 5583 OK, 0 fallos, 21 omitidos.
Omitidos: 16 requieren SQL Server real (`QA_FIN_CONC_SQL`, harness de cancelación vs pago), 4 son sembradores E2E que
requieren una LocalDB descartable, 1 es un test de Mora marcado `Skip`.
Tests nuevos de B4: `QaPrdP1B4RemediacionTests` (13) y 2 pruebas HTTP en `QaPrdP1HttpTests`.

**SQL Server real (stack aislado `prd`, base `BuryQA_PRD`), imagen construida desde el commit de esta rama:**
- Migración aplicada una vez; 28 movimientos históricos intactos (conteo y checksum idénticos antes/después);
  respaldo verificado previo (`RESTORE VERIFYONLY`).
- E2E Productos: 48 de 49 OK. Incluye `05-b4-idempotencia.spec.js` (8): ocho envíos simultáneos con el mismo
  `RequestId` → 1 movimiento y 1 cambio de stock; reuso con otra cantidad/motivo/tipo rechazado; reintento tras
  error de negocio conserva el `RequestId` y se aplica una sola vez; CSRF (400 sin token) y permisos (lector 403).
- La única falla es **PR12 / B8** (precio de venta menor al costo se acepta sin bloqueo), pendiente de decisión de
  negocio y ajena a esta integración.
- `S21b` (doble envío) se actualizó para enviar el `RequestId` que ahora traen los formularios reales.

## Pendiente / no verificado
- E2E de **SEG, FIN y CFG** sobre esta imagen: no ejecutados. Requieren estrategia segura porque la imagen aplica
  la migración B4 automáticamente al arrancar contra su base.
- Reconciliar la **PR #70** (Mora vistas, skills, layouts) después de este merge; esta rama no la contiene.
- Aplicar la migración en otros entornos es una decisión aparte: no se aplicó a desarrollo ni a producción.
- PR12 / B8 (decisión de negocio).
