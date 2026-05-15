# Fase 8.2 — Cierre documental: Trazabilidad individual

**Estado:** Cerrado funcionalmente  
**Fecha de cierre:** 2026-05-15  
**Agente:** Fase 8.2.V — Cierre documental trazabilidad individual  
**Alcance:** documentación de cierre. Sin cambios de código, migraciones, Caja, comprobantes, VentaService ni ProductoUnidadService.

---

## A. Resumen ejecutivo

El bloque 8.2 implementa trazabilidad individual de unidades físicas para productos que lo requieren.

Conceptos clave:

- **Producto** = SKU. Representa el artículo como entidad comercial. Tiene `StockActual` agregado por SKU.
- **ProductoUnidad** = unidad física individual. Una instancia concreta del SKU con código interno propio y número de serie opcional.
- **MovimientoStock** = kardex agregado por SKU. Registra entradas, salidas y ajustes de stock a nivel producto.
- **ProductoUnidadMovimiento** = historial físico individual. Registra cada cambio de estado de cada unidad física, con motivo, usuario y referencia operativa.

Las dos trazabilidades coexisten y son independientes:

- `MovimientoStock` registra qué pasó con el stock agregado del SKU.
- `ProductoUnidadMovimiento` registra qué pasó con cada unidad física individual.

La integración se produce en la venta: al confirmar, el sistema descuenta stock agregado (kardex) Y marca la unidad física como Vendida. Al cancelar, revierte ambas.

---

## B. Contrato funcional vigente

### Reglas de validación en venta (crear y editar)

Para cada `VentaDetalle` con `Producto.RequiereNumeroSerie = true`:

1. `ProductoUnidadId` debe estar informado (obligatorio).
2. La unidad debe pertenecer al mismo `ProductoId` del detalle.
3. La unidad debe estar en estado `EstadoUnidad.EnStock`.
4. La unidad no debe estar soft-deleted.
5. `Cantidad` debe ser exactamente `1`.
6. No puede haber dos líneas en la misma venta con el mismo `ProductoUnidadId`.

Para productos con `RequiereNumeroSerie = false`:

- `ProductoUnidadId` debe ser `null`. Si viene informado: la venta es rechazada.

### Reglas de confirmación

Al confirmar una venta:

- Se re-verifica que la unidad siga en estado `EnStock` al momento de confirmar (mitigación de race condition).
- Si la unidad ya fue vendida entre la creación y la confirmación: se lanza excepción descriptiva.

### Reglas de cancelación

Al cancelar una venta confirmada o facturada:

- La unidad trazable se revierte a `EnStock` (`RevertirVentaAsync`).
- El stock agregado se revierte vía `MovimientoStockService.RegistrarEntradasAsync`.
- Se limpia `VentaDetalleId`, `ClienteId` y `FechaVenta` en la unidad.
- Se registra movimiento de historial individual con motivo.

Al cancelar una venta no confirmada:

- La unidad nunca fue marcada como `Vendida`.
- No se toca `ProductoUnidad`. El detalle queda soft-deleted.

### Activación/desactivación de trazabilidad

- La trazabilidad se activa o desactiva por acción explícita del usuario (`ActivarTrazabilidad` / `DesactivarTrazabilidad`).
- La edición normal de un producto no modifica `RequiereNumeroSerie`.
- `CambiarTrazabilidadIndividualAsync` en `ProductoService` gestiona el cambio.
- Para desactivar trazabilidad, el servicio valida que no existan unidades en estado bloqueante.

### Compatibilidad histórica

- Ventas previas a la Fase 8.2.E con `ProductoUnidadId = null` son válidas aunque el producto sea trazable.
- La validación de unidad obligatoria aplica solo a ventas nuevas o editadas después de la implementación.

### Decisión V1 — cantidad por línea

**Una línea trazable = `Cantidad 1`.** Para vender 3 unidades del mismo SKU trazable: 3 líneas con 3 `ProductoUnidadId` distintos.

### Decisión V1 — momento de transición

La unidad pasa a `Vendida` únicamente al confirmar la venta. No al cotizar, no al reservar.

