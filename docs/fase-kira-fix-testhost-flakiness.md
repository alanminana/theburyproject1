# Kira — Fix TestHost Flakiness en Suite Completa

**Rama:** `kira/fix-testhost-flakiness`
**Fecha:** 2026-05-16
**Agente:** Kira (QA Automation Engineer)

---

## A. Problema

Dos tests de integración HTTP fallaban intermitentemente en suite completa con:

```
System.Threading.Tasks.TaskCanceledException
HttpClient.Timeout de 100 segundos
```

Tests afectados:
1. `CambiosPreciosAplicarRapidoTest.Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada`
2. `VentaApiControllerConfiguracionPagosGlobalTests.VentaApiController_ConfiguracionPagosGlobal_RutaHttpRespondeOkConListaVacia`

Ambos **pasan en aislamiento y corridos juntos**. Solo fallan en suite completa — flakiness clásico por contención de infraestructura.

---

## B. Tests afectados

| Test | Clase | Colección antes del fix |
|------|-------|------------------------|
| `Post_AplicarRapido_SinListasExplicitas_UsaListaPredeterminada` | `CambiosPreciosAplicarRapidoTest` | `[Collection("HttpIntegration")]` ✓ |
| `VentaApiController_ConfiguracionPagosGlobal_RutaHttpRespondeOkConListaVacia` | `VentaApiControllerConfiguracionPagosGlobalTests` | **sin colección** ← causa raíz |

---

## C. Diagnóstico

### Infraestructura de tests HTTP

- 10 clases de test usan `IClassFixture<CustomWebApplicationFactory>` — cada una crea su propio `TestServer` + pipeline ASP.NET completo + SQLite in-memory.
- 9 de las 10 están en `[Collection("HttpIntegration")]` → corren **secuencialmente** entre sí.
- `VentaApiControllerConfiguracionPagosGlobalTests` **NO** tenía `[Collection]` → corría **en paralelo con todo**.

### Tests no-HTTP (service tests)

- Usan `SqliteConnection` directamente con `IDisposable`, **sin** `CustomWebApplicationFactory`.
- No crean `TestServer` → no compiten con los HTTP tests por ThreadPool.

### Middleware y background services

- `UseHsts` y `UseHttpsRedirection` desactivados en `Environment="Testing"` en `Program.cs`.
- Los 4 `BackgroundService` (`MoraBackgroundService`, `AlertaStockBackgroundService`, `DocumentoVencidoBackgroundService`, `CotizacionVencimientoBackgroundService`) correctamente removidos en `CustomWebApplicationFactory.ConfigureWebHost`.
- `TestAuthHandler` funciona correctamente para autenticación fake.

### Escenario de fallo

Cuando la suite completa corre:
1. La colección `HttpIntegration` inicia — ejecuta sus tests secuencialmente, crea/destruye `CustomWebApplicationFactory` por clase.
2. `VentaApiControllerConfiguracionPagosGlobalTests` (sin colección) crea su propio `CustomWebApplicationFactory` **en paralelo** con el test que esté corriendo en ese momento en la colección.
3. Dos `TestServer` activos simultáneamente compiten por threads del mismo `ThreadPool` compartido con xUnit.
4. Thread pool starvation: las continuaciones `async/await` del `TestServer` no tienen threads libres.
5. `HttpClient.SendAsync` no recibe respuesta en 100 segundos → `TaskCanceledException`.

---

## D. Causa raíz

`VentaApiControllerConfiguracionPagosGlobalTests` no tenía `[Collection("HttpIntegration")]`.

Esto permitía que se ejecutara en **paralelo** con los tests de la colección `HttpIntegration`, creando dos `WebApplicationFactory`/`TestServer` simultáneos. Bajo carga de suite completa (ThreadPool saturado por xUnit manejando múltiples clases), la contención generaba timeouts de 100s.

---

## E. Fix aplicado

**Archivo:** [`TheBuryProyect.Tests/Integration/VentaApiControllerConfiguracionPagosGlobalTests.cs`](../TheBuryProyect.Tests/Integration/VentaApiControllerConfiguracionPagosGlobalTests.cs)

