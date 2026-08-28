---
name: dotnet-patterns
description: Referencia consultiva de patrones idiomáticos C# / .NET 8 / ASP.NET Core / EF Core para TheBuryProject. Consultiva manual (`user-invocable-only`), invocar con `/dotnet-patterns`. No reemplaza la arquitectura real del ERP ni justifica por sí sola un refactor.
metadata:
  origin: adaptado de affaan-m/ECC (skills/dotnet-patterns/SKILL.md)
  upstream-commit: 5eddf1a3ffd311423be2d4ba7d26f7209c91b033
  upstream-date: 2026-08-27
---

# Patrones .NET (consultiva manual)

Referencia de patrones idiomáticos de C# / .NET para este ERP. Es consultiva: no se invoca
automáticamente (ver `.claude/settings.json` → `skillOverrides`). Se usa solo con `/dotnet-patterns`.

## Precedencia

Esta skill nunca es la autoridad. Por encima de ella, en orden:

1. código real del proyecto;
2. `AGENTS.md` y `CLAUDE.md`;
3. skills propias del proyecto (`modulo-ui-refactor`, protocolos, etc.);
4. esta referencia.

Ninguna recomendación de acá justifica por sí sola refactorizar código existente, introducir una
capa nueva o cambiar una convención ya consolidada. El agente `csharp-reviewer` y la skill
`csharp-testing` comparten esta misma regla de precedencia.

## Arquitectura real del ERP — no proponer alternativas sin pedido explícito

Antes de sugerir un patrón, confirmar que no contradice lo que ya existe:

- Los `Services/*Service.cs` acceden a `AppDbContext` directamente; **no hay capa de Repository**.
  No introducirla a partir de esta skill.
- Las interfaces `IXxxService` en `Services/Interfaces/` ya cubren "depender de abstracciones".
- `Nullable` e `ImplicitUsings` ya están en `enable` a nivel de proyecto
  (`TheBuryProyect.csproj`).
- Los `record` ya se usan como convención para DTOs de resultado/request en `Services/Models/*`.
  Es la convención existente, no algo a imponer donde no encaja (p. ej. no convertir entidades EF
  en records).
- `CancellationToken` ya se usa de forma extensa (services de integración con MercadoLibre,
  background services). Sumarlo donde falte es coherente con lo existente, no una regla nueva.
- El patrón `Options` (`IOptions<T>` + `services.Configure<T>`) ya está en uso puntual
  (MercadoLibre, Identity).
- Hay middleware propio real: `Middleware/AuditMiddleware.cs`,
  `Middleware/TerminosCondicionesMiddleware.cs`.
- El proyecto es MVC con Controllers + Razor Views, **no Minimal API** — no se incluyen ejemplos
  de Minimal API porque no aplican a esta arquitectura.

## Qué conservar de ECC (adaptado con el contexto real)

### Async/await correcto

```csharp
// Bien: async de punta a punta, con CancellationToken
public async Task<Cliente?> BuscarPorIdAsync(int id, CancellationToken cancellationToken)
    => await _context.Clientes
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

// Mal: bloquear sobre código async (riesgo de deadlock)
public Cliente? BuscarPorId(int id)
    => _context.Clientes.FirstOrDefaultAsync(c => c.Id == id).Result;
```

### CancellationToken cuando corresponda

Propagar el `CancellationToken` recibido hacia las llamadas async internas (EF Core, HttpClient,
otros services). No agregarlo en firmas que no lo necesitan (métodos puramente síncronos o de
cómputo in-memory).

### Dependency Injection por constructor

Ya es la convención de todo `Services/*.cs`: constructor injection de interfaces, sin `new
Service()` dentro de otro service o controller.

### Nullability explícita

El proyecto ya compila con `Nullable=enable`. Evitar silenciar warnings con `!` sin justificar por
qué el valor no puede ser null en ese punto; si la justificación no es obvia, dejar un comentario
breve o preferir un guard clause.

### EF Core: `AsNoTracking` en lecturas, evitar N+1

Patrón real del proyecto (no repositorio, service directo sobre `AppDbContext`):

```csharp
public async Task<List<Venta>> GetVentasClienteAsync(int clienteId, CancellationToken ct)
    => await _context.Ventas
        .Where(v => v.ClienteId == clienteId)
        .Include(v => v.Detalles)
        .AsNoTracking()
        .ToListAsync(ct);
```

`AsNoTracking` ya se usa en más de 450 lugares del proyecto — extenderlo a lecturas nuevas es
coherente con la convención existente. Usar `Include`/`ThenInclude` para evitar N+1 quando el
consumo posterior itera sobre una navegación.

### Options pattern para configuración tipada

```csharp
public sealed class MercadoLibreOptions
{
    public const string SectionName = "MercadoLibre";
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}

builder.Services.Configure<MercadoLibreOptions>(
    builder.Configuration.GetSection(MercadoLibreOptions.SectionName));
```

Usar solo cuando la configuración tenga varias claves relacionadas y vida propia; no envolver un
único valor suelto en una clase de opciones sin necesidad.

### Middleware

El proyecto ya tiene middleware custom (`AuditMiddleware`, `TerminosCondicionesMiddleware`). Seguir
esa misma forma (clase con `RequestDelegate _next` + `InvokeAsync(HttpContext)`) si se necesita uno
nuevo; no es una sugerencia nueva, es alinearse con lo existente.

### Guard clauses

```csharp
public async Task<ProcessResult> ProcesarPagoAsync(PagoRequest request, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(request);
    if (request.Monto <= 0)
        throw new ArgumentOutOfRangeException(nameof(request.Monto), "El monto debe ser positivo");

    // happy path sin anidar
    return await _gateway.CobrarAsync(request, ct);
}
```

### Antipatrones a evitar (filtrado)

| Antipatrón | Alternativa |
|---|---|
| `async void` | Devolver `Task` (salvo handlers de evento) |
| `.Result` / `.Wait()` | `await` |
| `catch (Exception) { }` vacío | Manejar o relanzar con contexto |
| `new Service()` dentro de otro componente | Inyección por constructor |
| Campos `public` | Propiedades con el modificador que corresponda |
| `dynamic` en lógica de negocio | Tipos genéricos o explícitos |
| Estado `static` mutable compartido | DI con el scope correcto o `ConcurrentDictionary` |
| Concatenar strings en loops | `StringBuilder` o `string.Join` |

## Qué se descartó de ECC y por qué

- **"Prefer Immutability" como principio obligatorio** (records/init-only en todo) — el ERP no lo
  impone; se usa donde ya es la convención (DTOs de resultado), no en entidades EF ni de forma
  general.
- **"Result Pattern" (`Result<T>` genérico)** — no existe en el proyecto; no se introduce sin
  pedido explícito.
- **"Repository Pattern con EF Core"** — arquitectónicamente incompatible: los services ya acceden
  a `AppDbContext` directamente.
- **Ejemplos de Minimal API** — el ERP es MVC con Controllers, no aplica.
- **`sealed` como regla obligatoria** — no se trata como defecto automático (ver agente
  `csharp-reviewer`).

## Cuándo usar Context7

Antes de aplicar una recomendación que dependa de comportamiento específico de la versión instalada
(.NET 8, ASP.NET Core 8, EF Core 8.0.11 — ver `TheBuryProyect.csproj`), confirmar con Context7
según el protocolo del proyecto en vez de asumir por esta skill.
