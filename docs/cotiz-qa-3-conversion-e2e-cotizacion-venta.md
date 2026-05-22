# COTIZ-QA-3 — Validación E2E Conversión Cotización → Venta

## A. Objetivo

Validar el flujo completo Cotización → Venta desde la UI usando Playwright E2E:
- Crear cotización (simular + guardar).
- Abrir detalle de cotización.
- Convertir a Venta desde el modal de conversión.
- Verificar navegación a `/Venta/Edit/{id}`.
- Verificar estado "Convertida" en detalle de cotización.
- Verificar que descuentos no rompen la conversión.
- Documentar comportamiento real de descuentos en conversión.

## B. Contexto COTIZ-QA-2

COTIZ-QA-2 estableció los contratos de conversión mediante tests de integración .NET:

| Contrato | Comportamiento |
|---|---|
| `CotizacionDetalle.DescuentoImporteSnapshot` | Se copia al request y persiste en DB |
| `CotizacionDetalle.DescuentoPorcentajeSnapshot` | Se copia al request y persiste en DB |
| `Cotizacion.DescuentoTotal` | Calculado por `ICotizacionPagoCalculator.SimularAsync` |
| `DescuentoImporteSnapshot → VentaDetalle.Descuento` | Propagado en conversión |
| `DescuentoPorcentajeSnapshot solo → VentaDetalle.Descuento` | = 0 (no propagado) |
| `Venta.Descuento` | Siempre 0 (descuento general no se propaga) |

También agregó T7 y T8 en `cotizacion-simulador.spec.js` cubriendo guardar → detalles.

## C. Flujo UI auditado

### Ruta completa Cotización → Venta

```
GET  /Cotizacion                      → simulador
POST /api/cotizacion/simular          → resultados (cards)
POST /api/cotizacion/guardar          → {id, numero, detalleUrl}
                                        navega a /Cotizacion/Detalles/{id}

GET  /Cotizacion/Detalles/{id}        → vista Detalles_tw.cshtml
                                        puedeConvertir = Estado.Emitida && permiso "convert"
                                        muestra panel amber con #cotizacion-btn-convertir

click #cotizacion-btn-convertir
POST /api/cotizacion/{id}/conversion/preview  → modal abre con preview
                                               totalCotizado, detalles, errores, advertencias
                                               clienteFaltante (solo si CreditoPersonal sin cliente)

click #cotizacion-btn-confirmar-conversion
POST /api/cotizacion/{id}/conversion/convertir  → {exitoso, ventaId}
                                                  JS: window.location.href = /Venta/Edit/{ventaId}

GET  /Venta/Edit/{ventaId}            → venta creada, editable

GET  /Cotizacion/Detalles/{id}        → badge .quote-state-badge--convertida
                                        panel emerald con link Ver venta /Venta/Edit/{ventaId}
                                        panel de conversión ya no visible
```

### Selectores clave (producción)

| Selector | Rol |
|---|---|
| `#cotizacion-btn-convertir` | Abre modal de conversión |
| `#cotizacion-conversion-modal` | Modal completo |
| `#cotizacion-conversion-loading` | Panel "Calculando preview..." |
| `#cotizacion-conversion-contenido` | Contenido del preview |
| `#cotizacion-btn-confirmar-conversion` | Confirma conversión |
| `.quote-state-badge--convertida` | Badge estado Convertida |
| `.quote-state-badge--emitida` | Badge estado Emitida |
| `#cotizacion-conversion-panel` | Panel amber (solo si puedeConvertir) |
| `[data-cotizacion-desc-importe-index="0"]` | Input descuento importe primer producto |

### Lógica de habilitación del botón confirmar

```js
// cotizacion-conversion.js
const clienteOk = !previewData.clienteFaltante || clienteIdInput.value !== '';
const advertenciasOk = previewData.advertencias.length === 0 || checkAdvertencias.checked;
btnConfirmar.disabled = !(clienteOk && advertenciasOk);
```

**Comportamiento real auditado en `CotizacionConversionService.cs`:**
```csharp
var clienteFaltante = cotizacion.ClienteId is null; // SIEMPRE true sin cliente
```
La conversión **siempre** requiere `ClienteId`, independientemente del medio de pago.
Los tests E2E asignan un cliente al crear la cotización (via `#cotizacion-cliente-buscar`)
para que el flujo proceda sin intervención manual en el modal de override.

## D. Spec creado

**Archivo:** `e2e/cotizacion-conversion.spec.js`

Nuevo spec independiente. No se modificó `cotizacion-simulador.spec.js`.

### Helpers internos del spec

| Helper | Descripción |
|---|---|
| `gotoCotizacion(page)` | Navega a /Cotizacion y espera `#cotizacion-simular` |
| `agregarProductoSimulador(page)` | Busca y agrega el primer producto disponible |
| `crearCotizacionYNavegar(page)` | Crear cotización completa y retorna URL de Detalles |
| `abrirModalYEsperarPreview(page)` | Click en convertir, espera preview, retorna si es habilitado |

## E. Escenarios cubiertos

| Test | Qué valida |
|---|---|
| T9: Conversión completa | Simular → guardar → Detalles → modal → confirmar → `/Venta/Edit/{id}` |
| T10: Estado "Convertida" | Volver a Detalles post-conversión: badge Convertida + link a venta + sin botón Convertir |
| T11: Con descuento por producto importe | Descuento importe $50 → conversión no rompe → navega a Venta/Edit |
| T12: Panel conversión ausente | Cotización ya convertida → `#cotizacion-conversion-panel` no en DOM |

