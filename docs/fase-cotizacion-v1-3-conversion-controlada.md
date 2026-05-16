# Fase Cotización V1.3 — Conversión controlada Cotización → Venta

**Agente:** Carlos — Cotización  
**Rama:** `carlos/cotizacion-v1-contratos`  
**Estado:** Implementación cerrada  
**Fecha:** 2026-05-15  
**Referencia base:** `docs/fase-cotizacion-v1-2-diseno-conversion-venta.md`

---

## A. Objetivo

Implementar la conversión controlada mínima de Cotización → Venta según el diseño V1.2.

Sin efectos irreversibles: no confirma, no descuenta stock, no marca unidades, no registra caja, no genera factura, no crea crédito definitivo.

---

## B. Diagnóstico previo

| Pregunta | Respuesta |
|----------|-----------|
| Campos mínimos Venta | Numero, ClienteId (int, NO nullable), FechaVenta, Estado, TipoPago, detalles |
| Campos mínimos VentaDetalle | ProductoId, Cantidad (int), PrecioUnitario, Subtotal |
| VentaNumberGenerator | Reutilizable. GenerarNumeroAsync(EstadoVenta.Cotizacion) genera prefijo COT- |
| AperturaCajaId | Nullable — no requiere caja abierta para estado Cotizacion |
| ClienteId en Venta | int, NO nullable → BLOQUEANTE si cotización no tiene cliente y no hay ClienteIdOverride |
| Forma de pago | CotizacionMedioPagoTipo → TipoPago: mapeo directo por enum |
| Cuotas/tarjeta | No crear DatosTarjeta. Copiar CantidadCuotas/ValorCuota como texto en Observaciones |
| Crédito personal | NO crear crédito. Datos de cuotas solo informativos en Observaciones |
| Marcar Cotizacion | Cotizacion.Estado = ConvertidaAVenta en la misma transacción |
| Doble conversión | Verificar estado dentro de transacción (recargar cotizacion post-lock) |
| Migración | NO necesaria. Sin CotizacionOrigenId (diferido a V1.4+) |

---

## C. Decisión técnica

- **No llamar a `VentaService.CreateAsync`** — construir entidad Venta directamente para evitar efectos secundarios (caja, crédito, precio vigente forzado).
- **Separar validación de ConvertirAVentaAsync del PreviewConversionAsync** — el preview valida con el estado de la cotización; el convert valida con el request (ClienteIdOverride, UsarPrecioCotizado) para no bloquear casos legítimos.
- **Política de precios:** si `UsarPrecioCotizado = true` y hay cambios de precio → advertencia fuerte (requiere `ConfirmarAdvertencias = true`). Si `UsarPrecioCotizado = false` → usar precio actual, sin advertencia fuerte de precio.
- **Estado destino:** `EstadoVenta.Cotizacion` — editable, no confirmable directamente. El operador debe avanzar a Presupuesto para confirmar.

---

## D. Componentes creados

| Componente | Clasificación | Archivo |
|------------|---------------|---------|
| `ICotizacionConversionService` | Nuevo canónico | `Services/Interfaces/ICotizacionConversionService.cs` |
| `CotizacionConversionService` | Nuevo canónico | `Services/CotizacionConversionService.cs` |
| `CotizacionConversionPreviewResultado` | Nuevo canónico | `Services/Models/CotizacionConversionModels.cs` |
| `CotizacionConversionDetallePreview` | Nuevo canónico | `Services/Models/CotizacionConversionModels.cs` |
| `CotizacionConversionRequest` | Nuevo canónico | `Services/Models/CotizacionConversionModels.cs` |
| `CotizacionConversionResultado` | Nuevo canónico | `Services/Models/CotizacionConversionModels.cs` |

| Componente | Clasificación | Decisión |
|------------|---------------|----------|
| `CotizacionApiController` | Canónico extendido | Agregar endpoints de preview y conversión |
| `VentaService` | Canónico Venta | No tocar — construcción de Venta directa |
| `EstadoVenta.Cotizacion` | Canónico existente | Estado destino |
| `EstadoCotizacion.ConvertidaAVenta` | Canónico existente | Estado marcado en cotización |
| `VentaNumberGenerator` | Canónico auxiliar | Reutilizado |
| `IPrecioVigenteResolver` | Canónico auxiliar | Reutilizado para comparar precios |

---

## E. DTOs

### `CotizacionConversionPreviewResultado`
- `Convertible` — si la cotización puede convertirse
- `Errores` — errores bloqueantes
- `Advertencias` — advertencias informativas
- `ClienteFaltante`, `CotizacionVencida`, `HayCambiosDePrecios`, `HayProductosTrazables`
- `TotalCotizado`, `Detalles` (lista de `CotizacionConversionDetallePreview`)

### `CotizacionConversionDetallePreview`
- `ProductoId`, `CodigoProducto`, `NombreProducto`, `Cantidad`
- `PrecioCotizado`, `PrecioActual`, `PrecioCambio`
- `ProductoActivo`, `RequiereUnidadFisica`, `Advertencias`