| Evento | Estado unidad | Acción |
|---|---|---|
| `CreateAsync` (Cotización/Presupuesto) | Sin cambio | Solo validar disponibilidad |
| `UpdateAsync` | Sin cambio | Re-validar disponibilidad de la unidad asignada |
| `ConfirmarVentaAsync` | `EnStock → Vendida` | `MarcarVendidaAsync` |
| `ConfirmarVentaCreditoAsync` | `EnStock → Vendida` | Igual |
| Preview / CalcularTotales | Sin cambio | Nunca tocar estado |
| Cancelar venta no confirmada | Sin cambio | No tocar unidad |
| Cancelar venta confirmada/facturada | `Vendida → EnStock` | `RevertirVentaAsync` |

---

## C. Estados de unidad (`EstadoUnidad`)

| Estado | Valor | Significado operativo | Aparece en selector de venta | Permite ajuste | Cuenta como disponible | Bloquea desactivación de trazabilidad |
|---|---|---|---|---|---|---|
| `EnStock` | 0 | Unidad disponible para venta | Sí | Sí (puede pasar a Faltante o Baja) | Sí | No |
| `Reservada` | 1 | Reservada para una operación (no implementado en V1) | No | No (estado futuro) | No | Sí (si existe) |
| `Vendida` | 2 | Asignada a una venta confirmada | No | No (solo vía cancelación) | No | Sí |
| `Entregada` | 3 | Entregada físicamente al cliente | No | No (estado futuro) | No | Sí |
| `Devuelta` | 4 | Devuelta por el cliente (en evaluación) | No | Sí (puede pasar a EnStock o Baja) | No | Sí |
| `EnReparacion` | 5 | En servicio técnico | No | No (estado futuro) | No | Sí |
| `Faltante` | 6 | No encontrada físicamente | No | Sí (puede reintegrarse a EnStock) | No | Sí |
| `Baja` | 7 | Dada de baja definitiva | No | Sí (puede reintegrarse a EnStock si se cancela la baja) | No | Sí |
| `Anulada` | 8 | Anulada por causa externa (robo, error de ingreso) | No | No | No | Sí |

**Nota:** La cancelación de venta usa `RevertirVentaAsync` (transición `Vendida → EnStock`), no `Anulada`. `Anulada` se reserva para bajas por causas externas, no para flujos automáticos del sistema.

---

## D. Flujos implementados

### Alta manual de unidad

- `POST /Producto/CrearUnidad/{productoId}`
- Crea una `ProductoUnidad` con `EstadoUnidad.EnStock`.
- Genera `CodigoInternoUnidad` automático.
- `NumeroSerie` es opcional.
- **No modifica `Producto.StockActual` ni crea `MovimientoStock`.**

### Carga masiva de unidades

- `POST /Producto/CargaMasivaUnidades/{productoId}`
- Crea múltiples unidades en batch.
- Mismas reglas que alta manual.
- **No modifica stock agregado.**

### Historial de unidad

- `GET /Producto/HistorialUnidad/{productoUnidadId}` (modal)
- Muestra todos los `ProductoUnidadMovimiento` de la unidad: estado anterior → nuevo, motivo, usuario, fecha.

### Listado de unidades por producto

- `GET /Producto/Unidades/{productoId}`
- Muestra todas las unidades del producto con su estado.
- Incluye panel de conciliación: `StockActual` vs unidades `EnStock`, diferencia, badge OK/Advertencia.

### Venta con unidad física

1. Usuario selecciona producto trazable en `Nueva Venta`.
2. UI carga unidades disponibles vía `GET /api/productos/{productoId}/unidades-disponibles`.
3. Usuario selecciona unidad del selector.
4. `VentaDetalle.ProductoUnidadId` se guarda en la línea.
5. Al crear/editar: validaciones en `VentaService.ValidarUnidadesTrazablesAsync`.
6. Al confirmar: `MarcarVendidaAsync(productoUnidadId, ventaDetalleId, clienteId, usuario)`.

### Edición de venta no confirmada

- Se puede cambiar la unidad asignada.
- Se re-valida disponibilidad de la nueva unidad.
- La unidad anterior queda libre (nunca fue marcada Vendida).

### Confirmación de venta

- Descuenta stock agregado: `MovimientoStockService.RegistrarSalidasAsync`.
- Marca unidad como Vendida: `ProductoUnidadService.MarcarVendidaAsync`.
- Re-valida estado `EnStock` justo antes de marcar (mitigación de race condition).
- Todo en la misma transacción EF Core.

### Cancelación de venta confirmada o facturada

