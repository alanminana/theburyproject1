# Contrato vigente de pagos e indice legacy

Agente: Fase 7.7F - Indice contrato vigente pagos

## A. Proposito

Este documento es el indice unico para separar el contrato vigente de pagos de los documentos historicos o legacy vinculados a `ProductoCondicionPago*`.

No reemplaza los documentos canonicos existentes. Los referencia y deja claro como deben leerse para futuras fases.

Nota posterior Fase 7.10: el admin legacy de condiciones de pago por producto fue retirado. Tambien fueron retirados sus endpoints admin `Producto/CondicionesPago/{productoId}`, el service admin `IProductoCondicionPagoService` / `ProductoCondicionPagoService` y el JS `wwwroot/js/producto-condiciones-pago-modal.js`. Se conservan `ProductoCondicionPago`, `ProductoCondicionPagoRules` y `CondicionesPagoCarritoResolver` solo por la restriccion acotada de `CreditoPersonal`. Nueva Venta sigue usando configuracion global de pagos.

Nota posterior Fase 7.17: CreditoPersonal migro a `ProductoCreditoRestriccion`. `ICondicionesPagoCarritoResolver` fue eliminado de DI, VentaService y CreditoController. Endpoints legacy `GetMediosPagoPorProducto` y `DiagnosticarCondicionesPagoCarrito` retirados.

Nota posterior Fase 7.18: `CondicionesPagoCarritoResolver`, `ICondicionesPagoCarritoResolver`, `ProductoCondicionPagoRules` y `MediosPagoPorProductoResultado` eliminados del codebase. Sin consumidores runtime remanentes. Tests de caracterizacion legacy (`CondicionesPagoCarritoResolverTests`, `ProductoCondicionPagoRulesTests`) eliminados junto con los componentes.

## B. Contrato vigente

### 1 venta = 1 TipoPago principal

Nueva Venta conserva un unico medio principal por venta:

- `Venta.TipoPago` es la fuente funcional del medio de pago.
- Caja, reportes, comprobantes y validaciones principales deben leer el medio desde la venta y sus snapshots/datos persistidos.
- `VentaDetalle.TipoPago` y datos por item no son la fuente canonica del medio de pago en ventas nuevas.

### Configuracion global de pagos

La configuracion vigente de medios, tarjetas, planes, recargos, descuentos y validaciones opera a nivel venta.

Componentes canonicos o de soporte:

- `ConfiguracionPago`: configuracion global por `TipoPago`.
- `ConfiguracionTarjeta`: tarjetas asociadas a medios globales.
- `ConfiguracionPagoPlan`: planes/cuotas globales cuando aplique.
- servicios de venta/configuracion que validan y aplican el resultado final en backend.

El frontend puede previsualizar, pero el backend conserva la autoridad final sobre validacion y calculo.

### DatosTarjeta y snapshot global

`DatosTarjeta` pertenece al flujo global de la venta. Debe persistir datos aplicados al pago general, incluyendo tarjeta, plan global, cuotas y snapshots de ajuste cuando corresponda.

No debe usarse como puente para reactivar planes por producto en Nueva Venta.

### Comision individual por producto

La comision sigue siendo individual por producto.

Esta regla no equivale a pago por producto. Puede existir una base de comision por linea/producto sin que el sistema seleccione o calcule un medio, tarjeta o plan distinto por item.

### CreditoPersonal con restriccion acotada por producto

`CreditoPersonal` mantiene una excepcion acotada:

- `ProductoCondicionPago.Permitido` puede bloquear `CreditoPersonal` si algun producto del carrito no lo admite.
- `ProductoCondicionPago.MaxCuotasCredito` puede reducir el maximo efectivo de cuotas usando la regla mas restrictiva del carrito.

Esto no reactiva pago por producto. La venta sigue teniendo `Venta.TipoPago = CreditoPersonal` y el credito se configura para la venta completa.

## C. Documentos canonicos

Los documentos vigentes para interpretar pagos en Nueva Venta son:

- `docs/fase-5.1-diseno-configuracion-global-pagos-venta.md`: ADR base. Define configuracion global de pagos por venta, `1 venta = 1 TipoPago principal`, impacto en caja/reportes/comprobantes y no reactivacion de pago por producto.
- `docs/fase-7.5-handoff-pago-producto-legacy.md`: handoff que separa configuracion global canonica de `ProductoCondicionPago*` legacy administrativo/no canonico.
- `docs/fase-7.7c-restricciones-credito-personal-producto.md`: excepcion acotada para `CreditoPersonal` mediante `Permitido` y `MaxCuotasCredito`.
- `docs/contrato-vigente-pagos-y-legacy.md`: este indice/handoff, usado como puerta de entrada para distinguir contrato vigente de antecedentes historicos.

## D. Documentos historicos / legacy

Los siguientes documentos son referencia historica, administrativa o de caracterizacion legacy. No deben leerse como autorizacion para reactivar pago por producto en Nueva Venta:

- `docs/fase-8.2-condiciones-pago-producto.md`
- `docs/fase-8.9-cierre-condiciones-pago-producto.md`
- `docs/fase-14-2-condiciones-pago-modal.md`
- `docs/fase-15.1-diseno-cuotas-por-plan.md`
- `docs/fase-16.2-cierre-credito-personal-planes.md`

Lectura correcta:

- Pueden servir para entender deuda, decisiones previas, compatibilidad administrativa y caracterizacion del modulo.
- No son contrato vigente para medios, tarjetas, planes o totales de Nueva Venta.
- En `CreditoPersonal`, la lectura vigente se limita a bloqueo por `Permitido` y rango por `MaxCuotasCredito`, segun Fase 7.7C.

## E. Tests

### Tests canonicos

Los tests canonicos son los que protegen Nueva Venta, configuracion global, snapshots y lectura por `Venta.TipoPago`.

Referencias actuales relevantes:

- tests de `VentaService` y `VentaApiController` asociados a Nueva Venta y `TipoPago` general.
- tests de `ConfiguracionPago` / planes globales cuando validan medios, tarjetas o planes activos.
- tests de `VentaServiceGuardarDatosTarjetaTests` cuando validan `DatosTarjeta`, plan global, snapshot y total final.
- tests de `CajaServiceTests` cuando validan que caja usa `Venta.TipoPago` y total final persistido.
- tests de credito personal cuando validan configuracion del credito para la venta completa y restriccion acotada por producto.

Estos tests deben ser la proteccion principal del contrato vigente.

### Tests legacy / caracterizacion historica

Los tests de `ProductoCondicionPago*` deben leerse como caracterizacion legacy/admin historica, no como contrato canonico de Nueva Venta.

La etiqueta vigente es:

```csharp
[Trait("Area", "LegacyPagoPorProducto")]
```

Archivos historicos identificados en este frente:

- `TheBuryProyect.Tests/Unit/ProductoCondicionPagoPlanReadinessTests.cs`
- `TheBuryProyect.Tests/Integration/ProductoCondicionPagoEfTests.cs`

Eliminados en Fase 7.18 (sin consumidores runtime):
- `TheBuryProyect.Tests/Unit/ProductoCondicionPagoRulesTests.cs`
- `TheBuryProyect.Tests/Integration/CondicionesPagoCarritoResolverTests.cs`

Nota posterior Fase 7.10: los contratos del modal/admin y del service admin retirado dejaron de ser contrato vigente. La cobertura que permanecia debia proteger reglas/resolver usados por `CreditoPersonal`.

Nota posterior Fase 7.18: con la migracion de `CreditoPersonal` a `ProductoCreditoRestriccion` (Fase 7.17), los tests de caracterizacion del resolver fueron eliminados. No queda cobertura legacy del resolver; los tests remanentes de entidades (`ProductoCondicionPagoPlanReadinessTests`, `ProductoCondicionPagoEfTests`) solo verifican estructura de entidades DB.

Nota posterior Fase 7.20: `ProductoCondicionesPagoAdminLegacyDespublicadoTests` consolidado de 4 tests a 2 (`LegacyAdmin_NoPresente_EnVistaYAsset`, `LegacyAdmin_NoPresente_EnControllerYDI`). Se agrego `[Trait("Area", "LegacyPagoPorProducto")]` para consistencia. Todos los asserts originales se conservaron.

## F. Que NO debe hacerse

No se debe:

