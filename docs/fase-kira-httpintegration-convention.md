# Kira - Convencion HttpIntegration para tests HTTP

## A. Problema previo

`VentaApiControllerConfiguracionPagosGlobalTests` usaba `CustomWebApplicationFactory` sin pertenecer a la coleccion xUnit `HttpIntegration`. En suite completa podia correr en paralelo con otros tests HTTP/TestHost y generar timeouts intermitentes de 100 segundos, aunque aislado pasara correctamente.

## B. Riesgo que previene

La suite podia volver a incorporar tests HTTP con `WebApplicationFactory`/`TestHost` fuera de la coleccion serializada. Eso reintroduciria contencion entre instancias de `TestServer`, `HttpClient` y SQLite in-memory bajo carga de CI.

## C. Regla definida

Toda clase de test que use `CustomWebApplicationFactory`, `WebApplicationFactory`, `CreateClient()`, `CreateAuthenticatedClient()` o `CreateClientWithUserId()` debe declarar:

```csharp
[Collection("HttpIntegration")]
```

La regla aplica a tests HTTP/TestHost. No aplica a helpers de infraestructura ni a tests que usan `HttpClient` con handlers fake sin TestHost.

## D. Como se implemento

Se agrego `TheBuryProyect.Tests/Architecture/HttpIntegrationCollectionConventionTests.cs`.

El test de arquitectura valida por dos caminos:

- reflexion: detecta clases concretas de test que implementan `IClassFixture<CustomWebApplicationFactory>` o fixtures basados en `WebApplicationFactory<Program>`;
- analisis liviano de fuente: detecta patrones de TestHost que reflexion no ve, como llamadas a `CreateClient()` o wrappers del factory dentro de clases con `[Fact]` o `[Theory]`.

Si encuentra incumplidores, falla con un mensaje claro:

`Las siguientes clases usan CustomWebApplicationFactory/TestHost y deben tener [Collection("HttpIntegration")]: ...`

## E. Clases corregidas

No hubo clases reales para corregir en este lote. Las clases HTTP existentes ya declaraban `[Collection("HttpIntegration")]`.

## F. Tests ejecutados

- `dotnet build --configuration Release` - OK.
- `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"` - OK, 1 test.
- `dotnet test --filter "CambiosPreciosAplicarRapidoTest"` - OK, 1 test.
- `dotnet test --filter "Integration"` - OK, 2117 tests en diagnostico inicial.
- `dotnet test --filter "HttpIntegration"` - OK, 1 test de convencion.
- `dotnet test --filter "Controller|Api|Integration|Seguridad|Permiso"` - OK, 2285 tests.
- `dotnet test` - OK, 2967 tests.
- `git diff --check` - OK.

Nota: una ejecucion inicial de varios `dotnet test` en paralelo fallo por lock de MSBuild/testhost sobre `TheBuryProyect.Tests.dll`/`.pdb`. Se re-ejecutaron los filtros en secuencia y pasaron.

## G. Que NO se toco

- No se toco codigo productivo.
- No se toco `Program.cs`.
- No se toco `CustomWebApplicationFactory`.
- No se toco `TestAuthHandler`.
- No se modificaron servicios, controllers, entidades, migraciones ni vistas.
- No se desactivo paralelismo global.
- No se modifico configuracion xUnit.

## H. Riesgos/deuda remanente

El analisis de fuente es intencionalmente simple y acotado a patrones reales del repo. Si en el futuro aparecen tests HTTP con una abstraccion nueva que no mencione `CustomWebApplicationFactory`, `WebApplicationFactory` ni wrappers `CreateClient*`, habra que agregar ese patron al test de convencion.