### `CotizacionConversionRequest`
- `UsarPrecioCotizado` (default: true) — usar snapshot de cotización o precio actual
- `ConfirmarAdvertencias` (default: false) — confirmar advertencias fuertes
- `ClienteIdOverride` — cliente a usar si cotización no tiene uno
- `ObservacionesAdicionales` — texto adicional en Observaciones de la Venta

### `CotizacionConversionResultado`
- `Exitoso`, `Errores`, `Advertencias`
- `CotizacionId`, `VentaId`, `NumeroVenta`, `EstadoVenta`

---

## F. Preview (`PreviewConversionAsync`)

Valida sin efectos:

1. Cotización existe
2. Estado convertible (Emitida, no Vencida/Cancelada/ConvertidaAVenta/Borrador)
3. Fecha de vencimiento no superada
4. Cliente presente (error bloqueante si falta)
5. Productos activos (error bloqueante si hay inactivos)
6. Comparar precio snapshot vs actual → advertencia si cambiaron
7. Detectar productos con RequiereNumeroSerie → advertencia
8. No modifica DB, no crea Venta

---

## G. Conversión (`ConvertirAVentaAsync`)

1. Validar estado convertible (sin pasar por PreviewConversionAsync)
2. Resolver cliente: `request.ClienteIdOverride ?? cotizacion.ClienteId`
3. Si clienteId null → error bloqueante
4. Verificar productos activos
5. Evaluar advertencias según request:
   - Cambio de precio → advertencia fuerte solo si `UsarPrecioCotizado = true`
6. Si hay advertencias fuertes y `!ConfirmarAdvertencias` → falla
7. Iniciar transacción
8. Recargar cotización dentro de transacción → verificar estado = Emitida
9. Generar número de venta via `VentaNumberGenerator`
10. Crear `Venta` con `EstadoVenta.Cotizacion`
11. Crear `VentaDetalle` por cada `CotizacionDetalle` (precio snapshot o actual según request)
12. Calcular Subtotal/Total desde detalles
13. Marcar `Cotizacion.Estado = ConvertidaAVenta`
14. SaveChanges + Commit
15. Retornar VentaId/NumeroVenta

**No hace:** confirmar venta, descontar stock, marcar ProductoUnidad, registrar caja, generar factura, crear crédito.

---

## H. Endpoints

Agregados en `CotizacionApiController`:

```
POST /api/cotizacion/{id}/conversion/preview
```
- Permiso: `cotizaciones/view` (heredado del controller)
- Devuelve: `CotizacionConversionPreviewResultado` (siempre 200 OK)

```
POST /api/cotizacion/{id}/conversion/convertir
```
- Permiso: `cotizaciones/create`
- Body: `CotizacionConversionRequest`
- Éxito: 200 OK con `CotizacionConversionResultado`
- Error funcional: 400 Bad Request con `{ errores, advertencias }`

---

## I. Qué NO se toca

- `VentaService.cs` — no modificado
- `VentaController.cs` / `VentaApiController.cs` — no modificados
- `Views/Venta/*` — no modificados
- `Views/Cotizacion/*` — no modificados (UI diferida a V1.4)
- `DevolucionService.cs` / `DevolucionController.cs` — no tocados
- `ProductoUnidad`, `MovimientoStock`, `Caja`, `Factura` — no tocados
- Migraciones — no hay migración nueva
- `CotizacionOrigenId` en Venta — diferido a V1.4+

---

## J. Tests agregados

**Integración (CotizacionConversionServiceTests.cs):** 19 tests

| Test | Propósito |
|------|-----------|
| Preview_CotizacionExistente_DevuelveConvertible | Happy path preview |
| Preview_CotizacionConvertida_DevuelveError | Guard doble conversión |
| Preview_CotizacionCancelada_DevuelveError | Guard estado cancelado |
| Preview_CotizacionVencida_BloqueaConversion | Guard estado vencido |
| Preview_CotizacionConFechaVencimientoPasada_BloqueaConversion | Guard fecha vencida |
| Preview_ProductoConPrecioCambiado_AgregaAdvertencia | Detección cambio precio |
| Preview_CreditoPersonalSinCliente_BloqueaConversion | Guard cliente faltante |
| Preview_NoCreaVenta | Efecto nulo del preview |
| Preview_ProductoTrazable_AgregaAdvertencia | Detección producto trazable |
| Convertir_CotizacionValida_CreaVentaEnEstadoCotizacion | Happy path conversión |
| Convertir_CopiaDetallesDesdeSnapshot | Snapshot copiado a VentaDetalle |
| Convertir_MarcaCotizacionComoConvertida | Estado cotización actualizado |
| Convertir_NoConfirmaVenta | Venta queda en estado Cotizacion |
| Convertir_NoDescuentaStock | StockActual sin cambios |
| Convertir_NoMarcaProductoUnidad | ProductoUnidad sin cambios |
| Convertir_NoRegistraCaja | MovimientoCaja sin cambios |
| Convertir_NoGeneraFactura | Facturas sin cambios |
| Convertir_CotizacionYaConvertida_Falla | Guard doble conversión en tx |
| Convertir_ConAdvertenciasSinConfirmar_Falla | Guard advertencias |
| Convertir_ConAdvertenciasConfirmadas_Convierte | Override con advertencias |
| Convertir_SinClienteYSinOverride_Falla | Guard cliente obligatorio |
| Convertir_SinClienteConOverride_Convierte | ClienteIdOverride funciona |
| Convertir_UsandoPrecioActual_UsaPrecioDelResolver | Precio actual aplicado |
| Convertir_NoCreaCredito | Crédito sin cambios |
| Convertir_MapeoMedioPago_CreditoPersonal | Mapeo de TipoPago correcto |
| Convertir_IncluyeNumeroCotizacionEnObservaciones | Trazabilidad en Observaciones |

