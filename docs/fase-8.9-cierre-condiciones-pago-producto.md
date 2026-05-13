# Fase 8.9 - Cierre funcional de condiciones de pago por producto

Agente Kira

> Nota Fase 7.7D: documento historico. El alcance de condiciones por producto quedo como legacy administrativo/no canonico para Nueva Venta. El flujo vigente de Nueva Venta usa configuracion global de pagos y conserva **1 venta = 1 TipoPago principal**. Para `CreditoPersonal`, las referencias a restricciones por producto se interpretan solo como elegibilidad/rango (`Permitido` y `MaxCuotasCredito`), no como pago por producto, recargo, descuento o plan por item.

> Nota Fase 7.10: el admin/modal legacy de condiciones por producto, sus endpoints admin, `IProductoCondicionPagoService` / `ProductoCondicionPagoService` y `wwwroot/js/producto-condiciones-pago-modal.js` fueron retirados. Esta fase queda como antecedente historico; las piezas remanentes de `ProductoCondicionPago*` existen solo para la restriccion acotada de `CreditoPersonal`.

## A. Estado de cierre

La funcionalidad de condiciones de pago por producto queda cerrada funcionalmente para venta con alcance de bloqueo de medios, bloqueo de tarjeta especifica, limites de cuotas y mensajes backend controlados.

Ambiente usado para QA funcional:

- URL local: `http://localhost:5187`
- DB: LocalDB `TheBuryProjectDb`
- Usuario autenticado: `admin`
- Entorno: `Development`
- Migracion `AddProductoCondicionesPago`: aplicada previamente
- `dotnet ef database update`: no ejecutado en QA 8.8F ni en cierre 8.9

## B. Fases completadas

- 8.7C: diagnostico UI informativo.
- 8.8A: bloqueo UI por medio incompatible.
- 8.8B: limite UI de cuotas.
- 8.8C: validacion backend en `VentaService`.
- 8.8D: presentacion uniforme de errores.
- 8.8E: excepcion tipada `CondicionesPagoVentaException`.
- 8.8F: QA funcional autenticada.
- 8.9: cierre funcional, documentacion, evidencia y hardening menor de UI.

## C. Alcance implementado

- Configuracion de condiciones por producto y medio de pago.
- Reglas por tarjeta especifica para pagos con tarjeta.
- Diagnostico de carrito en `/Venta/Create`.
- Bloqueo del boton de confirmacion cuando el medio seleccionado queda bloqueado.
- Limite del selector de cuotas cuando existe una restriccion efectiva.
- Validacion backend en creacion y confirmacion de ventas.
- Mensajes controlados para `Create`, `CreateAjax` y `Confirmar`.
- Presentacion informativa de recargos y descuentos configurados por producto.

## D. Alcance explicitamente fuera

- No se aplican recargos reales por condiciones de pago por producto.
- No se aplican descuentos reales por condiciones de pago por producto.
- No se modifican totales por diagnostico de condiciones.
- No se modifican Caja, reportes ni comprobantes.
- No se modifica `CreditoService`.
- Credito personal por producto queda pendiente para una fase especifica.
- No se crean migraciones nuevas en 8.9.

## E. Evidencia QA consolidada

La evidencia visual de QA fue generada localmente durante la fase 8.9 para validar
los escenarios de bloqueo, compatibilidad, cuotas, venta valida y manejo de error
controlado. Las capturas `docs/qa-*.png` se consideran artefactos locales de QA y
no se conservan versionadas en Git.

## F. Resultados QA

| Escenario | Resultado esperado | Resultado obtenido |
| --- | --- | --- |
| Producto bloquea transferencia | Panel bloqueado, boton deshabilitado, mensaje claro | OK |
| Producto bloquea Visa | Visa bloquea venta | OK |
| Misma regla con Master compatible | Master permite continuar | OK |
| Cuotas excedidas | Dropdown limitado a maximo efectivo | OK |
| Bypass de cuotas por `CreateAjax` | Backend rechaza valor invalido | OK |
| Venta valida sin restricciones | Flujo de venta sigue funcionando | OK |
| Error backend controlado en `Create` | Banner con mensaje claro | OK |
| Error backend controlado en `CreateAjax` | JSON controlado con `message` y `errors` | OK |
| Error backend controlado en `Confirmar` | `TempData["Error"]` visible en Details | OK |

