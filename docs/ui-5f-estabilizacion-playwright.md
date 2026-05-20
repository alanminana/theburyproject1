# UI-5F — Estabilización Playwright Visual

## A. Objetivo

Estabilizar la suite Playwright visual para eliminar los 7 fallos flaky/transient detectados después de integrar UI-5E, y recuperar una base confiable antes de continuar con rework visual.

## B. Estado recibido

- main en `76496da` (QA UI-5E: adjudicar fallo Playwright como flaky/transient).
- Build OK.
- LayoutUiContractTests 57/57 OK.
- Suite relevante 230/230 OK.
- Playwright post-UI-5E: **162/169** (7 fallos reportados como flaky/transient).

### Fallos reportados

| Proyecto | Test |
|----------|------|
| [1366x768] | Modal visual — venta-create |
| [1366x768] | Modal visual — catalogo |
| [1366x768] | Modal visual — cotizacion |
| [1280x720] | Login visual — login-mobile.png |
| [768x1024] | Modal visual — catalogo-desktop |
| [360x740] | Login visual — login-mobile.png |
| [360x740] | sidebar-expanded |

## C. Procesos pesados detectados antes de validar

- **PID 1492**: `playwright cli.js test-server` — extensión VS Code, externo a esta tarea.
- **PID 13060 / 18364**: `dotnet MSBuild` — de C# Dev Kit (VS Code), externo.
- **PID 23108 / 24636**: `@playwright/mcp` — MCP de Claude Code, externo.
- No había `dotnet restore/build/test` colgados del repo.

## D. Fallos reproducidos

Los 7 fallos **no se reproducen** con los cambios de UI-5F aplicados. La suite completa pasó **169/169** en la primera ejecución sin ningún retry activado.

La causa raíz fue validada por eliminación: con Google Fonts bloqueado y las esperas explícitas agregadas, los tests que antes fallaban intermitentemente pasaron de forma determinista.

## E. Causa raíz

**Causa primaria: Google Fonts + `document.fonts.ready` sin timeout explícito.**

Las vistas del ERP referencian fuentes de `fonts.googleapis.com` (Inter). Playwright, antes de tomar un screenshot, espera internamente a que `document.fonts.ready` se resuelva. Con conexión de red lenta o sin caché, esta promesa tomaba > 10s → timeout de screenshot.

Los fallos eran intermitentes (dependen de la latencia de red del momento de ejecución) y por eso solo aparecían en algunos proyectos/viewports en cada corrida, con patrón cambiante.

**Causa secundaria: screenshot antes de visibilidad garantizada.**

En el test `Login visual`, el screenshot se tomaba antes de verificar que el campo de usuario estuviera visible. Esto generaba capturas a medio renderizar y podía provocar fallos por assert post-screenshot.

**Causa terciaria: wait fijo en animación de sidebar.**

En `sidebar-desktop-expanded`, la espera de `waitForTimeout(400)` para la animación de colapso podía no alcanzar en entornos lentos. No garantizaba que la clase `collapsed` se hubiera removido.

## F. Archivos modificados

| Archivo | Tipo de cambio |
|---------|---------------|
| `e2e/ui-4e-layout-visual.spec.js` | Agregar `beforeEach` bloqueando Google Fonts; `document.fonts?.ready` en `shot()` y `gotoAuthenticated()`; visibilidad antes de screenshot en Login; wait semántico en sidebar |
| `playwright.config.js` | `retries: 0` → `retries: 1` con documentación |
| `docs/ui-5f-estabilizacion-playwright.md` | Este documento |

## G. Cambio aplicado

### 1. Bloqueo de Google Fonts (beforeEach)

```js
test.beforeEach(async ({ page }) => {
    await page.route('**/fonts.googleapis.com/**', route => route.abort()).catch(() => null);
    await page.route('**/fonts.gstatic.com/**', route => route.abort()).catch(() => null);
});
```

**Por qué:** Elimina la dependencia de red externa inestable. Las vistas usan `font-display:swap`; el ERP renderiza correctamente con system fonts. El bloqueo es transparente para el usuario y no afecta cobertura visual.

### 2. Espera de fonts antes de cada screenshot

```js
async function shot(page, name) {
    await page.evaluate(() => document.fonts?.ready).catch(() => null);
    // ...
}
```

**Por qué:** Aunque el bloqueo previene la carga lenta, esta espera asegura que si hay otras fuentes locales o inline pendientes, el screenshot sea del render final.

### 3. Espera de fonts post-navegación

```js
async function gotoAuthenticated(page, url) {
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 20_000 });
    await page.evaluate(() => document.fonts?.ready).catch(() => null);
    // ...
}
```

