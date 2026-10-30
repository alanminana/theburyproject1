# Compras: cancelación de una orden parcialmente recibida (H9-M6)

Verificado en el código y en un clon de la base real (OC-2026-0002 y OC-2026-0003, ambas EnTransito con 1/2 y 2/3 unidades recepcionadas).

## Qué pasa al cancelar (comportamiento actual, ya corregido donde se indica)

| Aspecto | Resultado |
|---|---|
| Estado final | `Cancelada` (terminal: `OrdenCompraTransiciones` no admite salidas; `Recepcionar` exige Confirmada/EnTransito). |
| Líneas | `CantidadRecibida` se conserva. La cantidad pendiente (`Cantidad - CantidadRecibida`) ya no se puede recibir. Antes el detalle seguía mostrando "Pendientes" / "N pendiente(s)" como si estuviera viva; ahora dice "Pendiente cancelado" / "N cancelado(s)". |
| Stock | Se conserva lo ya ingresado; no hay reversión (decisión coherente: la mercadería llegó). |
| Kardex | Los `MovimientoStock` de la recepción (`OrdenCompraId` + `Referencia = "Orden de Compra OC-..."`) quedan intactos. |
| Costos | Sin cambios: el costo se registra en cada recepción (`MovimientoStockCostoLinea`); cancelar no lo toca. |
| `FechaRecepcion` | Queda `NULL` (solo se completa cuando TODAS las líneas se reciben). |
| Recepciones anteriores | Intactas. |
| Auditoría | Antes: solo `UpdatedAt/UpdatedBy` y un log. Ahora: evento `Compras / CancelarOrdenCompra` en `SeguridadEventosAuditoria`, en la misma operación que el cambio de estado, con `EstadoPrevio`, `Recibido=x/y`, pendiente cancelado y "stock ingresado conservado (sin reversión)". |
| Permiso | Antes `CambiarEstado -> Cancelada` alcanzaba con `ordenescompra.update`. Ahora exige `ordenescompra.cancel` (igual que `Delete`); el selector del detalle no ofrece "Cancelada" sin ese permiso. Roles de sistema con `ordenescompra` completo (Administrador, Gerente) no cambian. |

## Defecto técnico confirmado y corregido

El índice único `IX_OrdenesCompra_Numero` está filtrado por `IsDeleted = 0`. `DeleteAsync` solo bloqueaba órdenes `EnTransito`/`Recibida`, de modo que una orden **Cancelada con recepción parcial se podía eliminar** (reproducido en el clon: OC-2026-0002 quedó `IsDeleted=1` y su movimiento de Kardex siguió apuntando a "Orden de Compra OC-2026-0002"). Eso libera el número: si era la última del año, `GenerarNumeroOrdenAsync` (máximo entre no eliminadas) lo vuelve a emitir, y el Kardex, que muestra la referencia como texto, queda ambiguo entre dos órdenes.

Corrección: `OrdenCompraService.DeleteAsync` y el `GET Delete` del controller rechazan eliminar una orden con `CantidadRecibida > 0` o con movimientos de stock asociados ("Dejala como Cancelada"). Una cancelada sin recepción sigue pudiéndose eliminar. Tests: `OrdenCompraServiceTests` (3) y `OrdenCompraBacklogHttpTests` (2).

No se tocó: el número de una orden cancelada **sin** eliminar nunca se reutiliza (sigue ocupando el índice).

## Decisiones de negocio pendientes (no implementadas)

El stock ya ingresado de una OC cancelada queda sin vínculo operativo con el proveedor. Opciones, sin revertir movimientos históricos automáticamente:

1. **Mantener como está (recomendada ahora).** Cancelar = "no se recibe el resto". Lo ingresado se conserva y queda auditado. Es lo que hoy refleja la UI con el aviso previo a cancelar.
2. **Cerrar la orden parcial como `Recibida` con cantidad menor** ("cierre con faltante"): estado nuevo o bandera `CerradaConFaltante`. Útil si el proveedor factura solo lo entregado; requiere decidir cómo se reporta el faltante. Más fiel al circuito real que cancelar.
3. **Devolución al proveedor** como circuito propio (egreso de stock con motivo, referencia a la OC y, si corresponde, nota de crédito del proveedor). Es la única forma correcta de sacar stock ya recibido; hoy fuera de alcance por pedido. Hasta que exista, la corrección de stock va por un ajuste manual de `MovimientoStock` con referencia a la OC.

Recomendación: mantener 1 y evaluar 2 si compras necesita distinguir "cancelada sin recibir nada" de "cerrada con faltante". Llevar 3 a un lote propio con su regla contable.

## Pendiente menor

`CambiarEstadoAsync` no recibe `RowVersion`: cancelar en paralelo con una recepción se resuelve por la concurrencia de EF (error genérico "Error al cambiar el estado"), sin pérdida de datos. Bajo.
