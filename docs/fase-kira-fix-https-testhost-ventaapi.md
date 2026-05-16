# Fase Kira — Fix HTTPS TestHost VentaApiController

**Agente:** Kira  
**Rama:** `kira/fix-https-testhost`  
**Fecha:** 2026-05-15

---

## A. Objetivo

Diagnosticar y corregir el test preexistente `VentaApiController_ConfiguracionPagosGlobal_RutaHttpRespondeOkConListaVacia` que era reportado como fallando por un problema relacionado con HTTPS/TestHost.

---

## B. Error reproducido

Al ejecutar `dotnet test --filter "VentaApiController_ConfiguracionPagosGlobal"`:

```
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
      Failed to determine the https port for redirect.
```

El test pasaba, pero por razón frágil: el middleware de HTTPS redirection no podía determinar el puerto HTTPS en el TestHost, por lo que emitía un warning y continuaba sin redirigir. El test obtenía HTTP 200 por fallo silencioso del middleware, no por comportamiento explícitamente correcto.

**Escenario de fallo real**: si alguien configuraba `ASPNETCORE_HTTPS_PORT` en el entorno de test o agregaba HTTPS al TestHost, el middleware redirigiría. El cliente (con `AllowAutoRedirect = false`) recibiría HTTP 307 y el test fallaría.

---

## C. Causa raíz

En `Program.cs`, la condición para `UseHttpsRedirection()` era:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
```

El entorno "Testing" (configurado en `CustomWebApplicationFactory` vía `builder.UseEnvironment("Testing")`) no es "Development", por lo que el middleware se activaba en tests. El TestHost no tiene HTTPS configurado → el middleware no puede resolver el puerto → fallo silencioso → warning en consola.

---

## D. Solución aplicada

Agregar la exclusión del entorno "Testing" a la condición de `UseHttpsRedirection()`, siguiendo el patrón ya establecido en la línea del DbInitializer (`!app.Environment.IsEnvironment("Testing")`):

```csharp
// Antes
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Después
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
```

El cambio es mínimo, explícito y no afecta producción ni desarrollo. Alinea el comportamiento de HTTPS con el patrón ya usado para DbInitializer.

---

## E. Archivos modificados

| Archivo | Cambio |
|---|---|
| `Program.cs` | Agregar `&& !app.Environment.IsEnvironment("Testing")` a condición de `UseHttpsRedirection()` |

Archivos **no modificados**:
- `TheBuryProyect.Tests/Integration/VentaApiControllerConfiguracionPagosGlobalTests.cs`
- `TheBuryProyect.Tests/CustomWebApplicationFactory.cs`
- `TheBuryProyect.Tests/Infrastructure/TestAuthHandler.cs`
- `Controllers/VentaApiController.cs`

---

## F. Tests ejecutados

| Filtro | Resultado |
|---|---|
| `VentaApiController_ConfiguracionPagosGlobal` | 1/1 ✓ — sin warning HTTPS |
| `VentaApiController\|ConfiguracionPago\|Seguridad\|Permiso` | 214/214 ✓ |

Warning HTTPS eliminado post-fix. Build limpio en Release.

---

## G. Qué NO se tocó

- Reglas de negocio de Venta
- VentaService
- Cotización / worktree de Carlos
- Devoluciones / cambios de Juan
- ProductoUnidad, Caja, Factura, Stock
- UI / vistas Razor
- Lógica del test (el test estaba bien, el problema era la infra)
- CustomWebApplicationFactory (ya tenía `AllowAutoRedirect = false`, correcto)

---

## H. Riesgos y deuda

**Riesgo bajo:**  
El cambio solo afecta el entorno "Testing". Producción y Development no se ven afectados.

**Deuda remanente:**  
Ninguna. Con la limpieza HSTS (sección J), el middleware HTTP queda correctamente configurado para los tres entornos.

---

## J. Limpieza posterior — HSTS también excluye Testing

**Fecha:** 2026-05-16  
**Commit:** `Excluir HSTS en entorno Testing`

### Motivo

La condición original de HSTS era:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
```

El entorno "Testing" no es "Development", por lo tanto HSTS se activaba en TestHost. El middleware `UseHsts()` agrega el header `Strict-Transport-Security` a las respuestas — comportamiento de producción que no tiene sentido en un entorno de integración que no usa HTTPS real.

### Cambio aplicado

Se separó `UseExceptionHandler` y `UseHsts` en bloques independientes, aplicando el mismo patrón que `UseHttpsRedirection`:

```csharp
// Antes
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Después
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}
```

### Comportamiento por entorno

| Entorno | UseExceptionHandler | UseHsts | UseHttpsRedirection |
|---|---|---|---|
| Production | activo | activo | activo |
| Development | no | no | no |
| Testing | activo | **no** | no |

### Riesgo

Bajo. `UseHsts()` solo agrega un header HTTP. Su ausencia en Testing no afecta rutas, autenticación, lógica de negocio ni ningún test existente.

### Tests ejecutados post-cambio

| Filtro | Resultado |
|---|---|
| `VentaApiController_ConfiguracionPagosGlobal` | 1/1 ✓ |
| `VentaApiController\|ConfiguracionPago\|Seguridad\|Permiso` | 214/214 ✓ |

Build Release: 0 errores, 0 advertencias.  
`git diff --check`: limpio.

### Nota sobre tests HSTS

No existe infraestructura simple para validar ausencia del header `Strict-Transport-Security` en las respuestas del TestHost. La validación se considera cubierta por:
- lectura directa de la condición en Program.cs
- build limpio en Release
- regresión completa 214/214

---

## I. Checklist actualizado

- [x] Worktree kira/fix-https-testhost configurado
- [x] Working tree limpio al inicio
- [x] Build Release passing antes del fix HTTPS
- [x] Test reproducido con warning HTTPS identificado
- [x] Causa raíz diagnosticada
- [x] Fix UseHttpsRedirection aplicado en Program.cs
- [x] Build Release passing post-fix HTTPS
- [x] Test específico passing sin warning HTTPS (1/1)
- [x] Filtro amplio 214/214 passing
- [x] Limpieza HSTS aplicada — UseHsts excluye Testing
- [x] Build Release passing post-limpieza HSTS
- [x] Test específico 1/1 post-limpieza HSTS
- [x] Filtro amplio 214/214 post-limpieza HSTS
- [x] git diff --check limpio
- [x] Documentación actualizada
- [ ] Commit y push pendiente (siguiente paso)
