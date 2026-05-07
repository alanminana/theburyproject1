# Fase 9.1 - Diseno tecnico de restricciones de credito personal por producto

Agente Kira

## A. Diagnostico del flujo actual de credito personal

La venta con `TipoPago.CreditoPersonal` hoy esta separada en dos etapas principales:

- En `VentaService.CreateAsync`, la venta se calcula con las reglas generales de venta, se valida con `IValidacionVentaService.ValidarVentaCreditoPersonalAsync`, se capturan snapshots de cupo/riesgo en `Venta`, y se deja la operacion en `PendienteFinanciacion` cuando corresponde. Si no hay `CreditoId`, se crea un `Credito` pendiente de configuracion con monto igual al total de la venta, tasa `0` y cuotas `0`.
- En `CreditoController.ConfigurarVenta`, el operador configura el plan. Ahi se resuelve tasa, gastos y rango de cuotas desde `ConfiguracionPagoService`, con prioridad funcional existente: cliente/perfil/global/manual segun el metodo elegido. El POST valida `CantidadCuotas` contra `ResolverRangoCuotasAsync` y luego llama a `CreditoService.ConfigurarCreditoAsync`.
- En `VentaService.ConfirmarVentaCreditoAsync`, se exige caja abierta, venta de credito personal, credito asociado, cupo disponible, credito configurado, tasa mayor a cero, stock, autorizacion y contrato generado. Recien despues genera cuotas con `GenerarCuotasCreditoAsync` usando `credito.MontoAprobado`, `credito.TasaInteres` y `credito.CantidadCuotas`.
- En `VentaService.ConfirmarVentaAsync`, el camino general tambien revalida credito personal, contrato y condiciones de pago antes de confirmar.

La arquitectura de Fase 8 ya incorporo `ProductoCondicionPago.MaxCuotasCredito`, `ProductoCondicionPagoRules.ResolverCondicionesCarrito`, `CondicionesPagoCarritoResolver` y diagnostico UI. Para credito personal, hoy eso funciona como bloqueo del medio y maximo informativo; no participa todavia del rango real del plan configurado.

## B. Reglas existentes que no deben duplicarse

No se deben reimplementar en condiciones por producto estas reglas ya existentes:

- Aptitud, documentacion, autorizacion y viabilidad de credito personal: `IValidacionVentaService.ValidarVentaCreditoPersonalAsync` y `ValidarConfirmacionVentaAsync`.
- Cupo disponible por cliente/puntaje/excepcion: `CreditoDisponibleService` y validaciones en `VentaService`.
- Snapshots de riesgo/cupo en `Venta`: `LimiteAplicado`, `PuntajeAlMomento`, `PresetIdAlMomento`, `OverrideAlMomento`, `ExcepcionAlMomento`.
- Resolucion de tasa, gastos, perfil, cliente, global y manual: `ConfiguracionPagoService.ObtenerParametrosCreditoClienteAsync`, `ResolverRangoCuotasAsync` y `CreditoConfiguracionHelper`.
- Persistencia final del plan y snapshots del credito configurado: `CreditoService.ConfigurarCreditoAsync`, incluyendo `MetodoCalculoAplicado`, `FuenteConfiguracionAplicada`, `PerfilCreditoAplicadoId`, `TasaInteresAplicada`, `CuotasMinimasPermitidas` y `CuotasMaximasPermitidas`.
- Calculo financiero de cuotas, CFTEA, interes y total del plan: `IFinancialCalculationService`, `CreditoService` y `VentaService.GenerarCuotasCreditoAsync`.
- Caja, reportes, comprobantes y contratos. Las condiciones por producto no deben generar ingresos, facturas, comprobantes ni contratos.

## C. Restricciones por producto recomendadas

Primera version recomendada:

