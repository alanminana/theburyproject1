# Fase 7.5 - Handoff pago por producto legacy

Agente: Fase 7.5 - Handoff pago producto legacy

## A. Contexto

El frente de pagos ya consolido la decision funcional vigente:

**1 venta = 1 TipoPago principal.**

Nueva Venta debe operar con configuracion global de pagos a nivel venta. El medio de pago principal se toma de `Venta.TipoPago`, y los datos asociados al medio, tarjeta, plan o snapshot pertenecen al flujo global de la venta.

La regla funcional confirmada para comisiones es distinta y no debe confundirse con pago por producto:

**La comision es individual por producto.**

Esto significa que la base de comision puede evaluarse por linea/producto, pero no habilita que Nueva Venta vuelva a seleccionar o calcular un medio de pago distinto por producto.

## B. Diagnostico documental

### Fuente canonica vigente

`docs/fase-5.1-diseno-configuracion-global-pagos-venta.md` ya documenta el contrato principal:

- Nueva Venta usa `Venta.TipoPago` como fuente principal.
- El pago por producto queda aislado como compatibilidad legacy.
- Caja y reportes interpretan el medio desde la venta, no desde cada detalle.
- La configuracion global de pagos opera a nivel venta.
- Nueva Venta no debe enviar pago por detalle ni `ProductoCondicionPagoPlanId` por detalle.
- `ProductoCondicionPago*` esta clasificado como legacy para venta activa.

### Referencias ambiguas detectadas

Hay documentos posteriores que describen fases historicas de condiciones de pago por producto:

- `docs/fase-8.9-cierre-condiciones-pago-producto.md`
- `docs/fase-14-2-condiciones-pago-modal.md`
- `docs/fase-15.1-diseno-cuotas-por-plan.md`
- `docs/fase-16.2-cierre-credito-personal-planes.md`

Estas referencias no deben interpretarse como autorizacion para reactivar pago por producto en Nueva Venta. Deben leerse como documentacion historica, administrativa o de caracterizacion legacy.

`CLAUDE.md` y `AGENTS.md` no requieren cambios por esta fase. No se detecto una referencia puntual que obligue a tratar pago por producto como camino canonico de Nueva Venta.

Nota posterior Fase 7.7C: esta clasificacion no elimina la excepcion acotada de `CreditoPersonal`. Para ese medio, `ProductoCondicionPago.Permitido` y `ProductoCondicionPago.MaxCuotasCredito` siguen vigentes como restriccion de elegibilidad/rango del carrito, no como pago por producto ni plan por item. Ver `docs/fase-7.7c-restricciones-credito-personal-producto.md`.

Nota posterior Fase 7.10: el admin legacy de condiciones de pago por producto fue retirado. Ya no quedan como superficie vigente el modal administrativo, `wwwroot/js/producto-condiciones-pago-modal.js`, los endpoints admin `Producto/CondicionesPago/{productoId}` ni `IProductoCondicionPagoService` / `ProductoCondicionPagoService`. Las entidades/reglas/resolver remanentes se conservan solo para la restriccion acotada de `CreditoPersonal`. Nueva Venta sigue con configuracion global de pagos.

## C. Decision vigente

El camino canonico para ventas nuevas es:

- una venta tiene un unico `TipoPago` principal;
- la configuracion global define medios disponibles, tarjetas, planes, validaciones y ajustes;
- el backend valida y persiste el resultado final;
- Caja, reportes y comprobantes deben leer el medio de pago desde la venta y sus snapshots/datos persistidos;
- comision por producto se mantiene como regla propia, separada del modelo de pago.

## D. Que es canonico

### Canonic para Nueva Venta

- `Venta.TipoPago` como fuente principal del medio.
- Configuracion global de pagos por venta.
- Validaciones backend sobre medio/tarjeta/plan global.
- Snapshot o datos persistidos de pago aplicados a la venta.
- Caja y reportes usando la venta como fuente del medio y del total final.
- Comision individual por producto, sin convertir eso en pago por producto.

### Canonic parcial o de soporte

- `ConfiguracionPago`: medio global por `TipoPago`.
- `ConfiguracionTarjeta`: tarjetas asociadas a configuracion global.
- `DatosTarjeta`: snapshot/datos de tarjeta asociados a la venta cuando aplique.
- Tests de venta/caja/reportes/configuracion global que protegen el contrato `Venta.TipoPago`.

## E. Que queda legacy

El flujo `ProductoCondicionPago*` queda como legacy/no canonico para Nueva Venta.

Incluia historicamente:

- `ProductoCondicionPagoService`
- `ProductoCondicionPagoRules`
- entidades `ProductoCondicionPago`, `ProductoCondicionPagoTarjeta`, `ProductoCondicionPagoPlan`
- modal administrativo de condiciones por producto
- `wwwroot/js/producto-condiciones-pago-modal.js`
- tests relacionados con el modal y reglas de condiciones por producto

Nota historica Fase 7.8: el acceso visible desde Catalogo al modal de condiciones por producto fue ocultado. En ese momento no se borraron endpoints, servicios, entidades, migraciones ni tests legacy y el bloque quedaba disponible solo para compatibilidad/admin futuro con decision explicita.

