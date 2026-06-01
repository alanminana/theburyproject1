# Fase Kira - Ventas/Create tipo de pago UX

## A. Diagnostico

En `Views/Venta/Create_tw.cshtml`, el tipo de pago principal ya estaba visible en Datos generales con `id="select-tipo-pago"` y participaba del flujo canonico de `wwwroot/js/venta-create.js`.

En `Views/Venta/_VentaCrearModal.cshtml`, el mismo selector existia con `name="TipoPago"` e `id="select-tipo-pago"`, pero estaba dentro de un `div.hidden`. El modal indicaba que el tipo de pago se configuraba desde cada producto, aunque el formulario seguia posteando un tipo de pago principal.

## B. Diferencia entre vista completa y modal

- Vista completa: selector principal visible, texto orientado al tipo de pago principal y detalle de cobro dinamico.
- Modal: selector principal oculto, texto que enviaba al usuario al pago por producto y detalle de cobro sin control visible asociado.

## C. Problema UX

El vendedor no ve donde definir el tipo de pago de la venta desde el modal. La UI mezcla dos ideas: pago principal de la venta y ajuste especifico por producto. Eso hace parecer que faltan campos de cobro o que el pago solo puede configurarse en un sub-modal.

## D. Decision tomada

Se eligio mostrar el selector principal existente en el modal. Se mantuvieron `name="TipoPago"` e `id="select-tipo-pago"` para conservar el POST y el contrato con `venta-create.js`.

El sub-modal `select-tipo-pago-item` se conserva como ajuste especifico por producto, no como camino obligatorio.

## E. Cambios aplicados

- `_VentaCrearModal.cshtml`: el selector `select-tipo-pago` paso de oculto a visible.
- `_VentaCrearModal.cshtml`: el bloque de Datos generales ahora menciona cliente, fecha y tipo de pago principal.
- `_VentaCrearModal.cshtml`: Detalle de cobro aclara que los campos se adaptan al tipo de pago principal elegido.
- `_VentaCrearModal.cshtml`: el sub-modal de producto aclara que su configuracion es especifica del producto y que, si no se cambia, usa el tipo de pago principal de la venta.

## F. Tests agregados/ajustados

Se agregaron contratos UI en `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` para verificar:

- el modal muestra `Tipo de pago principal`;
- `select-tipo-pago` no queda dentro de un `div.hidden` inmediato;
- el modal ya no dice que el pago solo se configura desde cada producto;
- el modal aclara pago principal y ajuste por producto;
- la vista completa conserva el selector visible;
- `venta-create.js` sigue usando `select-tipo-pago` y el listener `change`.

## G. Validaciones

Validaciones base antes del cambio:

- `dotnet build --configuration Release`: OK.
- `dotnet test --filter "VentaCreateUiContractTests"`: OK.
- `dotnet test --filter "Venta|VentaApiController|VentaController|ConfiguracionPago|ProductoUnidad|Conciliacion"`: OK.

Validaciones post-cambio:

- `dotnet test --filter "VentaCreateUiContractTests"`: OK, 54/54.
- `dotnet build --configuration Release`: OK.
- `dotnet test --filter "Venta|VentaApiController|VentaController|ConfiguracionPago|ProductoUnidad|Conciliacion"`: OK, 1016/1016.
- `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"`: OK, 1/1.
- `git diff --check`: OK.

Prueba manual:

- La app levanto en `http://localhost:5187`.
- Login local con usuario de desarrollo OK.
- La pantalla de Ventas cargo OK.
- Limitacion: el estado local no renderizo `#modal-crear-venta` en DOM porque el boton disponible correspondia al flujo bloqueado/alternativo de Nueva Venta. No se abrio ni modifico Caja para forzar el flujo.

## H. Que NO se toco

- `VentaService`.
- Backend de reglas de venta.
- Caja.
- Cotizacion.
- ProductoUnidad.
- MovimientoStock.
- Migraciones.
- Logica JavaScript de calculos, carrito, stock breakdown o pagos globales.

## I. Riesgos/deuda

- El sub-modal de tipo de pago por producto sigue existiendo en Razor, pero los tests actuales marcan que Nueva Venta no debe activar ese flujo desde `venta-create.js`.
- La configuracion de pagos globales sigue dependiendo del selector principal y del fallback Razor. Este cambio no altera esa autoridad.
- Queda deuda de lenguaje historico alrededor de "tipo predeterminado del sistema" en flujos legacy, reemplazada en el sub-modal tocado por "tipo de pago principal de la venta".
