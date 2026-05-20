# UI-5H — Seguridad visual y toasts inline

## A. Objetivo

Normalizar visualmente las vistas parciales de Seguridad que usaban `.toast-msg` con Tailwind inline, migrando el feedback visual a `.alert-erp` según el design system canónico (UI-5B). Sin cambios en lógica funcional de usuarios, roles ni permisos.

## B. Vistas auditadas

| Vista | Ubicación | Rol |
|-------|-----------|-----|
| `Index.cshtml` | `Views/Seguridad/` | Contenedor de tabs (no modificado) |
| `_SeguridadUsuariosTab.cshtml` | `Views/Shared/` | Tab usuarios — **modificado** |
| `_SeguridadRolesTab.cshtml` | `Views/Shared/` | Tab roles — **modificado** |
| `_SeguridadPermisosRolTab.cshtml` | `Views/Shared/` | Tab permisos-rol — **modificado** |
| `_SeguridadAuditoriaTab.cshtml` | `Views/Shared/` | Tab auditoría — sin toasts, no modificado |
| `_SeguridadTabs.cshtml` | `Views/Shared/` | Componente de tabs — no modificado |
| `Auditoria_tw.cshtml` | `Views/Seguridad/` | Vista standalone — fuera de alcance |
| `EditUsuario_tw.cshtml` | `Views/Seguridad/` | Vista edición — fuera de alcance |
| `RolDetails_tw.cshtml` | `Views/Seguridad/` | Vista detalle — fuera de alcance |
| `_*Modal_tw.cshtml` (4 modales) | `Views/Seguridad/` | Modales — fuera de alcance |

## C. Archivos modificados

- `Views/Shared/_SeguridadUsuariosTab.cshtml`
- `Views/Shared/_SeguridadRolesTab.cshtml`
- `Views/Shared/_SeguridadPermisosRolTab.cshtml`
- `docs/ui-5h-seguridad-visual-toasts.md` (este archivo)

## D. Cambios en toasts/alerts

**Patrón anterior (3 parciales, bloques success y error):**
```html
<div class="toast-msg flex items-center gap-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm font-semibold text-emerald-400" role="status">
    <div class="bg-emerald-500/20 text-emerald-500 p-2 rounded-lg">
        <span class="material-symbols-outlined">check_circle</span>
    </div>
    <div>
        <p class="text-emerald-500 text-sm font-bold leading-tight">Operación exitosa</p>
        <p class="text-emerald-500/80 text-xs font-medium">@TempData["Success"]</p>
    </div>
</div>
```

**Patrón nuevo:**
```html
<div class="toast-msg alert-erp alert-erp-success" role="status">
    <span class="material-symbols-outlined">check_circle</span>
    <div>
        <p class="font-bold leading-tight">Operación exitosa</p>
        <p class="text-xs font-medium opacity-80">@TempData["Success"]</p>
    </div>
</div>
```

**Diferencia visual:** El diseño es equivalente. `.alert-erp` provee flex, gap, padding, border y border-radius. `.alert-erp-success/error` provee el color de fondo, texto y borde semántico. Se simplificó el ícono (directo en el flex sin wrapper div). El `opacity-80` preserva el efecto de dimming del texto secundario.

**Bloques migrados:** 6 total (2 por parcial × 3 parciales).

## E. Cambios en botones/acciones

Sin cambios. Los botones ya usaban clases canónicas:
- Acciones de fila: `row-action`, `row-action--primary`, `row-action--danger`, `row-action--warning`
- Acciones bulk: `btn-erp-success`, `btn-erp-secondary`, `btn-erp-danger`, `btn-erp-primary`, `btn-erp-warning`, `btn-erp-ghost`
- Acciones de tab: `btn-erp-primary`, `btn-erp-secondary`

## F. Contratos preservados

- `.toast-msg` mantenido como clase hook para `TheBury.autoDismissToasts()` (shared-ui.js)
- Todos los `id=` preservados (selectAll, searchUsuarios, filterRol, etc.)
- Todos los `data-*` preservados (data-seguridad-*, data-oc-scroll, etc.)
- Todos los `name=` de formularios preservados
- `@Html.AntiForgeryToken()` preservado en todos los formularios
- `asp-*` tag helpers preservados
- `role="status"` y `role="alert"` preservados para ARIA

## G. Accesibilidad

- `role="status"` en toasts de éxito (live region polite implícito)
- `role="alert"` en toasts de error (live region assertive)
- Contraste mejorado: `.alert-erp-success` usa `#34d399` (emerald-400) sobre fondo oscuro
- `.alert-erp-error` usa `#f87171` (red-400) sobre fondo oscuro
- Ícono `material-symbols-outlined` con `flex-shrink: 0` (no se comprime en móvil)

## H. Validaciones

- `dotnet build --configuration Release` → **0 errores, 0 advertencias**
- `git diff --check` → **sin errores de whitespace** en archivos modificados

## I. Tests

- `LayoutUiContractTests` → **57/57 OK**
- Suite `Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard` → **230/230 OK**
- Contrato `.toast-msg` en `shared-ui.js` → **verificado** (`.toast-msg` preservado en markup)

## J. Playwright

- Suite: `e2e/ui-4e-layout-visual.spec.js`
- Resultado: **169/169 passed**
- Duración: ~1m 54s
- App: `TheBuryProyect.exe` PID 25728 — corriendo en `http://localhost:5187`

## K. Cierre de procesos

- **PID 25728** (`TheBuryProyect.exe`): corriendo intencionalmente como servidor para Playwright, externo a la tarea
- **PID 1492** (`playwright cli.js test-server`): proceso del VS Code extension, no iniciado por esta tarea
- **PID 15240** (`dotnet.exe MSBuild.dll`): nodo MSBuild background, benigno
- No se iniciaron ni detuvieron procesos del repo por esta tarea

## L. Riesgos y deudas

### Completado en UI-5H
- ✅ Toasts de feedback en tabs Usuarios, Roles, PermisosRol

### Postergado (fuera de alcance confirmado)
- Vistas `_tw.cshtml` en `Views/Seguridad/` (modales, EditUsuario, RolDetails, Auditoria standalone) — estructuralmente más complejas
- Badges de roles de usuario (inline con Tailwind condicional) — `.badge-erp` aplica `text-transform: uppercase` que cambiaría labels visualmente
- Badges de estado con dot-indicator — patrón no soportado nativamente por `.badge-erp`
- Botones inline de la matriz de permisos (`btn-select-column`, `btn-select-row`) — funcionales, riesgo medio

### Deuda conocida
- `Views/Seguridad/_SeguridadModuleStyles.cshtml` carga `seguridad-module.css` — no auditado en esta fase

## M. Próximo paso recomendado

**UI-5I** — Normalizar vistas `_tw.cshtml` de Seguridad (modales y vistas standalone) que quedaron fuera de alcance de UI-5H. Alternativa: auditar badges de estado y roles en Seguridad para migración segura a `.badge-erp-*` con ajuste de uppercase.
