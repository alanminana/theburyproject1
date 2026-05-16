# Fase Cotización V1.11 — Vencimiento Automático de Cotizaciones

## A. Objetivo

Implementar vencimiento automático de cotizaciones emitidas cuya `FechaVencimiento` haya sido superada.

Regla funcional:
- Solo se vencen cotizaciones con `Estado == Emitida` y `FechaVencimiento < fechaReferenciaUtc`
- No se vencen: `Borrador`, `Cancelada`, `ConvertidaAVenta`, `Vencida`
- No se modifican ventas ni ningún otro módulo

---

## B. Diagnóstico Previo

### Preguntas respondidas

**A. Campo de vencimiento:**  
`Cotizacion.FechaVencimiento` (`DateTime?`) en `Models/Entities/Cotizacion.cs:41`

**B. Nullable:** Sí, `DateTime?`

**C. Cálculo al crear:** Viene directo del request (`CotizacionCrearRequest.FechaVencimiento`). No hay default de días. El campo es opcional.

**D. Estado visual vs cambio real:** `EstadoCotizacion.Vencida = 2` existía pero nada lo asignaba automáticamente. El flag `CotizacionVencida` en `CotizacionConversionModels` era solo informativo para el preview de conversión.

**E. Background services:** Sí existen 3 implementaciones consolidadas:
- `MoraBackgroundService`
- `AlertaStockBackgroundService`  
- `DocumentoVencidoBackgroundService`

**F. Endpoint administrativo:** No existía para cotizaciones. `DocumentoVencidoBackgroundService` llama directamente al service sin endpoint separado.

**G. Decisión de vencimiento:** Método de servicio + BackgroundService + endpoint manual (ver sección D).

**H. Tests existentes para Vencida:** No existían tests para `EstadoCotizacion.Vencida` en cotizaciones.

**I. ConvertidaAVenta con fecha vencida:** No se vence — la regla `Estado == Emitida` es suficiente guardia. `CotizacionConversionService.EsVencida()` ya impedía convertir cotizaciones con fecha expirada aunque el estado fuera Emitida.

**J. Migración:** No necesaria. `FechaVencimiento` ya existía en la entidad.

---

## C. Clasificación de Componentes

| Componente | Clasificación | Evidencia | Decisión |
|---|---|---|---|
| `CotizacionService` | canónico | DI registrado, usado por controller | modificar |
| `ICotizacionService` | canónico | inyectado en controller y tests | extender |
| `CotizacionApiController` | canónico | route `/api/cotizacion`, Authorize | agregar endpoint |
| `EstadoCotizacion.Vencida` | canónico | enum value = 2, ya existía | usar |
| `FechaVencimiento` en `Cotizacion` | canónico | `DateTime?`, mapeado, en request | usar |
| `DocumentoVencidoBackgroundService` | canónico | patrón establecido, registrado en Program.cs | replicar patrón |
| `CotizacionVencimientoBackgroundService` | canónico nuevo | creado en V1.11 | registrar en Program.cs |

---

## D. Decisión Técnica

**Opción elegida:** Combinación de Método de servicio + BackgroundService + endpoint manual.

Justificación:
- El proyecto ya tiene 3 background services con el mismo patrón (`DocumentoVencidoBackgroundService`, `MoraBackgroundService`, `AlertaStockBackgroundService`). Seguirlo es coherente y no introduce infraestructura nueva.
- El método de servicio `VencerEmitidasAsync` es testeable de forma aislada.
- El endpoint `/api/cotizacion/vencer-expiradas` permite dispararlo manualmente sin depender del reloj.
- No se agregó lógica de vencimiento al listar/detallar (lecturas con efectos secundarios — se evitó).

---

## E. Método de Servicio

```csharp
// ICotizacionService
Task<CotizacionVencimientoResultado> VencerEmitidasAsync(
    DateTime fechaReferenciaUtc,
    string usuario,
    CancellationToken cancellationToken = default);
```

### CotizacionVencimientoResultado

```csharp
public sealed class CotizacionVencimientoResultado
{
    public bool Exitoso { get; init; }
    public int CantidadEvaluadas { get; init; }
    public int CantidadVencidas { get; init; }
    public List<int> CotizacionesVencidasIds { get; init; }
    public List<string> Advertencias { get; init; }
    public List<string> Errores { get; init; }
}
```

### Implementación (`CotizacionService.VencerEmitidasAsync`)

- Query: `Estado == Emitida && FechaVencimiento.HasValue && FechaVencimiento < fechaReferenciaUtc && !IsDeleted`
- Cambio: `Estado = Vencida`
- Ejecución en transacción con rollback en error
- Auditoría por interceptor de EF (UpdatedAt/UpdatedBy)
- No modifica `MotivoCancelacion`

---

## F. Endpoint

```
POST /api/cotizacion/vencer-expiradas
```

- Permiso: `[PermisoRequerido(Modulo = "cotizaciones", Accion = "expire")]`
- Respuesta exitosa: `200 OK` con `CotizacionVencimientoResultado`
- Error interno: `500` con `{ errores: [...] }`

---

## G. BackgroundService

`CotizacionVencimientoBackgroundService`:
- Ejecuta diariamente a las **3:00 AM UTC**
- Llama a `ICotizacionService.VencerEmitidasAsync(DateTime.UtcNow, "Sistema")`
- Sigue exactamente el patrón de `DocumentoVencidoBackgroundService`
- Registrado en `Program.cs` con `AddHostedService<CotizacionVencimientoBackgroundService>()`
- Removido en tests via `CustomWebApplicationFactory.BackgroundServicesToRemove`

