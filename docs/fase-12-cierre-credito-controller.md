# Fase 12.8 - Cierre documental de CreditoController

Agente Kira

Fecha: 2026-05-07

## A. Diagnostico final de Fase 12

Fase 12 queda cerrada desde el frente `CreditoController` como refactor de bajo riesgo funcional. El controller conserva la responsabilidad HTTP/MVC, validacion de flujo, redirecciones, `TempData`, `ViewData["ReturnUrl"]` y seleccion de vistas, mientras que reglas repetidas o calculos derivados quedaron encapsulados en servicios especificos.

El diagnostico inicial de 12.1 identifico un controller con alta concentracion de responsabilidades:

- consulta y agrupacion de creditos para `Index`;
- configuracion de credito asociado a venta;
- resolucion de rangos de cuotas globales, por cliente, perfil y producto;
- simulacion JSON del plan de venta;
- armado de datos auxiliares para UI de pago de cuotas;
- uso mixto de `ViewBag` y modelos tipados.

El cierre confirma que las extracciones de 12.3 a 12.7 redujeron complejidad local sin cambiar el comportamiento productivo esperado ni ampliar el alcance a DB, UI, JS, Caja, Venta, reportes o comprobantes.

## B. Documentacion creada o actualizada

- Creado `docs/fase-12-cierre-credito-controller.md`.
- No se actualizo documentacion ajena al cierre de Fase 12.
- No se modifico codigo productivo durante 12.8.
- No se modifico DB, no se crearon migraciones y no se ejecuto `database update`.

## C. Servicios extraidos y responsabilidades

- `CreditoRangoProductoService`: resuelve el rango efectivo de cuotas para credito personal segun condiciones de pago por producto. Conserva minimo/base, aplica maximo efectivo, detecta producto restrictivo y devuelve error si el medio queda bloqueado o el rango resultante es invalido.
- `CreditoConfiguracionVentaService`: resuelve el POST de `ConfigurarVenta`. Valida metodo de calculo, fuente de configuracion, tasa global/manual/cliente, rango de cuotas y restriccion por producto; produce `ConfiguracionCreditoComando` con snapshots preservados.
- `CreditoSimulacionVentaService`: concentra la simulacion de plan de venta para `SimularPlanVenta`. Normaliza opcionales, valida montos/cuotas/tasa/gastos, aplica fallback de fecha y conserva semaforo financiero cuando existe `IClienteAptitudService`.
- `CreditoUiQueryService`: concentra helpers de UI/consulta para cuotas pendientes, `SelectListItem`, JSON de cuotas y agrupacion de creditos por cliente para `Index`.

Los servicios quedaron registrados en DI en `Program.cs`:

- `ICreditoRangoProductoService`
- `ICreditoConfiguracionVentaService`
- `ICreditoSimulacionVentaService`
- `ICreditoUiQueryService`

## D. Contratos preservados

Contratos MVC preservados:

- `GET Credito/Index` sigue renderizando `Index_tw`.
- `GET/POST Credito/ConfigurarVenta` sigue renderizando `ConfigurarVenta_tw` en validaciones y redirige a `ContratoVentaCredito/Preparar` cuando corresponde a venta configurada.
- `GET Credito/PagarCuota` y `GET Credito/AdelantarCuota` siguen renderizando `PagarCuota_tw`.
- `Details`, `Create`, `Edit`, `Delete`, `CuotasVencidas`, aprobacion, rechazo y cancelacion mantienen flujo MVC existente.

Contrato JSON preservado en `SimularPlanVenta`:

- parametros AJAX conservados: `totalVenta`, `anticipo`, `cuotas`, `gastosAdministrativos`, `fechaPrimeraCuota`, `tasaMensual`;
- respuesta OK conserva nombres consumidos por JS: `montoFinanciado`, `cuotaEstimada`, `tasaAplicada`, `interesTotal`, `totalAPagar`, `gastosAdministrativos`, `totalPlan`, `fechaPrimerPago`, `semaforoEstado`, `semaforoMensaje`, `mostrarMsgIngreso`, `mostrarMsgAntiguedad`;
- errores de validacion conservan `BadRequest` con objeto `{ error }`;
- errores inesperados conservan `500` con objeto `{ error = "Ocurrio un error al calcular el plan de credito." }`.

Contrato JSON preservado en `RegistrarPagoMultiple`:

- solicitud invalida: `BadRequest(new { success = false, errors = [...] })`;
- validacion de modelo invalida: `BadRequest(new { success = false, errors = errores })`;
- exito: `Ok(new { success = true, data = resultado })`;
- error controlado: `BadRequest(new { success = false, errors = [...] })`;
- error inesperado: `500` con `success = false` y `errors`.