- Devuelve stock agregado: `MovimientoStockService.RegistrarEntradasAsync`.
- Revierte unidad a EnStock: `ProductoUnidadService.RevertirVentaAsync`.
- Limpia `VentaDetalleId`, `ClienteId`, `FechaVenta` en la unidad.
- Registra movimiento individual con motivo.
- **La factura queda sin anular (deuda abierta en Comprobantes).**
- **El MovimientoCaja de ingreso queda activo (deuda abierta en Caja).**

### Ajustes físicos: Faltante, Baja, Reintegro

| Acción | Método | Transición |
|---|---|---|
| Marcar faltante | `MarcarFaltanteAsync` | `EnStock → Faltante` |
| Marcar baja | `MarcarBajaAsync` | `EnStock → Baja` |
| Reintegrar | `ReintegrarAStockAsync` | `Faltante/Baja → EnStock` |

**Ninguno de estos ajustes modifica `Producto.StockActual` ni crea `MovimientoStock` automáticamente.**

### Conciliación stock agregado vs unidades

- Panel en `/Producto/Unidades/{productoId}` muestra diferencia.
- `Diferencia = Producto.StockActual - UnidadesEnStock`.
- Badge `OK` si diferencia = 0; `Advertencia` si diferencia ≠ 0.

### Ajuste asistido de stock agregado

- Desde el panel de conciliación.
- Genera `MovimientoStock` de tipo `Ajuste` vía `MovimientoStockService.RegistrarAjusteAsync`.
- Usa `ConciliacionUnidad:{productoId}` como referencia.
- Requiere motivo obligatorio.
- **No modifica estados de `ProductoUnidad`.**

### Kardex y modal con motivo y referencia

- `MovimientoStock` incluye `Motivo` y `Referencia`.
- El Kardex SKU muestra estos campos.
- El modal de kardex/detalle los expone correctamente.
- `ProductoUnidadMovimiento` incluye `Motivo`, `OrigenReferencia` y `UsuarioResponsable`.

---

## E. Conciliación stock agregado vs unidades

### Regla fundamental

Una transición de estado de unidad física **nunca** modifica `Producto.StockActual` ni crea `MovimientoStock` por sí sola.

La única excepción es la acción explícita de ajuste asistido desde el panel de conciliación.

### Autoridades separadas

| Concepto | Autoridad | Fuente |
|---|---|---|
| Stock vendible por SKU | `Producto.StockActual` | `MovimientoStockService` |
| Disponibilidad física individual | `ProductoUnidad.Estado = EnStock` | `ProductoUnidadService` |
| Historial agregado de SKU | `MovimientoStock` | `MovimientoStockService` |
| Historial individual de unidad | `ProductoUnidadMovimiento` | `ProductoUnidadService` |

### Fórmula de diferencia

```
UnidadesEnStock = count(ProductoUnidad WHERE ProductoId = x AND Estado = EnStock AND IsDeleted = 0)
Diferencia = Producto.StockActual - UnidadesEnStock
```

- `Diferencia = 0`: stock agregado y unidades físicas disponibles coinciden.
- `Diferencia > 0`: hay más stock agregado vendible que unidades físicas disponibles.
- `Diferencia < 0`: hay más unidades físicas disponibles que stock agregado vendible.

### Referencia de ajuste asistido

Cuando se genera ajuste desde conciliación, la referencia usada es:

```
ConciliacionUnidad:{productoId}
```

### Motivo de separación estricta en V1

- Evitar movimientos automáticos silenciosos.
- Evitar doble ajuste en cancelaciones (venta ya genera reversión de kardex; unidad no debe generar otro movimiento).
- Mantener `MovimientoStock` como kardex agregado canónico independiente.
- Hacer visible la diferencia sin decidir por el usuario.

### Productos no trazables

- Productos con `RequiereNumeroSerie = false` quedan excluidos del reporte de conciliación obligatorio.
- Si tienen unidades cargadas (trazabilidad operativa opcional), se muestran sin estado de desvío crítico.

---

## F. Comportamiento en venta facturada (auditado en Fase 8.2.U)

Al cancelar una venta en estado `Facturada`:

| Acción | Resultado |
|---|---|
| Unidad física | Se revierte a `EnStock` correctamente |
| Stock agregado | Se revierte correctamente vía `RegistrarEntradasAsync` |
| Factura | **Queda sin anular** (deuda abierta en Comprobantes) |
| MovimientoCaja | **Queda activo** (deuda abierta en Caja) |