- `Permitido`: permitir/bloquear `CreditoPersonal` por producto, igual que otros medios.
- `MaxCuotasCredito`: limitar la cantidad maxima de cuotas permitidas para credito personal.
- `MinCuotasCredito`: agregar solo si el negocio confirma que algunos productos exigen un piso de financiacion. Tecnica y funcionalmente es viable, pero hoy no existe en entidad/DTO/resolver y requeriria cambio de contrato y migracion en una fase posterior.

Quedan fuera de la primera implementacion:

- Tasa propia por producto.
- Perfil de credito por producto.
- Gastos administrativos por producto.
- Recargos/descuentos reales por producto.
- Cambios de monto financiado, anticipo, CFTEA, cuotas o total por condicion de producto.

## D. Reglas de prioridad global/producto/carrito

La restriccion por producto debe operar como una capa de elegibilidad, no como motor financiero.

Prioridad propuesta:

1. Se resuelve primero el rango base de credito personal con las reglas existentes: metodo manual, cliente, perfil o global.
2. Se resuelven condiciones de carrito para `TipoPago.CreditoPersonal`.
3. Si algun producto tiene `Permitido = false`, el medio queda bloqueado para todo el carrito.
4. Si no hay bloqueo, el maximo efectivo es `min(maximoBaseCredito, min(MaxCuotasCredito por producto))`.
5. Si existe `MinCuotasCredito`, el minimo efectivo seria `max(minimoBaseCredito, max(MinCuotasCredito por producto))`.
6. Si `minimoEfectivo > maximoEfectivo`, el carrito queda incompatible para credito personal y debe bloquear la configuracion/confirmacion con mensaje explicito.

Para carrito multiproducto:

- Maximos: gana el minimo mas restrictivo.
- Minimos: gana el maximo mas restrictivo.
- Rango invalido: bloqueo del medio para ese carrito, no ajuste automatico silencioso.
- Productos sin condicion no restringen.
- Productos bloqueados no deben aportar maximos visibles; Fase 8 ya caracteriza ese comportamiento para `MaxCuotasCredito`.

Cuenta corriente debe quedar fuera. Aunque comparte la idea de deuda del cliente, no es credito personal financiado en cuotas y no usa `CreditoService`, perfiles, tasa ni contrato de venta de credito.

## E. Impacto tecnico esperado

`ProductoCondicionPagoRules`:

- Ya resuelve `MaxCuotasCredito` como minimo restrictivo.
- Deberia extenderse con `MinCuotasCredito` solo en una fase con cambio de modelo.
- Deberia exponer resultado de rango efectivo/incompatibilidad si se incorpora minimo.

`CondicionesPagoCarritoResultado`:

- Hoy alcanza para bloqueo y maximo (`MaxCuotasCredito`).
- Para rango completo deberia sumar `MinCuotasCredito`, `RangoCuotasCreditoValido` o un bloqueo tipado por rango invalido.

`CreditoController.ConfigurarVenta`:

- Es el punto natural para aplicar la restriccion al configurar el plan, porque ahi existe `CantidadCuotas` y se resuelve el rango base.
- Debe consultar el carrito de la venta cuando `VentaId` exista y cruzar el rango base con condiciones por producto.
- No debe resolver tasas, perfiles ni calculos financieros desde producto.

`VentaService`:

- Debe conservar la validacion backend de Fase 8 en `CreateAsync` y `ConfirmarVentaAsync`.
- Para credito personal, la validacion relevante de cuotas debe estar en configuracion del credito y confirmacion de credito, no en tarjeta.
- `ConfirmarVentaCreditoAsync` deberia revalidar que `credito.CantidadCuotas` sigue dentro del rango efectivo del carrito antes de generar cuotas.

UI:

- `/Venta/Create` puede seguir mostrando diagnostico informativo y bloqueo del medio.
- `/Credito/ConfigurarVenta` deberia mostrar el rango efectivo resultante cuando la venta viene de un carrito.
- El input de cuotas deberia limitarse por el maximo efectivo y, si existe minimo, por el minimo efectivo.

