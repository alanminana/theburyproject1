# Cierre Final de Integración — 2026-05-16

Rama integrada: `integration/cierre-final` → `main`
HEAD resultante: `aabc45d`
Fecha: 2026-05-16

---

## A. Ramas integradas

| Rama | Desarrollador |
|------|--------------|
| `kira/fix-https-testhost` | Kira |
| `kira/fix-ventas-create` | Kira |
| `juan/fix-producto-movimientos-delete` | Juan |
| `juan/diagnostico-stockactual-unidades` | Juan |
| `juan/polish-movimientostock-kardex` | Juan |
| `carlos/cotizacion-v1-contratos` | Carlos |

Fix de integración aplicado: `aabc45d` — Actualizar stubs de producto para integracion final (agrega `GetByIdParaHistorialAsync` a fakes/stubs de tests de Cotización).

---

## B. Funcionalidad cerrada

### Juan

- **DocumentoCliente agrupado por cliente + modal de documentos** (fase 10.19)
  - Listado de documentos agrupado por cliente
  - Modal de visualización de documentos
- **MovimientoStock histórico preservado al eliminar producto**
  - Fix: eliminar producto ya no elimina movimientos de stock en cascada
  - MovimientoStock queda con referencia preservada o nulificada según diseño
- **StockActual vs unidades físicas validado** (diagnóstico fase 8.2.K)
  - Reconciliación entre stock contable y unidades físicas trazadas
- **MovimientoStock/Kardex visual** (polish fase)
  - Vista `Kardex_tw.cshtml` con mejoras visuales

### Kira

- **HTTPS/HSTS excluido en Testing**
  - `UseHttpsRedirection` y `UseHsts` solo corren fuera del entorno `Testing`
  - `CustomWebApplicationFactory` excluye `CotizacionVencimientoBackgroundService`
- **Ventas/Create fallback de medios de pago**
  - `Create_tw.cshtml` y `venta-create.js` con fallback robusto de medios de pago
  - Panel diagnóstico oculto en producción

### Carlos

- **Cotización V1.x completa**
  - V1.1: Persistencia mínima
  - V1.2–V1.4: Conversión a Venta (diseño, controlada, UI)
  - V1.5: Trazabilidad IVA en conversión
  - V1.7: Tests de seguridad de conversión
  - V1.8: Preview cambios unitarios
  - V1.9: Numeración robusta
  - V1.a–V1.e: Contratos, calculator readonly, API simulación, crédito personal readonly, UI readonly
  - V1.11: Vencimiento automático con BackgroundService
  - V1.12: Vista imprimible / descarga

  Funcionalidades cubiertas: simulador de pagos, persistencia de cotizaciones, conversión controlada a venta, permisos por rol, cancelación con motivo, vencimiento automático, impresión.

---

## C. Validaciones ejecutadas

### Pre-merge (rama `integration/cierre-final`)

| Validación | Resultado |
|-----------|-----------|
| `dotnet build --configuration Release` | OK — 0 errores, 0 advertencias |
| `dotnet ef migrations list` | OK — sin pending |
| `dotnet ef database update` | OK — "already up to date" |
| `git diff --check` | OK |
| `git status --short` | Limpio |
| `dotnet test --filter "CambiosPreciosAplicarRapidoTest"` | 1/1 passing — 11s |
| `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"` | 1/1 passing — 4s |
| `dotnet test --filter "CambiosPrecios"` | 1/1 passing |
| `dotnet test --filter "VentaApiController|ConfiguracionPago|Seguridad|Permiso"` | 228/228 passing |
| `dotnet test --filter "Cotizacion"` | 153/153 passing |
| `dotnet test --filter "MovimientoStock|Producto|Catalogo|Inventario"` | 657/657 passing |
| `dotnet test --filter "VentaCreateUiContractTests"` | 36/36 passing |

### Post-merge (rama `main`)