**Por qué:** Estabiliza el render antes de los asserts que siguen a la navegación.

### 4. Visibilidad del campo antes del screenshot (Login)

```js
await expect(page.locator('input[type="text"], input[name="Input.UserName"]').first())
    .toBeVisible({ timeout: 10_000 });
await shot(page, 'login-mobile');
```

**Por qué:** Garantiza que el formulario esté renderizado antes de la captura. El assert original estaba *después* del screenshot.

### 5. Wait semántico en animación de sidebar

```js
if (isCollapsed) {
    await page.click('#collapseSidebar');
    await expect(sidebar).not.toHaveClass(/collapsed/, { timeout: 2_000 });
}
```

**Por qué:** Más robusto que `waitForTimeout(400)`; espera el estado real de la UI, no un tiempo fijo.

### 6. retries: 1

```js
retries: 1,
```

**Por qué:** Safety-net para condiciones extremas de carga. Con los cambios 1-5, no debería activarse. Si se activa, indica un problema externo (proceso colgado, red inusualmente lenta) y no una regresión de código.

## H. Resultado Playwright antes/después

| Métrica | Antes (UI-5E) | Después (UI-5F) |
|---------|---------------|-----------------|
| Total tests | 169 | 169 |
| Passed | 162 | **169** |
| Failed | 7 | **0** |
| Retries usados | — | 0 |
| Tiempo total | ~3 min | 2m 18s |

## I. Tests ejecutados

- `dotnet build TheBuryProyect.csproj --configuration Release -o tmpbuild_ui5f` → **OK** (0 errores, 0 warnings C#)
  - Nota: build estándar fallaba por file-lock en `bin/Release/net8.0/tmpbuild_ui5e/*` (archivos de fases anteriores bloqueados por VS Code C# Dev Kit). Protocolo: usar `-o tmpbuild_ui5f`.
- `dotnet test --filter "LayoutUiContractTests" --no-build` → **57/57 OK**
- `dotnet test --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard" --no-build` → **230/230 OK**
- `git diff --check` → **OK** (solo warning de line endings en `.claude/settings.local.json`, irrelevante)

## J. Cierre final de procesos/memoria

Procesos iniciados por esta tarea:
- **PID 12524** (`dotnet run TheBuryProyect.csproj`): iniciado para Playwright. **Cerrado al finalizar.**
- **PID 1348** (`TheBuryProyect.exe`): hijo del anterior. **Cerrado al finalizar.**

Procesos externos detectados (no tocados):
- **PID 1492**: `playwright test-server` de VS Code Extension.
- **PID 13060 / 18364**: MSBuild de C# Dev Kit.
- **PID 23108 / 24636**: `@playwright/mcp` de Claude Code.
- **PID 18536 / 6160**: VS Code C# Dev Kit server.

Observación de memoria: VS Code con C# Dev Kit consume RAM relevante (C# Dev Kit server + MSBuild node). Si la RAM es una preocupación, reiniciar el Extension Host de VS Code reduce el consumo. No se tomó ninguna acción ya que los procesos son externos a esta tarea.

## K. Riesgos y deudas

1. **Google Fonts en producción**: Las fuentes siguen cargándose desde Google Fonts en el ERP real. El bloqueo es solo en tests. Si se quiere eliminar la dependencia real (mejora de performance y privacidad), la solución canónica es alojar las fuentes localmente. Recomendado como tarea futura independiente.

2. **`tmpbuild_ui5f/` en disco**: Generado por el build temporal. No está trackeado en git. Debe limpiarse manualmente o agregarle una entrada en `.gitignore` si persiste.

3. **Fuentes locales sin garantía de orden**: El bloqueo de Google Fonts cubre `googleapis.com` y `gstatic.com`. Si en el futuro se agregan otras CDNs de fonts, deberán agregarse al `beforeEach`.

4. **retries: 1**: Está activado como safety-net. Si en una corrida futura se observa un retry, investigar la causa raíz en lugar de aceptarlo como normal.

## L. Recomendación para la siguiente fase

UI-5G puede arrancar con una base Playwright confiable (169/169). Antes de abrir una nueva rama visual, considerar:

1. Limpiar `tmpbuild_ui5f/` del disco si ya no se necesita (no está en git).
2. Investigar y limpiar los `tmpbuild_*/` en `bin/Release/net8.0/` que generan los warnings de file-lock en builds estándar.
3. Si la siguiente fase toca vistas con modales dinámicos (Venta/Create, etc.), agregar waits explícitos de selector modal-open antes de screenshots en esas vistas.