**Cambio:** agregar `[Collection("HttpIntegration")]` a la clase.

```diff
+[Collection("HttpIntegration")]
 public sealed class VentaApiControllerConfiguracionPagosGlobalTests : IClassFixture<CustomWebApplicationFactory>
```

**Por qué funciona:**

- xUnit ejecuta todos los tests de una colección **secuencialmente** entre sí.
- Con el fix, los 10 tests HTTP están en la misma colección → nunca hay dos `TestServer` activos simultáneamente.
- El `IClassFixture<CustomWebApplicationFactory>` se mantiene: cada clase sigue teniendo su propio SQLite aislado — no hay contaminación de datos entre tests.

**Efecto secundario positivo:** suite completa pasó de 4m01s a 2m36s. Los TestHosts ya no compiten entre sí por recursos del sistema.

---

## F. Validaciones

### Build
```
dotnet build --configuration Release
→ 0 errores, 0 advertencias
```

### Tests aislados (pre y post fix)
```
dotnet test --filter "CambiosPreciosAplicarRapidoTest"          → 1/1 ✓
dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal" → 1/1 ✓
```

### Tests juntos (pre y post fix)
```
dotnet test --filter "CambiosPreciosAplicarRapidoTest|VentaApiController_ConfiguracionPagosGlobal" → 2/2 ✓
```

### Tests HTTP relacionados
```
dotnet test --filter "VentaApiController|ConfiguracionPago|Seguridad|Permiso" → 229/229 ✓
```

### Suite completa post-fix
```
dotnet test → 2929/2929 ✓ (2m 36s)
```

### diff --check
```
→ clean (warning LF→CRLF es comportamiento normal de git core.autocrlf en Windows)
```

---

## G. Qué NO se tocó

- `VentaService`, `CambiosPreciosService`, `CotizacionService`, `Caja`, `Factura`, `Stock`
- Ningún servicio de dominio
- `Program.cs` (no era necesario — middleware ya estaba correctamente configurado)
- `CustomWebApplicationFactory.cs` (ya removía correctamente los BackgroundServices)
- `TestAuthHandler.cs`
- Ninguna vista, migración ni entidad
- Ningún test de lógica de negocio

---

## H. Riesgos y deuda remanente

### Riesgos residuales

- El fix garantiza ausencia de paralelo entre HTTP tests, pero no cubre hipotéticos timeouts de red real si un endpoint productivo cuelga en Testing. Actualmente no hay evidencia de ese problema.
- Si se agrega un test HTTP nuevo sin `[Collection("HttpIntegration")]`, el problema puede reaparecer. Mitigación: documentar la convención.

### Deuda documentada (fuera de alcance)

1. **Doble lectura de body en `CambiosPreciosAplicarRapidoTest` (líneas 97 y 104):** `ReadAsStringAsync()` se llama antes de `ReadFromJsonAsync()`. En TestServer con `MemoryStream` esto no falla (el stream es seekable), pero es un patrón frágil. No causa el timeout; se deja como deuda menor.

2. **Convención de colección no documentada:** no existe ningún comentario ni guard que recuerde que todo test HTTP debe tener `[Collection("HttpIntegration")]`. Podría agregarse un test de arquitectura (ArchUnit / reflexión) que verifique que toda clase `IClassFixture<CustomWebApplicationFactory>` tenga la anotación.

3. **`xunit.runner.json` no existe:** el paralelismo está activo por defecto. Si en el futuro hay más clases HTTP fuera de la colección, considerar crear el archivo con `"parallelizeTestCollections": false` como red de seguridad adicional.

---

## I. Resultado final

| Métrica | Antes | Después |
|---------|-------|---------|
| Tests afectados | 2 tests flaky en suite completa | 0 tests flaky |
| Causa | `VentaApiController*` sin `[Collection]` | Corregido |
| Archivos modificados | — | 1 (1 línea agregada) |
| Suite completa | Falla intermitente | 2929/2929 ✓ |
| Duración suite | ~4m01s | ~2m36s |
| Lógica productiva tocada | — | Ninguna |
| Tests deshabilitados | — | Ninguno |

**Estado:** cerrado. Fix mínimo, bajo riesgo, causa raíz eliminada.