## G. Recargos, descuentos y totales

Los recargos y descuentos declarados en condiciones por producto son informativos en esta etapa.

El diagnostico:

- no llama a `actualizarTotalesUI`;
- no escribe `hdnTotal`;
- no registra movimientos de caja;
- no genera comprobantes;
- solo limita cuotas disponibles cuando corresponde.

El total de venta se mantiene definido por el calculo existente de venta y por las reglas previas del sistema.

## H. Credito personal pendiente

Las restricciones de `CreditoPersonal` por producto quedan documentadas como pendientes. En la UI actual no existe selector especifico de cuotas de credito en `/Venta/Create`, por lo que el diagnostico solo puede mostrar maximos informativos para credito personal.

Una fase futura deberia definir:

- contrato de cuotas de credito personal por producto;
- interaccion con perfil de credito, cliente y configuracion global;
- momento exacto de validacion del plan;
- tests contra `CreditoService` sin duplicar reglas.

## I. Hardening menor de UI

Durante QA 8.8F se observo que el nodo `#diagnostico-condiciones-pago-bloqueo` podia conservar texto residual luego de volver a un estado permitido. Visualmente el panel quedaba permitido y el boton se habilitaba, pero el DOM retenia el texto anterior.

En 8.9 se ajusto el comportamiento para que, cuando el diagnostico libera continuidad, el texto del nodo de bloqueo se limpie antes de ocultarlo. Esto evita estado residual para inspeccion DOM y tecnologia asistiva, sin cambiar reglas, calculos, totales ni endpoints.

Smoke Playwright 8.9:

- Estado bloqueado con Visa: mensaje presente, nodo visible, boton deshabilitado, total `2420.00`.
- Estado permitido con Master: mensaje residual vacio, nodo oculto, `aria-describedby` removido, boton habilitado, total `2420.00`.

## J. Checklist de release tecnico

- Confirmar que la migracion `AddProductoCondicionesPago` esta aplicada en el ambiente destino.
- Ejecutar `dotnet build`.
- Ejecutar `dotnet test --filter "CondicionesPago|Venta|VentaController|VentaCreate"`.
- Ejecutar `dotnet test --no-build`.
- Ejecutar `git diff --check`.
- Smoke test autenticado en `/Venta/Create`:
  - producto con medio bloqueado;
  - producto con tarjeta bloqueada y otra compatible;
  - producto con cuotas limitadas;
  - venta valida sin restricciones.
- Confirmar que no hay cambios en totales por recargos/descuentos informativos.
- Confirmar que Caja, reportes y comprobantes no cambian por esta funcionalidad.
- Preparar rollback de despliegue aplicable al release de app y DB.
- Documentar datos QA locales antes de limpiar.

## K. Datos QA locales

No se limpian automaticamente en esta fase.

Datos creados por QA 8.8F:

- Productos con codigo `QA-88F-%`.
- Tarjetas con nombre `QA 8.8F%`.
- Condiciones de pago asociadas a esos productos.
- Venta valida `VTA-202605-000006`.
- Venta de confirmacion controlada `QA88F-CONFIRM`.

Consulta de revision:

```sql
SELECT Id, Codigo, Nombre
FROM Productos
WHERE Codigo LIKE 'QA-88F%';

SELECT Id, NombreTarjeta
FROM ConfiguracionesTarjeta
WHERE NombreTarjeta LIKE 'QA 8.8F%';

SELECT Id, Numero, Estado, TipoPago, Total, Observaciones
FROM Ventas
WHERE Observaciones LIKE 'QA 8.8F%' OR Numero = 'QA88F-CONFIRM';
```

La limpieza debe hacerse manualmente y solo en DB local/preparada, preferentemente por soft delete o restauracion de snapshot local.

## L. Riesgos y deuda pendiente

- Credito personal por producto no esta implementado funcionalmente.
- Los datos QA locales deben gestionarse antes de demos o pruebas de usuarios.
- La documentacion de operacion debe actualizarse si en fases futuras recargos/descuentos dejan de ser informativos.

## M. Recomendacion

Cerrar Fase 8 como funcionalmente validada para venta y planificar una fase separada para credito personal por producto, con alcance propio y tests especificos.