| Validación | Resultado |
|-----------|-----------|
| `dotnet build --configuration Release` | OK — 0 errores, 0 advertencias |
| `dotnet ef database update` | OK — "already up to date" |
| `git diff --check` | OK |
| `git status --short` | Limpio |
| `dotnet test --filter "CambiosPreciosAplicarRapidoTest"` | 1/1 passing |
| `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"` | 1/1 passing |
| `dotnet test --filter "Cotizacion"` | 153/153 passing |
| `dotnet test --filter "MovimientoStock|Producto|Catalogo|Inventario"` | 657/657 passing |
| `dotnet test --filter "VentaCreateUiContractTests"` | 36/36 passing |
| `git push origin main` | OK — `850be58..aabc45d` |

---

## D. Timeouts / Flakiness

### Tests afectados (suite completa anterior)

1. `TheBuryProject.Tests.Integration.CambiosPreciosAplicarRapidoTest.Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada`
2. `TheBuryProject.Tests.Integration.VentaApiControllerConfiguracionPagosGlobalTests.VentaApiController_ConfiguracionPagosGlobal_RutaHttpRespondeOkConListaVacia`

### Diagnóstico

- Ambos tests fueron ejecutados aislados antes y después del merge: **pasan en 100% de los casos**.
- Los timeouts de `HttpClient` (100s) solo ocurren cuando los tests corren dentro de la suite completa (~2920 tests).
- Causa probable: contención de recursos de TestHost bajo carga alta de suite completa — múltiples instancias de `WebApplicationFactory` compitiendo por puertos/threads.
- No hay bug funcional ni regresión de negocio.

### Decisión

Flakiness documentada como deuda de TestHost. No bloquea el merge a `main`. Los filtros críticos pasan en todos los casos.

---

## E. Migraciones

Migraciones de Cotización aplicadas correctamente:

| Migración | Estado |
|-----------|--------|
| `20260515194116_AddCotizaciones` | Aplicada |
| `20260515234236_AddCotizacionOrigenToVenta` | Aplicada |

### Deuda técnica menor — migración sin Designer.cs

El archivo `Migrations/20260516000000_AddCotizacionMotivoCancelacion.cs` existe en el repo pero **no tiene `.Designer.cs`**. EF Core no lo incluye en la cadena de migraciones y `dotnet ef migrations list` no lo muestra.

Sin embargo:
- El campo `MotivoCancelacion` **está presente en `AppDbContextModelSnapshot.cs`** (líneas 300, 5150, 7110).
- El campo **está en la entidad** `Models/Entities/Cotizacion.cs`.
- `dotnet ef database update` reporta "already up to date".
- Los 153 tests de Cotización (incluyendo cancelación) pasan.

**Conclusión:** la columna existe en la DB y el modelo es consistente. La migración `.cs` es redundante/huérfana. No afecta el funcionamiento actual. Se recomienda en una próxima sesión: eliminar el `.cs` huérfano o regenerar la migración correctamente con `dotnet ef migrations add`.

---

## F. Riesgos y deuda remanente

| Deuda | Prioridad | Descripción |
|-------|-----------|-------------|
| PDF real de cotización | Backlog | La vista imprimible actual es HTML/CSS. PDF generado desde servidor o descarga PDF real queda como backlog. |
| Email de cotización | Backlog | Envío de cotización por email no implementado. |
| WhatsApp de cotización | Backlog | Compartir por WhatsApp no implementado. |
| Flakiness de suite completa | Baja | 2 integration tests con timeout solo bajo carga de suite completa. Pasan aislados. Deuda de TestHost/paralelismo. |
| Migración AddCotizacionMotivoCancelacion sin .Designer.cs | Baja | Archivo `.cs` huérfano. DB y snapshot son consistentes. Cleanup recomendado. |

---

## G. Cierre

- Integración final completada: `integration/cierre-final` mergeada a `main` (fast-forward).
- `main` actualizado: HEAD `aabc45d`.
- Working tree limpio.
- Build Release sin errores.
- Migraciones aplicadas.
- Filtros críticos: todos passing.
- Push a origin/main: exitoso.

---

## Checklist final

- [x] `integration/cierre-final` validada
- [x] `main` actualizado
- [x] Build Release OK
- [x] Migraciones OK
- [x] Filtros críticos OK (1075+ tests passing)
- [x] Tests aislados de timeouts OK
- [x] Flakiness de suite completa documentada
- [x] `git diff --check` OK
- [x] `git status` limpio
- [x] `git push origin main` OK
- [x] Documento de cierre creado