- reactivar pago por producto en Nueva Venta;
- usar `ProductoCondicionPagoPlanId` para pagos globales;
- enviar o depender de planes por item desde Nueva Venta;
- expandir `ProductoCondicionPagoRules` como motor de pagos globales;
- usar `ProductoCondicionPagoService` como base de configuracion global de venta;
- confundir comision individual por producto con pago por producto;
- usar `VentaDetalle.TipoPago` como fuente funcional del medio en ventas nuevas;
- calcular caja, reportes o comprobantes desde condiciones por producto;
- eliminar `CreditoPersonal`, `ProductoCondicionPago.Permitido` o `ProductoCondicionPago.MaxCuotasCredito` sin decision funcional explicita;
- tocar migraciones, Seguridad o permisos para reactivar este flujo sin fase aprobada.

## G. Clasificacion operativa

### Canonico

- `Venta.TipoPago` como medio principal.
- Configuracion global de pagos.
- `DatosTarjeta` como snapshot/datos globales de venta.
- Planes globales de pago cuando correspondan.
- Comision individual por producto como regla separada del medio de pago.
- Restriccion acotada de `CreditoPersonal` por `Permitido` y `MaxCuotasCredito`.

### Legacy / administrativo

- `ProductoCondicionPago*` entidades y datos historicos en DB (no eliminar sin inventario).
- Modal/admin legacy de condiciones por producto: retirado en Fase 7.10, junto con endpoints admin, service admin y JS asociado.
- Reglas/resolver: `ProductoCondicionPagoRules`, `CondicionesPagoCarritoResolver`, `ICondicionesPagoCarritoResolver` eliminados en Fase 7.18.
- Planes por producto y ajustes por item.
- Tests etiquetados con `Area=LegacyPagoPorProducto` remanentes: solo estructura de entidades.

### Incierto / requiere verificacion futura

- Estrategia de retiro de datos/modelo legacy remanente de `ProductoCondicionPago*` en DB (tablas, columnas historicas).
- Limpieza de columnas legacy en ventas/detalles historicos.
- Modelos DTO remanentes: `CondicionesPagoCarritoResultado.cs`, `ProductoCondicionesPagoDtos.cs` (solo usados por tests de estructura de entidad; candidatos a eliminar junto con `ProductoCondicionPagoPlanReadinessTests`).

No eliminar ni ocultar componentes legacy sin inventario real de rutas, permisos, vistas, servicios, tests y datos historicos.

## H. Checklist actualizado

- [x] Contrato vigente `1 venta = 1 TipoPago principal` documentado.
- [x] Configuracion global de pagos documentada como camino canonico.
- [x] `DatosTarjeta` / snapshot global documentado.
- [x] Comision individual por producto diferenciada de pago por producto.
- [x] `CreditoPersonal` documentado con restriccion acotada por producto.
- [x] Documentos canonicos listados.
- [x] Documentos historicos/legacy listados.
- [x] Tests canonicos y legacy separados.
- [x] Trait `Area=LegacyPagoPorProducto` referenciado.
- [x] Restricciones de no reactivacion documentadas.
- [x] Sin cambios de codigo, tests, migraciones ni Seguridad en esta fase.
- [x] Fase 7.8: acceso visible desde Catalogo a condiciones por producto oculto sin borrar endpoints, servicios, entidades ni tests legacy.
- [x] Fase 7.10: admin legacy, endpoints admin, service admin y JS de modal retirados.
- [x] Fase 7.17: CreditoPersonal migrado a ProductoCreditoRestriccion; ICondicionesPagoCarritoResolver eliminado de DI, VentaService y CreditoController.
- [x] Fase 7.18: CondicionesPagoCarritoResolver, ICondicionesPagoCarritoResolver, ProductoCondicionPagoRules, MediosPagoPorProductoResultado y tests de caracterizacion legacy eliminados.
- [x] Fase 7.20: ProductoCondicionesPagoAdminLegacyDespublicadoTests consolidado de 4 a 2 tests; [Trait] agregado; todos los asserts conservados.
- [ ] Inventario futuro de datos/modelo legacy remanente de `ProductoCondicionPago*` en DB.
- [ ] Cleanup de CondicionesPagoCarritoResultado.cs, ProductoCondicionesPagoDtos.cs y ProductoCondicionPagoPlanReadinessTests.cs (solo usados entre si).
