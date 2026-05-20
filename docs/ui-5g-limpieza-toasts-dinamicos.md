# UI-5G — Limpieza de temporales y normalización de feedback dinámico

## A. Objetivo

Cerrar dos deudas operativas menores antes de avanzar a nuevos módulos:

1. Verificar y limpiar temporales `tmpbuild_*` / `tmptest_*` en `bin/Release`.
2. Auditar `showPageFeedback` y toasts dinámicos.
3. Normalizar el feedback dinámico de caja para usar `.alert-erp` en lugar de Tailwind inline.
4. Mantener Playwright 169/169.
5. Verificar cierre limpio de procesos al terminar.

## B. Temporales detectados

- **Ningún** `tmpbuild_*` ni `tmptest_*` encontrado en `bin/Release/net8.0/`.
- Sin archivos temporales no trackeados en el árbol de trabajo.
- `.gitignore` ya cubre los patrones: `tmpbuild*/`, `theburyproject1tmpbuild*/`, `tmptest*/`.

## C. Limpieza aplicada

- No hubo limpieza de archivos necesaria (no existían temporales).
- `.gitignore` confirmado completo; no se modificó.

## D. showPageFeedback auditado

- **Ubicación única:** `wwwroot/js/caja-index.js:27`.
- **Comportamiento previo:** creaba un elemento dinámico con `toast-msg` + clases Tailwind inline
  (`bg-rose-500/10 border-rose-500/20 text-rose-500`, etc.) y un `variants` object con wrapper, iconBg, title e icon.
  La estructura interna tenía un icono envuelto en un div coloreado + dos párrafos (título + mensaje).
- **`.toast-msg` como hook JS:** confirmado en `shared-ui.js:26` (`querySelectorAll('.toast-msg, [id^="toast-"]')`
  para auto-dismiss). Test `LayoutUiContractTests.cs:264` valida su presencia. **Hook preservado en el cambio.**
- **Vistas estáticas con `.toast-msg + Tailwind` inline (~30 vistas):** auditadas, fuera de scope de UI-5G.
  Están documentadas como deuda en UI-5B. Se posterga su migración módulo a módulo.

### JS dinámicos auditados (no migrados en UI-5G)

| Archivo | Clase dinámica | Decisión |
|---|---|---|
| `venta-create.js:199` | `toast-msg flex items-center ... ${variant.wrapper}` | Postergar — forma parte de flujo Venta/Create (alcance prohibido) |
| `catalogo-index.js:272` | `toast-msg fixed bottom-4 right-4 ...` | Postergar — toast flotante con posición fija, requiere evaluación específica |
| `proveedor-index.js:32` | `toast-msg fixed bottom-4 right-4 ...` | Postergar — idem catalogo |

## E. Cambios aplicados

**`wwwroot/js/caja-index.js`** — función `showPageFeedback` (línea 27):

- Eliminado el objeto `variants` con clases Tailwind inline (wrapper, iconBg, title).
- Eliminado el uso de `innerHTML` con estructura HTML compleja.
- Reemplazado por `typeMap` que mapea tipo → `{cls, icon, role}`.
- Clase del toast: `toast-msg alert-erp alert-erp-{type}` (canónico, sin Tailwind inline).
- Estructura interna: `<span class="material-symbols-outlined">` + texto plano, usando `createElement` y
  `createTextNode` (sin `innerHTML`; eliminado XSS surface).
- `role` diferenciado: `status` para success, `alert` para error/warning.

## F. Contratos preservados

- `.toast-msg` presente en el elemento creado → hook JS auto-dismiss preservado.
- `TheBury.autoDismissToasts()` sigue siendo llamado post-insert.
- `feedbackSlot.replaceChildren(toast)` sin cambios.
- `escapeHtml` removido del markup inline (reemplazado por `textContent`/`createTextNode`, más seguro).
- Comportamiento funcional idéntico: el toast aparece en `#caja-index-feedback-slot` y desaparece a los 5s.

## G. Validaciones

- `git diff --check`: OK (sin trailing whitespace ni conflictos).
- `git status --short`: solo `.claude/settings.local.json` (no commiteado) y `wwwroot/js/caja-index.js`.

## H. Tests ejecutados

| Suite | Resultado |
|---|---|
| `LayoutUiContractTests` | 57/57 OK |
| `Layout\|Shared\|Navigation\|...\|Dashboard` | 230/230 OK |

## I. Playwright ejecutado

- Comando: `npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js`
- Variables: `E2E_USER=Admin`, `E2E_PASS=Admin123!`, `ASPNETCORE_ENVIRONMENT=Development`
- **Resultado: 169 passed / 0 failed** (1.9 min)

## J. Cierre de procesos

| PID | Proceso | Estado |
|---|---|---|
| 7628 | `dotnet run` (lanzado para Playwright) | Detenido al finalizar |
| 26528 | `TheBuryProyect.exe` (hijo del run) | Detenido al finalizar |
| MCP servers (playwright/context7) | Servidores externos de Claude | Corriendo — no son del repo |
| VS Code y extensiones | Herramienta de desarrollo | Corriendo — esperados |

## K. Riesgos / deudas remanentes

| Ítem | Riesgo | Estado |
|---|---|---|
| `venta-create.js` toast dinámico | Bajo — patrón similar, alcance prohibido en UI-5G | Deuda documentada |
| `catalogo-index.js` toast flotante | Bajo — usa `fixed bottom-4`, distinto al feedback inline | Deuda documentada |
| `proveedor-index.js` toast flotante | Bajo — idem catalogo | Deuda documentada |
| ~30 vistas con `.toast-msg + Tailwind` estático | Muy bajo — solo visual, sin JS propio | Deuda UI-5B preexistente |

## L. Próximo paso recomendado

**UI-5H:** Normalizar vistas con `.toast-msg + Tailwind inline` en módulos de Seguridad
(`_SeguridadUsuariosTab`, `_SeguridadRolesTab`, `_SeguridadPermisosRolTab`) como micro-lote visual
contenido, sin tocar lógica de negocio. O bien avanzar al siguiente módulo funcional del rework visual.
