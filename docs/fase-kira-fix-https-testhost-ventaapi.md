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
Ninguna en este scope. La condición `if (!app.Environment.IsDevelopment())` para HSTS (línea anterior) también se activa en Testing, pero HSTS en TestHost no causa problemas funcionales ya que el middleware solo agrega un header de respuesta sin redirigir.

---

## I. Checklist actualizado

- [x] Worktree kira/fix-https-testhost configurado
- [x] Working tree limpio al inicio
- [x] Build Release passing antes del fix
- [x] Test reproducido con warning HTTPS identificado
- [x] Causa raíz diagnosticada
- [x] Fix mínimo aplicado en Program.cs
- [x] Build Release passing post-fix
- [x] Test específico passing sin warning HTTPS
- [x] Filtro amplio 214/214 passing
- [x] Documentación creada
- [ ] Commit y push pendiente (siguiente paso)