## F. Cambios propuestos en contratos/modelo si aplican

Sin `MinCuotasCredito`, primera version puede reutilizar:

- `ProductoCondicionPago.MaxCuotasCredito`.
- `ProductoCondicionPagoDto.MaxCuotasCredito`.
- `GuardarProductoCondicionPagoItem.MaxCuotasCredito`.
- `CondicionesPagoCarritoResultado.MaxCuotasCredito`.

Para una version con minimo se requeriria:

- `ProductoCondicionPago.MinCuotasCredito`.
- `ProductoCondicionPagoDto.MinCuotasCredito`.
- `GuardarProductoCondicionPagoItem.MinCuotasCredito`.
- `TipoRestriccionCuotas.MinCuotasCredito`.
- `CondicionesPagoCarritoResultado.MinCuotasCredito`.
- Validaciones puras para valores menores a 1 y rango producto invalido cuando `MinCuotasCredito > MaxCuotasCredito`.
- Migracion EF en fase de implementacion, no en esta fase de diseno.

Para auditoria de snapshot, no es obligatorio agregar columnas al `Credito` en primera version si se reutilizan `CuotasMinimasPermitidas` y `CuotasMaximasPermitidas` como rango efectivo final. Si se necesita trazabilidad fina, una fase posterior podria agregar campos de origen/productos restrictivos, pero no es necesario para bloquear correctamente.

## G. Riesgos funcionales

- Bloquear credito personal en `/Venta/Create` sin revalidar en configuracion permitiria bypass si cambia el carrito o la condicion.
- Limitar solo UI sin backend permitiria configurar cuotas por encima del producto.
- Mezclar tasa/perfil por producto duplicaria reglas existentes y abriria conflictos con cliente/perfil/global/manual.
- Aplicar recargos/descuentos reales alteraria totales, caja, comprobantes y reportes, fuera del alcance.
- Tratar cuenta corriente como credito personal podria afectar caja y deuda sin contrato/cuotas.
- Reusar `CuotasMinimasPermitidas` y `CuotasMaximasPermitidas` como snapshot efectivo es simple, pero puede ocultar si el limite vino de producto o de cliente/perfil/global.

## H. Tests necesarios antes de implementar

Caracterizacion previa recomendada:

- `ProductoCondicionPagoRules`: `MaxCuotasCredito` usa minimo restrictivo y no participa si el producto esta bloqueado.
- `CondicionesPagoCarritoResolver`: lee `MaxCuotasCredito` desde DB para `TipoPago.CreditoPersonal`.
- `CreditoConfiguracionHelper`: mantiene rangos actuales para Manual, Perfil, Cliente y Global.
- `CreditoController.ConfigurarVenta` POST: rechaza cuotas fuera del rango base actual.
- `CreditoService.ConfigurarCreditoAsync`: persiste cantidad, tasa, fuente, metodo y snapshots de rango.
- `VentaServiceConfirmarCredito`: genera cuotas segun `credito.CantidadCuotas` y monto aprobado, sin recalcular desde producto.
- `VentaService`: credito personal no registra movimiento de caja inmediato salvo anticipo ya existente.

Tests de implementacion posterior:

- Producto bloquea `CreditoPersonal`: venta/configuracion no permite continuar.
- Dos productos con `MaxCuotasCredito` 18 y 12: maximo efectivo 12.
- Rango base global 24 y producto 12: POST con 18 cuotas falla, POST con 12 pasa.
- Rango base cliente/perfil 6 y producto 12: maximo efectivo queda 6.
- Si se agrega `MinCuotasCredito`: producto minimo 10 y perfil maximo 6 bloquea por rango invalido.
- Confirmacion revalida condiciones si cambiaron despues de configurar credito.
- Cuenta corriente no usa reglas de credito personal.

## I. Fases recomendadas para credito personal por producto

Fase 9.2 - Caracterizacion:

- Agregar tests que congelen comportamiento actual de credito personal, configuracion de cuotas y `MaxCuotasCredito` informativo.
- Sin cambios productivos.

Fase 9.3 - Bloqueo y maximo de cuotas:

- Reutilizar `MaxCuotasCredito` existente.
- Aplicar bloqueo y maximo efectivo en `ConfigurarVenta` y `ConfirmarVentaCreditoAsync`.
- UI de configuracion muestra rango efectivo.
- Sin tasa/perfil por producto.

Fase 9.4 - Minimo de cuotas, si negocio lo confirma:

- Agregar `MinCuotasCredito` con migracion propia.
- Resolver rango multiproducto con `max(minimos)` y `min(maximos)`.
- Bloquear rango invalido.

Fase 9.5 - Auditoria ampliada opcional:

- Guardar snapshot/origen de restricciones de producto si se necesita explicar historicamente por que el rango fue reducido.

Fase 9.6 - Alcances avanzados, solo con definicion funcional:

- Evaluar tasa/perfil por producto como feature separada. Recomendacion actual: no implementarlo.

## J. Dudas funcionales bloqueantes

- Confirmar si primera version queda limitada a `Permitido` y `MaxCuotasCredito`.
- Confirmar si `MinCuotasCredito` existe como necesidad real o puede quedar fuera.
- Confirmar el texto operativo cuando un carrito queda incompatible por rango.
- Confirmar si el snapshot de rango efectivo alcanza con `Credito.CuotasMinimasPermitidas` y `CuotasMaximasPermitidas`.
- Confirmar que cuenta corriente queda separada de credito personal.

## K. Checklist actualizado

- [x] Revisar cierre Fase 8.
- [x] Auditar `CreditoService`, `CreditoController`, `VentaService` y entidades relacionadas.
- [x] Identificar reglas existentes que no deben duplicarse.
- [x] Definir alcance seguro de primera version: bloqueo y maximo de cuotas.
- [x] Separar restricciones de producto de calculo financiero.
- [x] Definir regla multiproducto: minimo de maximos, maximo de minimos y bloqueo si el rango queda invalido.
- [x] Dejar tasa propia, perfil por producto y recargos/descuentos reales fuera de primera implementacion.
- [x] Definir impacto esperado en contratos, resolver, controller, service y UI.
- [x] Definir tests de caracterizacion.
- [x] Proponer fases seguras.
- [x] No modificar logica productiva.
- [x] No tocar DB ni crear migraciones.
- [x] No modificar `CajaService`, reportes ni comprobantes.

## L. Prompt recomendado para la siguiente fase

```text
Agente Kira - Fase 9.2

Trabajar solo en tests de caracterizacion para credito personal por producto.
No modificar logica productiva, no tocar DB, no crear migraciones, no modificar CajaService, reportes ni comprobantes.

Objetivo:
Congelar el comportamiento actual antes de implementar restricciones efectivas de credito personal por producto.

Tareas:
- Agregar tests para ProductoCondicionPagoRules con CreditoPersonal y MaxCuotasCredito.
- Agregar tests de CondicionesPagoCarritoResolver leyendo MaxCuotasCredito desde DB.
- Agregar/validar tests de CreditoConfiguracionHelper para rangos base por metodo.
- Agregar tests de ConfigurarVenta POST que documenten rechazo por rango base actual.
- Agregar tests de VentaServiceConfirmarCredito que documenten que las cuotas se generan desde Credito.CantidadCuotas y MontoAprobado.
- Confirmar que CreditoPersonal no registra caja inmediata salvo anticipo existente.

Validacion:
- dotnet build
- dotnet test --filter "ProductoCondicionPagoRules|CondicionesPagoCarritoResolver|CreditoConfiguracion|ConfirmarVentaCredito"

Cierre:
Indicar que la fase fue solo caracterizacion y que no hubo cambios productivos.
```

