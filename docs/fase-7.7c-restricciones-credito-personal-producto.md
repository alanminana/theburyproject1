# Fase 7.7C - Restricciones de credito personal por producto

Agente: Fase 7.7C - Documentar credito producto

## A. Contexto

Fase 7.7B dejo cerrado que Nueva Venta mantiene el contrato funcional:

**1 venta = 1 TipoPago principal.**

Ese contrato sigue vigente. `ProductoCondicionPago*` no vuelve a ser fuente de pago por producto, no define un medio por item y no calcula totales, caja, reportes, comprobantes ni contratos por producto.

La excepcion vigente es acotada a `TipoPago.CreditoPersonal`: las condiciones por producto pueden actuar como restriccion de elegibilidad y rango del carrito.

Nota posterior Fase 7.10: el admin legacy de condiciones de pago por producto fue retirado junto con sus endpoints admin `Producto/CondicionesPago/{productoId}`, `IProductoCondicionPagoService` / `ProductoCondicionPagoService` y `wwwroot/js/producto-condiciones-pago-modal.js`. Esta fase sigue vigente solo para `CreditoPersonal`: `ProductoCondicionPago`, `ProductoCondicionPagoRules` y `CondicionesPagoCarritoResolver` permanecen como soporte de elegibilidad/rango. Nueva Venta continua con configuracion global de pagos.

## B. Distincion formal

### Pago por producto legacy

Clasificacion: legacy / compatibilidad.

El pago por producto implica que cada item pueda seleccionar o persistir un medio, tarjeta o plan propio para calcular la venta. Ese modelo no es canonico para Nueva Venta.

No debe reactivarse:

- `VentaDetalle.TipoPago` como fuente funcional del medio de pago.
- `VentaDetalle.ProductoCondicionPagoPlanId` como plan activo por item en Nueva Venta.
- ajustes por item como motor financiero canonico.
- caja, reportes o comprobantes leyendo el medio desde cada detalle.
- seleccion de tarjetas/planes por producto para reemplazar la configuracion global de la venta.

### Restriccion de credito personal por producto

Clasificacion: canonico acotado para `CreditoPersonal`.

Para credito personal, `ProductoCondicionPago*` se usa como capa de restriccion del carrito:

- `Permitido = false` bloquea `TipoPago.CreditoPersonal` si algun producto del carrito no lo admite.
- `MaxCuotasCredito` reduce el maximo efectivo de cuotas usando la regla mas restrictiva del carrito.

Esta restriccion no convierte la venta en pago por producto. El medio principal sigue siendo `Venta.TipoPago = CreditoPersonal` y el plan financiero se configura sobre el credito/venta completa.

## C. Campos usados

Para `CreditoPersonal`, el uso vigente de `ProductoCondicionPago` es:

- `ProductoId`: identifica el producto cuya condicion restringe el carrito.
- `TipoPago`: debe ser `TipoPago.CreditoPersonal`.
- `Permitido`: permite o bloquea el medio para ese producto.
- `MaxCuotasCredito`: limita el maximo efectivo de cuotas.
- `Activo` / `IsDeleted`: filtran condiciones vigentes.

Campos de soporte leidos por el resolver:

- `Observaciones` y metadatos de DTO pueden aparecer en diagnostico o administracion.
- relaciones de tarjetas/planes existen por compatibilidad del modelo general, pero no son la fuente financiera de credito personal.

## D. Campos NO usados para credito personal

Para `CreditoPersonal`, no deben usarse como regla financiera activa:

- `ProductoCondicionPagoPlan.CantidadCuotas` como plan seleccionable por item.
- `ProductoCondicionPagoPlan.AjustePorcentaje`.
- `ProductoCondicionPagoPlan.TipoAjuste`.
- `ProductoCondicionPagoTarjeta.*`.
- `MaxCuotasSinInteres`.
- `MaxCuotasConInteres`.
- `PorcentajeRecargo`.
- `PorcentajeDescuentoMaximo`.
- `VentaDetalle.TipoPago`.
- `VentaDetalle.ProductoCondicionPagoPlanId`.
- `VentaDetalle.PorcentajeAjustePlanAplicado`.
- `VentaDetalle.MontoAjustePlanAplicado`.

Credito personal no aplica planes por item ni ajustes por plan hasta que exista una decision funcional explicita sobre capital, tasa, total final o descuento/recargo comercial.

## E. Servicios canonicos

### `CondicionesPagoCarritoResolver`

Clasificacion: canonico acotado.