Contratos de seguridad y navegacion preservados:

- `[Authorize]` y `[PermisoRequerido(Modulo = "creditos", Accion = "view")]` siguen aplicando al controller.
- `returnUrl` sigue pasando por `Url.GetSafeReturnUrl`.
- `TempData` y redirecciones existentes se mantienen.

## E. ViewBag/ViewModel: migrado y pendiente

Migrado a ViewModel tipado:

- `Index_tw` consume `CreditoIndexViewModel` con `Filter` y `Clientes`.
- `ConfigurarVenta_tw` consume `ConfiguracionCreditoVentaViewModel` para datos de configuracion, rango efectivo, restriccion por producto, cliente personalizado y perfiles activos.
- `PagarCuota_tw` consume `PagarCuotaViewModel.Cuotas` y `PagarCuotaViewModel.CuotasJson` en lugar de depender de helpers sueltos del controller.

Preservado por riesgo:

- `ViewBag.ContratoVentaCredito` en `Details_tw`, para no ampliar el cambio sobre la vista de detalle y contrato.
- `CreditoViewBagBuilder` y `CargarViewBags` en Create/Edit, porque ese frente no era parte de Fase 12.8 y migrarlo implicaria tocar formularios no cubiertos por este cierre.
- `ViewData["ReturnUrl"]`, por ser contrato transversal de navegacion y retorno seguro en vistas existentes.

Pendiente recomendado:

- Migrar `Details_tw` a modelo completamente tipado para contrato de venta credito.
- Evaluar migracion gradual de Create/Edit a ViewModels especificos sin `ViewBag`, con tests de contrato de UI antes de tocar vistas.

## F. Tests agregados o ajustados

Caracterizacion y contratos de controller/UI:

- `CreditoControllerConfigurarVentaTests`: cubre GET/POST de `ConfigurarVenta`, configuracion cliente/perfil tipada, tasa global ausente, metodo ausente, cliente sin configuracion personalizada, tasa manual invalida, rango base, rango por producto, producto bloqueante y snapshots del comando.
- `CreditoControllerSimularPlanVentaTests`: cubre contrato JSON, semaforo, tasa global/request, fallback de fecha, errores por tasa ausente y validaciones negativas.
- `ConfigurarVentaUiContractTests`: protege alerta de restriccion por producto, nombres/fallback de producto restrictivo, rango efectivo, atributos para automatizacion, parametros AJAX y nombres JSON consumidos por JS, ademas de lectura tipada de cliente/perfiles.

Tests de servicios extraidos:

- `CreditoRangoProductoServiceTests`: cubre venta nula/sin productos, limite de cuotas por producto, bloqueo del medio, descripcion de producto restrictivo y rango invalido.
- `CreditoConfiguracionVentaServiceTests`: cubre resolucion de fuente/metodo/tasa, validaciones, rango efectivo, error por producto y comando final.
- `CreditoSimulacionVentaServiceTests`: cubre simulacion valida, validaciones de entrada, tasa global/request, semaforo y fallback de fecha.
- `CreditoUiQueryServiceTests`: cubre cuotas pendientes, proyeccion a select, JSON de cuotas y agrupacion/estado consolidado de creditos por cliente.

## G. Validacion ejecutada y resultado

Validacion previa informada al inicio de 12.8:

- `dotnet build`: OK.
- `dotnet test --filter "CreditoController|CreditoUi|ConfigurarVenta|Credito"`: 406 passed.
- `dotnet test --no-build --filter "CreditoControllerConfigurarVentaTests|CreditoControllerSimularPlanVentaTests|CreditoUiQueryServiceTests|ConfigurarVentaUiContractTests"`: 57 passed.
- `dotnet test --no-build`: 2292 passed.
- `git diff --check`: OK, solo warnings LF/CRLF.

Validacion obligatoria de cierre 12.8:

- `dotnet build`: OK, 0 warnings, 0 errores.
- `dotnet test --filter "CreditoController|CreditoUi|ConfigurarVenta|SimularPlanVenta|Credito"`: OK, 406/406.
- `dotnet test --no-build`: OK, 2292/2292.
- `git diff --check`: OK, sin errores; solo warnings LF/CRLF del working tree.

No se ejecutaron migraciones y no se ejecuto `database update`.

## H. Comportamiento productivo conservado

Se conserva el comportamiento observable de credito:

- mismas rutas MVC y vistas principales;
- mismos nombres JSON consumidos por JS;
- mismos errores HTTP esperados para simulacion;
- mismas redirecciones luego de configurar credito;
- misma proteccion de `returnUrl`;
- mismo flujo de contrato cuando `VentaId` existe;
- misma configuracion de credito enviada a `ICreditoService.ConfigurarCreditoAsync`;
- misma semantica de pago multiple;
- misma agrupacion funcional de creditos por cliente, ahora aislada en servicio;
- mismo armado de cuotas pendientes para pago/adelanto, ahora aislado en servicio.

