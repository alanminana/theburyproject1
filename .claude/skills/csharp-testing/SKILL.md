---
name: csharp-testing
description: Patrones de testing C#/xUnit para TheBuryProject usando el stack real del proyecto (xUnit, WebApplicationFactory, SQLite en memoria, stubs manuales). Usar cuando el pedido sea crear, modificar, revisar o debuggear tests C#/.NET (unit, integración, contrato/arquitectura) en TheBuryProyect.Tests. No aplica a tests Playwright/E2E de navegador (ver playwright-protocolo) ni a tareas C# que no sean de testing.
metadata:
  origin: adaptado de affaan-m/ECC (skills/csharp-testing/SKILL.md)
  upstream-commit: 5eddf1a3ffd311423be2d4ba7d26f7209c91b033
  upstream-date: 2026-08-27
---

# Testing C# (stack real del proyecto)

## Stack real — no asumir otro

- Framework: xUnit 2.5.3 (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`).
- Aserciones: `Assert.*` de xUnit. **No hay FluentAssertions instalado.**
- Integración HTTP: `Microsoft.AspNetCore.Mvc.Testing` vía
  `CustomWebApplicationFactory : WebApplicationFactory<Program>`
  (`TheBuryProyect.Tests/CustomWebApplicationFactory.cs`).
- Base de datos de test: **SQLite en memoria real** (`Microsoft.EntityFrameworkCore.Sqlite`),
  conexión `Filename=:memory:` o `DataSource={Guid}:...;Mode=Memory;Cache=Shared` mantenida
  abierta durante el ciclo de vida del test/factory. **No usar el proveedor EF Core InMemory** — el
  proyecto lo evita deliberadamente porque SQLite soporta `ExecuteUpdate`/`ExecuteDelete` y
  transacciones reales (ver el comentario explícito en `CustomWebApplicationFactory.cs`).
- Dobles de prueba: implementaciones manuales de las interfaces de servicio (`Stub*`, `*Fake`) en
  `TheBuryProyect.Tests/Infrastructure/` y `TheBuryProyect.Tests/Helpers/` (p. ej.
  `StubPrecioService`, `RelojComercialFake`, `StubContratoVentaCreditoService`). **No hay Moq,
  NSubstitute ni ninguna librería de mocking instalada.**
- Datos de prueba: construidos a mano en cada test o en seeders (`E2ESeeding/`). **No hay Bogus
  instalado.**
- Autenticación en tests HTTP: `TestAuthHandler` (`Infrastructure/TestAuthHandler.cs`) con el
  header `X-Test-User-Id` para simular un usuario distinto por request.
- Cobertura: `coverlet.collector` está referenciado pero no hay un umbral obligatorio configurado.
  No imponer 80% ni ningún otro número salvo que `AGENTS.md` o un pedido explícito lo pida.

## Organización existente — seguirla, no inventar una nueva

```
TheBuryProyect.Tests/
  Architecture/    # tests de contrato/arquitectura (auditan invariantes entre capas)
  Integration/     # tests contra AppDbContext real (SQLite) o HTTP end-to-end (*HttpTests.cs)
  Unit/            # tests unitarios, con subcarpetas por área (p. ej. Unit/MercadoLibre/)
  E2ESeeding/      # seeders para escenarios E2E
  Helpers/         # fakes y builders reutilizables
  Infrastructure/  # stubs de infraestructura (auth, interceptors, servicios externos)
```

Nombrar los tests nuevos según lo ya establecido:

- `<Sujeto>Tests.cs` — unit o integración directa contra `AppDbContext`;
- `<Flujo>HttpTests.cs` — integración HTTP end-to-end vía `CustomWebApplicationFactory`;
- `<Invariante>ContractTests.cs` / `<Invariante>AuditTests.cs` — tests de arquitectura/contrato.

## Patrón: test de integración contra EF Core (SQLite real)

```csharp
public class AlgoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AlgoService _service;

    public AlgoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new AlgoService(_context, NullLogger<AlgoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Metodo_Comportamiento_Cuando_Condicion()
    {
        // Arrange — seed vía helpers privados del propio test, como en AutorizacionServiceTests
        // Act
        // Assert — Assert.* de xUnit
    }
}
```

Ver `Integration/AutorizacionServiceTests.cs` como referencia completa (seeding helpers privados,
`Dispose` de connection + context).

## Patrón: test HTTP end-to-end (`CustomWebApplicationFactory`)

```csharp
public class AlgoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AlgoHttpTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Devuelve_Ok()
    {
        var response = await _client.GetAsync("/ruta/real");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Usar el header `X-Test-User-Id` de `TestAuthHandler` cuando el test necesite un usuario distinto al
default. No crear una factory nueva; reutilizar `CustomWebApplicationFactory` salvo necesidad
concreta y documentada.

## Dobles de prueba sin librería de mocking

Cuando un test necesita una dependencia que no es el foco del test:

1. Buscar primero si ya existe un stub/fake reutilizable en `Helpers/` o `Infrastructure/`.
2. Si no existe, escribir una implementación mínima de la interfaz en el mismo estilo: los métodos
   no ejercitados devuelven un valor por defecto razonable, o `throw new NotImplementedException()`
   si de verdad no deberían llamarse (ver `StubPrecioService` como ejemplo).
3. No introducir Moq/NSubstitute para evitar escribir un stub manual de pocas líneas. Si una
   dependencia es tan grande que un stub manual resulta inmanejable, plantearlo como hallazgo y
   pedir autorización antes de agregar una librería.

## Qué priorizar

- comportamiento observable, no implementación interna;
- aislamiento por test: `Dispose` de connection/context, sin estado compartido entre tests;
- nombres claros por la convención existente (`Metodo_Resultado_CuandoCondicion` quando aplique, o
  el patrón ya usado en el archivo que se está extendiendo);
- tests deterministas — usar `RelojComercialFake` en vez de `DateTime.Now`/`UtcNow` real cuando el
  comportamiento depende de fecha/hora;
- async correcto en los tests (`await`, nunca `.Result`/`.Wait()`);
- `CancellationToken` cuando el método bajo test ya lo expone;
- no depender del orden de ejecución entre tests.

## Qué requiere autorización antes de instalarse

Si una recomendación de testing requiere FluentAssertions, Moq, NSubstitute, Testcontainers, Bogus,
el proveedor EF Core InMemory, o cualquier paquete/proyecto de test nuevo: explicar la necesidad
concreta y el costo/beneficio, y esperar autorización explícita. No agregarlo por iniciativa propia.

## Qué se descartó de ECC y por qué

- Todo el stack propuesto (FluentAssertions, NSubstitute/Moq, Testcontainers, Bogus) — no está
  instalado; el proyecto ya cubre el mismo objetivo con stubs manuales + SQLite real, sin
  dependencias nuevas.
- El ejemplo de `WebApplicationFactory` con `UseInMemoryDatabase("TestDb")` — reemplazado por el
  patrón real del proyecto (SQLite en memoria), que es explícitamente lo opuesto a lo que ECC
  sugiere.
- Proyectos separados `MyApp.UnitTests` / `MyApp.IntegrationTests` — el proyecto usa un único
  proyecto de test (`TheBuryProyect.Tests`) con carpetas por tipo; no se propone dividirlo.
- Umbral de cobertura del 80% — no exigido por `AGENTS.md`; no se impone.

## Ejecución

Seguir [[timeouts-y-procesos]] para ejecución focalizada (`dotnet test --filter
"FullyQualifiedName~NombreDelTest"`, `--no-build` cuando corresponda). No correr la suite completa
salvo pre-merge o pedido explícito.

## Playwright / E2E de navegador

Los tests de comportamiento de navegador no son responsabilidad de esta skill — ver
[[playwright-protocolo]].