Nota posterior Fase 7.10: el bloque administrativo ya no queda disponible como compatibilidad futura. Se retiraron modal/admin, JS, endpoints admin y service admin. Permanecen solo las piezas necesarias para `CreditoPersonal`: entidad `ProductoCondicionPago`, reglas puras y resolver de carrito.

Uso permitido despues de Fase 7.10:

- caracterizacion del comportamiento legacy;
- lectura de `ProductoCondicionPago.Permitido` y `MaxCuotasCredito` para `CreditoPersonal`;
- preparacion de retiro o aislamiento futuro de datos/modelo remanente, con decision explicita.

Uso no permitido:

- tomarlo como base para Nueva Venta;
- reactivar seleccion de pago por producto;
- calcular totales finales de venta desde condiciones por producto;
- registrar caja o reportes desde condiciones por producto;
- duplicar reglas financieras sensibles entre este flujo y la configuracion global.

## F. Que no debe hacerse

No se debe:

- recrear `ProductoCondicionPagoService` para soportar Nueva Venta;
- recrear endpoints admin `Producto/CondicionesPago/{productoId}` sin fase aprobada;
- reactivar el modal/admin legacy de condiciones por producto;
- expandir `ProductoCondicionPagoRules` como motor de pagos globales;
- modificar `venta-create.js` para enviar pago por detalle;
- modificar `VentaService` para resolver medios por producto;
- usar `ProductoCondicionPagoPlanId` por detalle en ventas nuevas;
- crear migraciones para reactivar pago por producto;
- modificar Seguridad para exponer este flujo como operacion canonica;
- mezclar en un mismo lote deuda legacy de producto con reglas globales de venta.

Si una fase futura necesita limpiar o retirar este flujo, debe hacerlo como micro-lote separado, con inventario de usos reales, rutas, permisos, vistas, servicios, tests y datos historicos.

## G. Tests de caracterizacion legacy

Los siguientes tests deben tratarse como caracterizacion legacy o administrativa, no como especificacion canonica de Nueva Venta:

- `TheBuryProyect.Tests/Unit/ProductoCondicionPagoRulesTests.cs`
- `TheBuryProyect.Tests/Unit/ProductoCondicionPagoPlanReadinessTests.cs`
- `TheBuryProyect.Tests/Integration/ProductoCondicionPagoEfTests.cs`

Nota posterior Fase 7.10: los tests del modal/admin y del service admin retirado dejaron de ser contrato vigente. La caracterizacion que permanezca debe limitarse a reglas/resolver usados por `CreditoPersonal`.

Los tests de venta, caja, reportes y configuracion global deben ser la proteccion principal del camino canonico `1 venta = 1 TipoPago principal`.

## H. Deuda pendiente

- Inventariar datos/modelo legacy remanente de `ProductoCondicionPago*` sin tocar la restriccion vigente de `CreditoPersonal`.
- Separar con nombres, documentacion o estructura los tests legacy/remanentes de los tests canonicos de venta.
- Revisar docs historicos de fases 8, 14, 15 y 16 si se prepara documentacion de release para usuarios finales.
- Confirmar una estrategia de retiro o aislamiento para columnas/datos legacy sin eliminar migraciones ni datos historicos.
- Mantener documentada la diferencia entre comision individual por producto y pago por producto.

## I. Proximos frentes recomendados

1. Documentar en el indice/handoff de pagos que `docs/fase-5.1-diseno-configuracion-global-pagos-venta.md` y este handoff son las fuentes vigentes para Nueva Venta.
2. Agrupar o etiquetar tests legacy de `ProductoCondicionPago*` para evitar que se lean como contrato funcional nuevo.
3. Revisar permisos/navegacion del modulo administrativo sin cambiar comportamiento productivo.
4. Agregar, si hace falta, una nota corta en docs historicos 8/14/15/16 indicando que son antecedentes legacy y no contrato canonico vigente.
5. Seguir fortaleciendo tests del camino global: Venta, Caja, Reportes, DatosTarjeta, MercadoPago y comision por producto.

## J. Checklist actualizado

- [x] Decision `1 venta = 1 TipoPago principal` reafirmada.
- [x] Configuracion global de pagos confirmada como camino canonico de Nueva Venta.
- [x] Comision individual por producto diferenciada de pago por producto.
- [x] `ProductoCondicionPago*` documentado como legacy administrativo/no canonico.
- [x] Tests de modal y reglas por producto clasificados como caracterizacion legacy.
- [x] Restricciones de no reactivacion documentadas.
- [x] Deuda y proximos frentes recomendados documentados.
- [x] Fase 7.8: entrada visible desde Catalogo a condiciones por producto ocultada sin borrar superficie legacy.
- [x] Fase 7.10: modal/admin legacy, endpoints admin, service admin y JS asociado retirados.
- [x] Piezas remanentes limitadas a `CreditoPersonal` documentadas.
- [ ] Inventario operativo de datos/modelo legacy remanente.