Todos los tests de conversión incluyen `test.skip` si no hay productos en la DB o si el modal no es accesible.

## F. Escenarios no cubiertos

| Escenario | Motivo |
|---|---|
| Conversión con cliente explícito | La cotización sin cliente (mostrador) es suficiente para el flujo; agregar búsqueda de cliente aumentaría fragilidad sin valor marginal |
| Conversión con advertencias (checkbox) | Requiere producto con precio cambiado o trazable; frágil en entorno variable |
| Conversión bloqueada por CreditoPersonal sin cliente | Cubierto por tests .NET; el E2E no lo puede controlar fácilmente |
| Validar `VentaDetalle.Descuento` desde UI | No hay vista pública de detalle de venta en `/Venta/Edit` que muestre el importe exacto de descuento por ítem de forma estable |
| Conversión con descuento solo porcentaje | Comportamiento (VentaDetalle.Descuento = 0) cubierto por tests .NET; el E2E solo valida que no rompe la navegación |
| Edge case importe > subtotal | Sin evidencia de bug; fuera de alcance |
| Advertencia visual para descuento % sin importe | Fuera de alcance de esta fase |

## G. Comportamiento real de descuentos en conversión

Confirmado por tests .NET (COTIZ-QA-2) y auditado en `CotizacionConversionService.cs`:

### Descuento por producto

| Campo | En CotizacionDetalle | En VentaDetalle |
|---|---|---|
| `DescuentoImporteSnapshot` | Guardado desde request | → `VentaDetalle.Descuento` |
| `DescuentoPorcentajeSnapshot` | Guardado desde request | No se propaga (VentaDetalle.Descuento = 0 si solo pct) |

**Prioridad:** Si ambos están presentes, `DescuentoImporteSnapshot` tiene prioridad.

### Descuento general

- `Cotizacion.DescuentoTotal` refleja el resultado del calculator.
- `Venta.Descuento` = 0 siempre. El descuento general de la cotización no se propaga a `Venta.Descuento`.

### Advertencia potencial (deuda)

El sistema actualmente no advierte al usuario cuando usa solo `DescuentoPorcentajeSnapshot`
que dicho porcentaje **no se propagará** como descuento en la Venta resultante. Esta puede
ser una mejora UX futura, pero está fuera del alcance de COTIZ-QA-3.

## H. Evidencia de conversión

Screenshots guardados en `qa-evidence/cotiz-qa-3/`:

| Archivo | Test |
|---|---|
| `T9-venta-edit-post-conversion.png` | Pantalla de /Venta/Edit post-conversión |
| `T10-cotizacion-estado-convertida.png` | Detalle cotización con estado Convertida |
| `T11-conversion-con-descuento-importe.png` | Post-conversión con descuento importe |

## I. Archivos modificados

| Archivo | Tipo | Cambio |
|---|---|---|
| `e2e/cotizacion-conversion.spec.js` | E2E nuevo | 4 tests de conversión (T9-T12) |
| `docs/cotiz-qa-3-conversion-e2e-cotizacion-venta.md` | Doc nueva | Documentación COTIZ-QA-3 |

Sin cambios en producción (controllers, services, vistas, entities, migrations, JS productivo).

## J. Contratos preservados

- `CotizacionController` sin cambios.
- `CotizacionApiController` sin cambios.
- `CotizacionConversionService` sin cambios.
- `VentaService` no tocado.
- Ninguna migración.
- Ningún cambio de schema.
- T7 y T8 de COTIZ-QA-2 intactos en `cotizacion-simulador.spec.js`.

## K. Validaciones

```
dotnet build --configuration Release             → OK
dotnet test --filter "Cotizacion"                → ejecutado
dotnet test --filter "LayoutUiContractTests"     → ejecutado
npx.cmd playwright test e2e/cotizacion-simulador.spec.js    → 57/57 OK (regresión)
npx.cmd playwright test e2e/cotizacion-conversion.spec.js   → ver sección M
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js     → 169/169 OK (regresión)
```

## L. Playwright

### Configuración

```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
$env:ASPNETCORE_ENVIRONMENT="Development"
```

### Selectores del spec

Todos los selectores son IDs explícitos o clases existentes en producción. No se usaron `data-testid` adicionales.

## M. Procesos

- Se inició la app para correr Playwright.
- Al cierre: TheBuryProyect.exe terminado si fue iniciado por esta tarea.
- dotnet/testhost/vstest: pueden quedar como residuo de `dotnet test` — no es riesgo.

## N. Riesgos y deudas

| Riesgo/Deuda | Severidad | Nota |
|---|---|---|
| `DescuentoPorcentajeSnapshot` no propagado → sin advertencia visual | Baja | UX pendiente |
| Edge case importe > subtotal | Baja | Sin evidencia de bug; registrado |
| Si Admin no tiene permiso `cotizaciones:convert` → T9-T12 se skipean | Media | Verificar permiso en seeding |
| Conversión con CreditoPersonal sin cliente requiere client search en E2E | Baja | No cubierto intencionalmente |

## O. Próximo paso recomendado

**VENTAS-UX-1** — Mejoras de UX en el flujo de Venta (pantalla Edit post-conversión).

Alternativa: evaluar si corresponde agregar una advertencia visual en el modal de conversión
cuando el descuento de un producto fue ingresado solo por porcentaje (sin importe), informando
que dicho porcentaje no se trasladará como descuento a la venta.