**Unit tests API (CotizacionConversionApiTests.cs):** 6 tests

| Test | Propósito |
|------|-----------|
| PreviewEndpoint_CotizacionConvertible_DevuelveOk | Happy path preview API |
| PreviewEndpoint_CotizacionNoConvertible_DevuelveOkConErrores | Preview con errores |
| ConvertirEndpoint_ConversionExitosa_DevuelveOk | Happy path conversión API |
| ConvertirEndpoint_ErrorFuncional_DevuelveBadRequest | Error → 400 |
| ConvertirEndpoint_RequestNull_DevuelveBadRequest | Null request → 400 |
| ConvertirEndpoint_LlamaServicioConUsuarioIdentity | Service llamado con cotizacionId correcto |
| ConversionService_EstaInyectadoEnController | DI correcta en constructor |

---

## K. Riesgos y deuda

| ID | Riesgo/Deuda | Estado |
|----|-------------|--------|
| D1 | Sin trazabilidad bidireccional Cotizacion ↔ Venta | Diferido — V1.4 evalúa CotizacionOrigenId o tabla CotizacionConversion |
| D2 | Campos IVA en VentaDetalle en 0 (sin desglose) | Aceptable en estado Cotizacion — el operador debe revisar antes de confirmar |
| D3 | UI de conversión no implementada | V1.4 |
| D4 | Permiso "convert" no diferenciado de "create" | Documentado — futuro permiso granular si hace falta |
| D5 | DatosTarjeta/DatosCheque no copiados desde cotización | Cotización no tiene esos datos estructurados — el operador los completa en la venta |
| D6 | Sin validación de planes activos de configuracion de pago | Verificación compleja diferida — el operador revisa al confirmar |

---

## L. Checklist

### Completado en V1.3
- [x] Diagnóstico previo respondido
- [x] DTOs de conversión creados
- [x] ICotizacionConversionService creado
- [x] CotizacionConversionService implementado
- [x] PreviewConversionAsync sin efectos secundarios
- [x] ConvertirAVentaAsync con transacción y guard anti-doble conversión
- [x] Mapeo CotizacionMedioPagoTipo → TipoPago
- [x] ClienteIdOverride funcional
- [x] Política de precios: snapshot vs actual + ConfirmarAdvertencias
- [x] Cotizacion.Estado = ConvertidaAVenta en misma transacción
- [x] Endpoints POST /api/cotizacion/{id}/conversion/preview y /convertir
- [x] Registro DI en Program.cs
- [x] 26 tests nuevos (19 integración + 7 unit API)
- [x] 90/90 tests passing (57 previos + 33 nuevos)
- [x] Build Release OK
- [x] git diff --check OK
- [x] Documentación V1.3 creada
- [x] Documentación V1.2 actualizada

### Pendiente (V1.4+)
- [ ] UI de conversión en Views/Cotizacion/Detalles.cshtml
- [ ] CotizacionOrigenId en Venta o tabla CotizacionConversion
- [ ] Permiso granular "cotizaciones/convert"
- [ ] Validación de planes activos en preview

---

## M. Prompt siguiente recomendado (V1.4 UI Conversión)

```
CARLOS — FASE COTIZACIÓN V1.4 — UI de conversión Cotización → Venta

Contexto: V1.3 cerrado. Endpoints disponibles:
- POST /api/cotizacion/{id}/conversion/preview
- POST /api/cotizacion/{id}/conversion/convertir

Implementar:
1. Botón "Convertir a Venta" en Views/Cotizacion/Detalles.cshtml (visible si Estado == Emitida)
2. Modal o flow: llamar preview, mostrar errores/advertencias, permitir ClienteIdOverride, elegir política de precios
3. POST conversión con ConfirmarAdvertencias cuando aplica
4. Redirect a /Venta/Edit/{ventaId} con mensaje de éxito
5. Tests de flujo UI si aplica

Trabajar en E:\theburyproject-carlos-cotizacion, rama carlos/cotizacion-v1-contratos.
```

---

## Nota V1.4

V1.4 implementa la UI que consume los endpoints preview/convertir de esta fase.
Ver: docs/fase-cotizacion-v1-4-ui-conversion-venta.md