Estas dos deudas pertenecen a fases de Comprobantes y Caja respectivamente. No son defectos de trazabilidad: la trazabilidad física y de stock quedó correcta.

---

## G. Componentes canónicos

| Componente | Tipo | Rol |
|---|---|---|
| `Producto` | Entidad / canónico | SKU con stock agregado y flag `RequiereNumeroSerie` |
| `ProductoUnidad` | Entidad / canónico | Unidad física individual con estado y ciclo de vida |
| `ProductoUnidadMovimiento` | Entidad / canónico | Historial de cambios de estado de cada unidad física |
| `EstadoUnidad` | Enum / canónico | Todos los estados posibles de una unidad física |
| `VentaDetalle.ProductoUnidadId` | Campo nullable / canónico | FK a la unidad física asignada a esa línea de venta |
| `IProductoUnidadService` / `ProductoUnidadService` | Service / canónico | Ciclo de vida completo de unidades individuales |
| `VentaService` | Service / canónico | Orquesta venta, validación trazable, confirmación y cancelación |
| `ProductoService.CambiarTrazabilidadIndividualAsync` | Método / canónico | Activa/desactiva `RequiereNumeroSerie` con validaciones |
| `MovimientoStockService` | Service / canónico | Kardex agregado por SKU; separado de trazabilidad individual |
| `ProductoController` | Controller / canónico | Alta manual, carga masiva, ajustes, activar/desactivar trazabilidad, conciliación |
| `ProductoApiController` | Controller / canónico | Endpoint unidades disponibles para selector en venta |
| `MovimientoStockController` | Controller / canónico | Kardex SKU, ajuste asistido de conciliación |
| `Views/Producto/Unidades.cshtml` | Vista / canónico | Listado, historial, panel de conciliación, ajustes físicos |
| `Views/Venta/Create_tw.cshtml` | Vista / canónico | Nueva venta con selector de unidad para productos trazables |
| `Views/Venta/Edit_tw.cshtml` | Vista / canónico | Edición de venta con selector de unidad |
| `wwwroot/js/venta-create.js` | Script / canónico | Lógica de selector de unidad, validaciones frontend, cantidad bloqueada |
| `wwwroot/js/movimientos-inventario-modal.js` | Script / canónico | Modal de Kardex/detalle con motivo y referencia |

---

## H. Tests y validaciones

### Cobertura existente (88 tests pasan — filtro `ProductoUnidad|VentaServiceProductoUnidad|Conciliacion`)

| Archivo | Tests | Área |
|---|---|---|
| `ProductoUnidadTests.cs` | 11 | EF Core: entidades, relaciones, FK, índice único filtrado |
| `ProductoUnidadServiceTests.cs` | 42 | Service: creación, transiciones, validaciones, historial, conciliación |
| `VentaServiceProductoUnidadTrazabilidadTests.cs` | 38 | Integración: crear/editar/confirmar/cancelar venta con unidad trazable |
| `ConciliacionStockUnidadesTests.cs` | 13 | Conciliación: diferencias, conteos, ajuste asistido, separación estricta |

### Áreas cubiertas

- Alta manual individual: código interno, número de serie opcional, estado inicial EnStock.
- Carga masiva: batch de unidades, sin movimiento agregado.
- Validación en creación de venta: producto trazable sin unidad rechaza; unidad de otro producto rechaza; unidad no EnStock rechaza; cantidad > 1 rechaza; unidad duplicada en misma venta rechaza; producto no trazable con unidad informada rechaza.
- Confirmación: marca unidad Vendida; asigna VentaDetalleId y ClienteId; re-valida EnStock; race condition lanza excepción descriptiva.
- Cancelación de venta confirmada: revierte unidad a EnStock; limpia VentaDetalleId, ClienteId, FechaVenta; registra movimiento historial.
- Cancelación de venta no confirmada: no modifica unidad.
- Ajustes físicos: EnStock → Faltante; EnStock → Baja; Faltante/Baja → EnStock (reintegro). Ninguno modifica StockActual.
- Conciliación: diferencia 0, positiva, negativa; excluye productos no trazables; ajuste asistido crea MovimientoStock tipo Ajuste; referencia correcta; sin doble movimiento en cancelación.
- Historial individual: estado anterior/nuevo, motivo, usuario.
- Compatibilidad histórica: venta sin ProductoUnidadId es válida para productos trazables.
- UI contract: selector de unidad presente en Create_tw y Edit_tw; cantidad bloqueada para producto trazable.
- ProductoApiController: endpoint unidades disponibles.
- Activar/desactivar trazabilidad: validaciones de bloqueo por unidades en estados incompatibles.