## I. Riesgos/deuda tecnica pendiente

- `CreditoController` todavia concentra muchos flujos: CRUD, detalle, aprobacion/rechazo/cancelacion, pago, adelanto, cuotas vencidas y configuracion de venta.
- `Details_tw` conserva dependencia de `ViewBag.ContratoVentaCredito`.
- Create/Edit siguen dependiendo de `CreditoViewBagBuilder`.
- Hay textos con problemas historicos de encoding en partes del codigo existente; Fase 12 no los corrige para evitar ruido y cambios funcionales indirectos.
- Las migraciones existentes en el repositorio no fueron tocadas por 12.8; cualquier release debe validar estado DB fuera de esta fase documental.
- `CreditoConfiguracionVentaService` depende de contratos de configuracion de pago y condiciones por producto; futuras fases deben cubrir combinaciones con datos reales antes de ampliar reglas.
- El working tree contiene cambios amplios previos de Fase 12 y otros frentes; conviene separar commits/PR por alcance antes de staging.

## J. Checklist actualizado

- [x] Fase 8 - Condiciones de pago por producto: cerrada.
- [x] Fase 9 - Credito personal por producto: cerrada.
- [x] Fase 10 - Refactor ProductoController: cerrada.
- [x] Fase 11 - Limpieza/release readiness: limpieza QA pusheada.
- [ ] Fase 11 - Staging: pendiente.
- [x] Fase 12.1 - Auditoria CreditoController: cerrada.
- [x] Fase 12.2 - Tests caracterizacion ConfigurarVenta/SimularPlanVenta: cerrada.
- [x] Fase 12.3 - Extraer rango efectivo por producto: cerrada.
- [x] Fase 12.4 - Extraer resolucion configuracion POST: cerrada.
- [x] Fase 12.5 - Extraer simulacion de venta: cerrada.
- [x] Fase 12.6 - Limpiar helpers UI cuotas/index: cerrada.
- [x] Fase 12.7 - Reducir ViewBag a ViewModel tipado: cerrada.
- [x] Fase 12.8 - Cierre documental Fase 12: documentado.
- [ ] Staging / release final: pendiente.

Checklist final del frente credito:

- [x] Auditoria documentada.
- [x] Tests de caracterizacion agregados.
- [x] Servicios extraidos con interfaces.
- [x] DI actualizado.
- [x] Contratos HTTP/JSON protegidos por tests.
- [x] ViewModels tipados incorporados en `Index`, `ConfigurarVenta` y pago de cuota.
- [x] Deuda tecnica restante documentada.
- [x] Validacion obligatoria 12.8 ejecutada en esta misma fase.
- [ ] Separar/ordenar commits antes de staging.

## K. Siguiente frente recomendado

Antes de iniciar nuevos refactors, el siguiente frente recomendado es staging/release final:

- ordenar el working tree por commits o PRs de alcance claro;
- ejecutar smoke funcional autenticado de credito asociado a venta;
- verificar contrato de credito generado desde venta hasta preparacion de contrato;
- confirmar estado de migraciones en ambiente destino sin ejecutar cambios no planificados;
- revisar line endings si el release exige diff limpio sin warnings.

Despues de staging, el proximo refactor tecnico recomendable es `Details_tw`/detalle de credito hacia ViewModel completamente tipado, porque reduce dependencia de `ViewBag` sin tocar calculos ni DB.

## L. Prompt recomendado para proxima fase

```text
Agente Kira - Fase 13

Objetivo: preparar staging/release final del frente credito posterior a Fase 12.

Restricciones:
- No modificar DB sin aprobacion explicita.
- No crear migraciones.
- No ejecutar database update.
- No tocar CajaService, VentaService, reportes ni comprobantes salvo validacion.
- No iniciar nuevos refactors.

Tareas:
- Revisar working tree y separar alcance de Fase 12.
- Ejecutar dotnet build.
- Ejecutar dotnet test --filter "CreditoController|CreditoUi|ConfigurarVenta|SimularPlanVenta|Credito".
- Ejecutar dotnet test --no-build.
- Ejecutar git diff --check.
- Hacer smoke funcional autenticado de credito asociado a venta, configuracion, simulacion y preparacion de contrato.
- Documentar evidencia y riesgos de release.

Formato de respuesta:
A. estado de staging
B. validaciones ejecutadas
C. smoke funcional
D. riesgos bloqueantes
E. cambios listos para commit/PR
F. recomendacion de release
```