---

## H. Reglas de Transición

```
Emitida + FechaVencimiento < ahora → Vencida  ✓
Emitida + FechaVencimiento >= ahora → Emitida (no cambia)  ✓
Emitida + sin FechaVencimiento → Emitida (no cambia)  ✓
Borrador + cualquier fecha → Borrador (no cambia)  ✓
Cancelada + cualquier fecha → Cancelada (no cambia)  ✓
ConvertidaAVenta + cualquier fecha → ConvertidaAVenta (no cambia)  ✓
Vencida + cualquier fecha → Vencida (no cambia)  ✓
IsDeleted == true → ignorada  ✓
```

---

## I. Tests Agregados

### Service Tests (CotizacionVencimientoServiceTests — 11 tests)

1. `VencerEmitidasAsync_CotizacionEmitidaVencida_CambiaAVencida`
2. `VencerEmitidasAsync_CotizacionEmitidaNoVencida_NoCambia`
3. `VencerEmitidasAsync_CotizacionCanceladaVencida_NoCambia`
4. `VencerEmitidasAsync_CotizacionConvertidaVencida_NoCambia`
5. `VencerEmitidasAsync_CotizacionBorradorVencida_NoCambia`
6. `VencerEmitidasAsync_CotizacionYaVencida_NoCambia`
7. `VencerEmitidasAsync_SinFechaVencimiento_NoCambia`
8. `VencerEmitidasAsync_DevuelveCantidadVencidas`
9. `VencerEmitidasAsync_DevuelveCantidadEvaluadas_SoloEmitidas`
10. `VencerEmitidasAsync_NoTocaMotivoCancelacion`
11. `VencerEmitidasAsync_RespetaIsDeleted`

### Security Tests (CotizacionVencimientoSecurityTests — 3 tests)

1. `VencerExpiradas_SinPermisoExpire_DevuelveForbidden`
2. `VencerExpiradas_ConPermisoExpire_NoDevuelveForbidden`
3. `VencerExpiradas_SuperAdmin_DevuelveOk`

**Total V1.11:** 14 tests nuevos  
**Total suite:** 145 tests (131 pre-V1.11 + 14 V1.11)

---

## J. Validaciones Técnicas

```
dotnet build --configuration Release → OK (0 errores, 0 advertencias)
dotnet test --filter "Cotizacion"   → 145/145 pasando
dotnet test (suite completa)        → pendiente al momento de cierre
git diff --check                    → solo warnings CRLF (ignorables)
```

---

## K. Qué NO se tocó

- Ventas, Caja, Factura, Stock, ProductoUnidad
- Módulos de Juan (MovimientoStock, Kardex)
- Módulos de Kira (TestHost, Ventas/Create)
- Lógica de conversión de cotizaciones (no se modificó `CotizacionConversionService`)
- UI/Razor (no se agregó botón ni lógica visual nueva)
- Migraciones (no necesaria)
- Hangfire / Quartz (no existe en el proyecto)
- Email/WhatsApp/Impresión (fuera de alcance)

---

## L. Riesgos y Deuda Remanente

| Riesgo | Nivel | Descripción |
|---|---|---|
| FechaVencimiento no obligatoria | bajo | Cotizaciones sin fecha no vencen nunca. Comportamiento intencional pero puede sorprender. |
| Hora de ejecución en UTC | bajo | El BackgroundService usa 3:00 AM UTC. Si el servidor está en zona horaria distinta, ajustar. |
| Sin UI para disparar manualmente | bajo | El endpoint existe pero no hay botón en el frontend. Para V1.12 si se requiere. |
| Sin audit trail de vencimiento | medio | Solo hay log. No hay registro en tabla de auditoría explícita sobre quién disparó el vencimiento. |

---

## M. Checklist V1.11

- [x] Diagnóstico previo completo
- [x] `CotizacionVencimientoResultado` agregado a `CotizacionCrearRequest.cs`
- [x] `VencerEmitidasAsync` en `ICotizacionService`
- [x] `VencerEmitidasAsync` implementado en `CotizacionService`
- [x] `CotizacionVencimientoBackgroundService` creado
- [x] Endpoint `POST /api/cotizacion/vencer-expiradas` con permiso `cotizaciones.expire`
- [x] `CotizacionVencimientoBackgroundService` registrado en `Program.cs`
- [x] `CotizacionVencimientoBackgroundService` removido en `CustomWebApplicationFactory`
- [x] `ExpirePermsUserId` y `SeedUserWithExpirePermissionAsync` en factory
- [x] Stubs de tests unitarios actualizados (3 archivos)
- [x] `CotizacionVencimientoServiceTests` — 11 tests
- [x] `CotizacionVencimientoSecurityTests` — 3 tests
- [x] Build limpio (0 errores, 0 advertencias)
- [x] Suite Cotizacion: 145/145 pasando
- [x] Documentación creada

---

## N. Siguiente Micro-lote Recomendado

**V1.12 — Impresión/Envío de cotizaciones**

Opciones:
- Generar PDF de cotización (template Razor → HTML → PDF)
- Envío por email básico (SMTP ya presente en el proyecto?)
- Si el proyecto no tiene SMTP configurado, empezar por PDF/descarga

Prerequisito: revisar si `SmtpService` o similar existe en el proyecto.