### Fases de QA y auditoría

- **Fase 8.2.P:** QA final trazabilidad individual — smoke test E2E de los flujos principales.
- **Fase 8.2.Q/Q2:** gestión explícita de trazabilidad en producto — activar, desactivar, validaciones de bloqueo.
- **Fase 8.2.R:** UX de venta con producto trazable sin unidades disponibles — mensaje claro al usuario.
- **Fase 8.2.S:** auditoría edición venta con trazabilidad — tests y verificación.
- **Fase 8.2.T:** auditoría cancelación venta confirmada con trazabilidad — revertir unidad y stock.
- **Fase 8.2.U:** auditoría venta facturada con trazabilidad — reversión correcta, deudas de factura/caja identificadas.

---

## I. Qué NO incluye este cierre

Los siguientes puntos están fuera del alcance del bloque 8.2 y pertenecen a fases futuras:

- **Caja real:** reversión, anulación o acreditación de `MovimientoCaja` al cancelar una venta.
- **Anulación automática de factura:** la factura queda activa al cancelar una venta facturada.
- **Devoluciones/garantía con unidad:** flujo formal de devolución. **Implementado en Fase 10.3** (2026-05-15): `DevolucionDetalle.ProductoUnidadId`, auto-inferencia desde `VentaDetalle`, estado `Devuelta` al completar. Deuda restante: UI, Descarte→Baja, Reparacion→EnReparacion.
- **Reserva de unidades en ventas pendientes:** `EstadoUnidad.Reservada` no se usa en V1.
- **Race condition completa:** V1 mitiga re-validando EnStock en confirmación; la reserva formal queda para V2.
- **Permisos finos de conciliación:** V1 usa permiso `productos/edit`; un permiso específico `stock/conciliar` queda postergado.
- **Integración fiscal externa:** el sistema no envía eventos a ningún sistema fiscal al cancelar.
- **Reportes globales de unidades:** no hay pantalla global de inventario físico por estado.
- **Múltiples unidades por línea de venta:** V1 exige una línea por unidad; la tabla intermedia `VentaDetalleUnidad` queda para V2.

---

## J. Deudas abiertas

| Deuda | Prioridad | Fase recomendada |
|---|---|---|
| Factura queda activa al cancelar venta facturada | Alta | Comprobantes: anulación automática de factura |
| MovimientoCaja de ingreso no se revierte al cancelar | Alta | Caja: reversión/anulación/acreditación de movimientos |
| Devoluciones/garantía con unidad — UI (`DevolucionDetalle.ProductoUnidadId` implementado en Fase 10.3) | Media | UI devolucion: mostrar unidad, Descarte→Baja, Reparacion→EnReparacion |
| Race condition: dos ventas no confirmadas pueden intentar usar la misma unidad | Media | Implementar `EstadoUnidad.Reservada` en V2 |
| Reserva de unidades en ventas pendientes (negocio no lo exigió en V1) | Baja | Solo si el negocio lo requiere |
| Sin reporte global de inventario físico por estado | Baja | Reportes de unidades si negocio lo necesita |
| Permiso específico para conciliación (`stock/conciliar`) | Baja | Permisos finos en fase de seguridad |
| Warning Git multi-pack-index | Cosmético | `git multi-pack-index write` cuando convenga |
| Archivos ajenos no trackeados (`CTempapp_out.txt`, `npm-verificacion-*.txt`, etc.) | Cosmético | Limpiar manualmente o agregar a `.gitignore` |

---

## K. Próximos bloques recomendados

En orden de prioridad:

1. **Comprobantes — anulación de factura al cancelar venta facturada.**  
   Deuda funcional visible: la factura queda activa. Alto impacto operativo/contable.

2. **Caja real — reversión/anulación/acreditación de MovimientoCaja.**  
   Deuda funcional visible: el ingreso de caja queda activo al cancelar. Bloquea cierre contable correcto.

3. **Devoluciones/garantía con unidad.**  
   Permite cerrar el ciclo completo de vida de una unidad física en flujos post-venta. **Backend implementado en Fase 10.3.** UI y estados Descarte/Reparacion pendientes.