Lee condiciones vigentes por producto y delega la resolucion de carrito en `ProductoCondicionPagoRules.ResolverCondicionesCarrito`.

Uso permitido para credito personal:

- bloqueo por `Permitido`;
- maximo restrictivo por `MaxCuotasCredito`;
- resolucion por carrito multiproducto.

### `CreditoRangoProductoService`

Clasificacion: canonico.

Cruza el rango base de credito personal con el resultado del carrito. El maximo efectivo queda como:

```text
min(maximoBaseCredito, MaxCuotasCredito mas restrictivo del carrito)
```

Si el minimo base supera el maximo efectivo, devuelve error de rango invalido.

### `CreditoConfiguracionVentaService`

Clasificacion: canonico.

Resuelve tasa, gastos y rango base desde la configuracion de credito vigente, y despues aplica la restriccion por producto mediante `ICreditoRangoProductoService`.

El comando final mantiene snapshots del rango efectivo y origen de restriccion cuando aplica.

### `VentaService`

Clasificacion: canonico con deuda legacy aislada.

Para credito personal:

- valida condiciones del carrito al crear/confirmar cuando corresponde;
- revalida `credito.CantidadCuotas` contra el maximo efectivo por producto antes de confirmar credito;
- ignora planes por item para `CreditoPersonal`;
- normaliza pago por item legacy al crear/actualizar detalles;
- conserva `Venta.TipoPago` como fuente principal.

La presencia de metodos legacy de ajustes por item no autoriza a reactivar pago por producto.

## F. No tocar sin decision funcional

No modificar sin decision funcional explicita:

- semantica de `MaxCuotasCredito`;
- incorporacion de `MinCuotasCredito`;
- planes por item para credito personal;
- ajustes de `ProductoCondicionPagoPlan` aplicados a credito personal;
- tasa, perfil, gastos administrativos o CFTEA por producto;
- calculo de caja/reportes/comprobantes desde condiciones por producto;
- persistencia de `VentaDetalle.TipoPago` o `ProductoCondicionPagoPlanId` como dato activo de Nueva Venta;
- migraciones relacionadas con credito personal por producto;
- Seguridad o permisos del modulo administrativo.

## G. Relacion con documentos previos

- `docs/fase-5.1-diseno-configuracion-global-pagos-venta.md`: sigue siendo el ADR canonico para el contrato global `1 venta = 1 TipoPago principal`.
- `docs/fase-7.5-handoff-pago-producto-legacy.md`: sigue vigente para pago por producto legacy, con la excepcion acotada documentada en esta fase para `CreditoPersonal`.
- `docs/fase-9.1-diseno-restricciones-credito-personal-producto.md`: antecedente tecnico del rango de credito personal por producto.
- `docs/fase-16.2-cierre-credito-personal-planes.md`: cierre que confirma que credito personal conserva solo bloqueo por producto y `MaxCuotasCredito`, sin planes ni ajustes.
- Fase 7.10: retiro del admin legacy, endpoints admin, service admin y JS del modal. No cambia la restriccion vigente de `CreditoPersonal`.

## H. Decisiones documentadas

- `ProductoCondicionPago*` no es pago por producto para `CreditoPersonal`.
- `CreditoPersonal` usa `ProductoCondicionPago.Permitido` como restriccion de elegibilidad.
- `CreditoPersonal` usa `ProductoCondicionPago.MaxCuotasCredito` como restriccion de rango.
- La venta conserva un unico `TipoPago` principal.
- El credito se configura para la venta completa, no para cada item.
- Caja, reportes, comprobantes y contratos no se derivan de condiciones por producto.
- Cualquier extension de planes, ajustes, tasa o perfil por producto requiere decision funcional nueva.

## I. Checklist actualizado

- [x] Distincion entre pago por producto legacy y restriccion de credito personal documentada.
- [x] Campos usados por `CreditoPersonal` documentados.
- [x] Campos no usados por `CreditoPersonal` documentados.
- [x] Servicios canonicos identificados.
- [x] Restricciones que no deben tocarse sin decision funcional documentadas.
- [x] Fase documentada sin modificar codigo productivo.
- [x] Fase documentada sin modificar tests.
- [x] Fase documentada sin crear migraciones.
- [x] Fase documentada sin tocar Seguridad.
- [x] Fase documentada sin tocar UI.
- [x] Fase 7.10 documentada como retiro de admin legacy sin reactivar pago por producto.
- [ ] Mantener esta distincion referenciada en futuros handoffs que toquen credito personal, condiciones por producto o Nueva Venta.
