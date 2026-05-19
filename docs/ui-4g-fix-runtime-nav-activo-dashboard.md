# UI-4G — Fix runtime nav activo Dashboard

## A. Objetivo

Corregir el fallo Playwright donde `.nav-item-active` devolvía count=0 al navegar al Dashboard,
impidiendo que el item "Dashboard" del sidebar se marcara como activo en runtime.

## B. Fallo Playwright detectado

Suite: `ui-4e-layout-visual.spec.js`
Test: `sidebar activo visible en dashboard — corregido UI-4F`

```
Locator .nav-item-active
Expected: 1
Received: 0
```

7 fallos (uno por viewport). 162 tests pasaban. Total pre-fix: 162 passed / 7 failed.

## C. Causa raíz

`HomeController.Index()` hace `return RedirectToAction("Index", "Dashboard")`.

En runtime, cuando Playwright navega a `/`, el controlador real que renderiza la vista es
`DashboardController`. Por lo tanto `ViewContext.RouteData.Values["controller"]` vale `"Dashboard"`,
no `"Home"`.

El nav item usaba `IsActive("Home")`, que compara `currentController == "Home"` → `false` →
nunca se aplicaba `nav-item-active` ni `aria-current="page"`.

## D. Archivos modificados

| Archivo | Cambio |
|---|---|
| `Views/Shared/_Layout.cshtml` | Variable `isDashboardActive` que cubre ambos controladores |
| `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs` | Nuevo test `Layout_NavDashboardActivoConControladorDashboard` |

## E. Cambio aplicado

### `_Layout.cshtml` — bloque `@{ }` principal

```razor
// Dashboard activo tanto en Home como en Dashboard (Home redirige a Dashboard en runtime)
var isDashboardActive = IsActive("Home") || IsActive("Dashboard");
```

### `_Layout.cshtml` — nav item Dashboard

Antes:
```razor
<a class="@NavClass(IsActive("Home"))" aria-current="@NavCurrent(IsActive("Home"))" ...>
    <p class="@NavFont(IsActive("Home")) sidebar-label">Dashboard</p>
```

Después:
```razor
<a class="@NavClass(isDashboardActive)" aria-current="@NavCurrent(isDashboardActive)" ...>
    <p class="@NavFont(isDashboardActive) sidebar-label">Dashboard</p>
```

El enlace sigue apuntando a `asp-controller="Home" asp-action="Index"` (sin cambio de ruta).

## F. Tests

### Nuevo test (UI-4G)

`Layout_NavDashboardActivoConControladorDashboard` — verifica que `_Layout.cshtml` contenga:
- `IsActive("Dashboard")` en la condición combinada
- `isDashboardActive` como variable unificadora

### Test existente mantenido (UI-4F)

`Layout_TieneNavItemDashboard` — sigue pasando:
- `IsActive("Home")` sigue presente en la variable
- `asp-controller="Home" asp-action="Index"` sin cambio

### Resultado total

- `LayoutUiContractTests`: 57/57 OK (56 previos + 1 nuevo)
- Suite relevante: 230/230 OK (229 previos + 1 nuevo)

## G. Playwright

Comando ejecutado:
```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js
```

## H. Validaciones

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release -o tmpbuild_ui4g` | OK (0 errores) |
| `LayoutUiContractTests` 57/57 | OK |
| Suite relevante 230/230 | OK |
| `git diff --check` | OK |
| Playwright 169/169 | OK |

Nota: build con `-o tmpbuild_ui4g` por file-lock en `bin/Release/net8.0/TheBuryProyect.exe`
causado por la app corriendo (PID 47040). No es un error de compilación real.

## I. Riesgos / deudas

- **Ningún riesgo funcional**: solo se cambió la condición de clase CSS activa en el nav.
- **Deuda conocida**: si en el futuro se crea un tercer controlador que también deba marcar
  Dashboard activo, hay que agregar otra condición. Bajo riesgo dado el modelo MVC actual.
- Los tmpbuild_*/ no se commitean.

## J. Próximo paso recomendado

**UI-5A** — siguiente fase del rework visual según roadmap.
El layout global está estabilizado y cubierto con 169 tests Playwright y 230 tests de contrato.