4. **Reportes globales de unidades por estado (si el negocio lo necesita).**  
   Inventario físico completo: cuántas unidades hay EnStock, Vendidas, en Baja, etc.

5. **Implementar `EstadoUnidad.Reservada` para mitigar race condition.**  
   Requiere definir el momento exacto de reserva y la duración.

6. **Permisos finos de conciliación y trazabilidad.**  
   Depende del modelo de seguridad del proyecto.

---

## L. Checklist final — Fases 8.2.A a 8.2.U

### Completado

- [x] **Fase 8.2.A:** Entidades `ProductoUnidad` y `ProductoUnidadMovimiento`. Migración. EF Core configurado.
- [x] **Fase 8.2.B:** `ProductoUnidadService`: creación, código interno, consultas, historial.
- [x] **Fase 8.2.C:** Transiciones: `MarcarVendidaAsync`, `MarcarFaltanteAsync`, `MarcarBajaAsync`, `ReintegrarAStockAsync`.
- [x] **Fase 8.2.D:** Diseño de integración `ProductoUnidad` con ventas. ADR de modelo, relaciones EF, reglas V1.
- [x] **Fase 8.2.E:** Backend integración ventas: `VentaDetalle.ProductoUnidadId`, `ValidarUnidadesTrazablesAsync`, hook en `ConfirmarVentaAsync` y `ConfirmarVentaCreditoAsync`, `RevertirVentaAsync`, hook en `CancelarVentaAsync`.
- [x] **Fase 8.2.F / F2:** Selector de unidad en Nueva Venta + validación E2E.
- [x] **Fase 8.2.G:** UI listado/historial de unidades por producto.
- [x] **Fase 8.2.H / H2:** Carga manual individual + validación.
- [x] **Fase 8.2.I:** Carga masiva de unidades.
- [x] **Fase 8.2.J / J2:** Ajustes físicos por unidad (Faltante, Baja, Reintegro) + validación.
- [x] **Fase 8.2.K / L / L2 / L3:** Conciliación stock agregado vs unidades: diseño, read model, panel en Unidades, tests.
- [x] **Fase 8.2.M / N:** Ajuste asistido de stock + validación E2E.
- [x] **Fase 8.2.O / O2 / O3 / O4:** Motivo en Kardex/modal y tests frágiles corregidos.
- [x] **Fase 8.2.P:** QA final trazabilidad individual.
- [x] **Fase 8.2.Q / Q2:** Gestión explícita de trazabilidad en producto (activar/desactivar con validaciones).
- [x] **Fase 8.2.R:** UX de venta con producto trazable sin unidades disponibles.
- [x] **Fase 8.2.S:** Auditoría edición venta con trazabilidad.
- [x] **Fase 8.2.T:** Auditoría cancelación venta confirmada con trazabilidad.
- [x] **Fase 8.2.U:** Auditoría venta facturada con trazabilidad.
- [x] **Fase 8.2.V:** Cierre documental de trazabilidad individual (este documento).

### Pendiente — fuera del bloque 8.2

- [ ] Anulación automática de factura al cancelar venta facturada (Comprobantes).
- [ ] Reversión de MovimientoCaja al cancelar venta (Caja).
- [x] Backend: devolución simple con unidad física (Fase 10.3 — 2026-05-15).
- [ ] UI: mostrar y gestionar unidad en flujo de devolución.
- [ ] Descarte → marcar unidad como Baja.
- [ ] Reparación → marcar unidad como EnReparacion.
- [ ] `EstadoUnidad.Reservada` para mitigar race condition en V2.
- [ ] Reportes globales de inventario físico por estado.
- [ ] Permisos finos de conciliación.

---

## M. Validaciones técnicas de esta fase

| Validación | Resultado |
|---|---|
| `git status --short` | Solo archivos ajenos no trackeados; `package-lock.json` modificado. Sin cambios en código de producción. |
| `git diff --check` | Sin whitespace errors. |
| `dotnet build --configuration Release` | Falla por file lock (`TheBuryProyect.exe` PID 36200 — app en ejecución). Error de copia de `apphost.exe`, no de compilación. |
| `dotnet build --configuration Release --artifacts-path .\_build_check` | **Compilación correcta. 0 errores, 0 advertencias.** |
| `dotnet test --filter "ProductoUnidad\|VentaServiceProductoUnidad\|Conciliacion"` | **88 tests pasan. 0 errores. 0 omitidos.** |
