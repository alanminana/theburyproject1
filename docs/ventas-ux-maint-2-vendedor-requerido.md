# VENTAS-UX-MAINT-2 — Indicador visual de Vendedor requerido en Venta/Create

> ⚠️ **SUPERSEDED (2026-06-27).** El selector/delegación de vendedor fue eliminado: el vendedor
> ahora es SIEMPRE el usuario logueado (fijo, sin dropdown), resuelto por
> `VentaService.ResolverVendedorAsync`. Ya no existe el campo requerido ni su indicador en
> Create/Modal/Edit. Este documento queda como registro histórico.

## A. Objetivo

Agregar indicador visual y accesible de que el campo Vendedor es requerido en el formulario
principal de Venta/Create (Create_tw.cshtml), resolviendo la observación Q1 del smoke manual
VENTAS-UX-SMOKE-1.

---

## B. Deuda tomada desde VENTAS-UX-SMOKE-1

En el smoke manual (commit 8be55c5) se detectó:

> **Q1 — Vendedor requerido sin indicador visual de obligatoriedad.**
> No bloqueante. Conviene resolver antes de retomar Misa/Inventario.

El modal (`_VentaCrearModal.cshtml`) ya tenía el indicador `<span class="vm-required">*</span>`
implementado. El formulario principal (`Create_tw.cshtml`) lo tenía ausente.

---

## C. Archivos auditados

- `Views/Venta/Create_tw.cshtml`
- `Views/Venta/_VentaCrearModal.cshtml`
- `wwwroot/css/venta-module.css` (solo lectura para verificar patrón vm-required)
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

---

## D. Hallazgo

### Create_tw.cshtml — rama `puedeDelegarVendedor`

| Elemento | Estado anterior |
|---|---|
| Label "Vendedor" | Sin atributo `for`. Sin indicador de requerido |
| Select `VendedorUserId` | Sin `aria-required` |

### _VentaCrearModal.cshtml — rama `puedeDelegarVendedor`

| Elemento | Estado anterior |
|---|---|
| Label `for="VendedorUserId"` | Correcto |
| `<span class="vm-required">*</span>` | Presente |
| Select `aria-required` | No tenía, no se tocó |

El modal ya estaba correcto. No fue modificado.

La rama `else` de ambos archivos (muestra usuario actual estático) no es un campo de formulario
y no requiere indicador.

---

## E. Cambios aplicados

### `Views/Venta/Create_tw.cshtml`

**Antes (línea 748):**
```html
<label class="block text-xs font-bold text-slate-500 uppercase mb-2">Vendedor</label>
<select asp-for="VendedorUserId" asp-items="vendedores"
        class="venta-input text-sm">
```

**Después:**
```html
<label class="block text-xs font-bold text-slate-500 uppercase mb-2" for="VendedorUserId">
    Vendedor <span class="text-red-400" aria-hidden="true">*</span><span class="sr-only">obligatorio</span>
</label>
<select asp-for="VendedorUserId" asp-items="vendedores"
        class="venta-input text-sm" aria-required="true">
```

Cambios aplicados:
1. `for="VendedorUserId"` en el label — enlaza accesiblemente el label con el select.
2. `<span class="text-red-400" aria-hidden="true">*</span>` — indicador visual rojo visible.
3. `<span class="sr-only">obligatorio</span>` — texto accesible para lectores de pantalla.
4. `aria-required="true"` en el select — señal semántica de campo requerido para tecnologías asistivas.

### `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

Tests agregados (sección VENTAS-UX-MAINT-2):

- `CreateView_LabelVendedorTieneFor` — verifica `for="VendedorUserId"` y `asp-for="VendedorUserId"`.
- `CreateView_LabelVendedorTieneIndicadorRequerido` — verifica `aria-hidden="true">*</span>` y `sr-only`.
- `CreateView_SelectVendedorTieneAriaRequired` — verifica `aria-required="true"`.

---

## F. Contratos preservados

- `asp-for="VendedorUserId"` — sin cambios.
- `asp-items="vendedores"` — sin cambios.
- `class="venta-input text-sm"` — sin cambios.
- `<option value="">Seleccione vendedor...</option>` — sin cambios.
- Contratos `id`, `name`, eventos JS — sin cambios.
- Estructura HTML de la rama `else` (display estático) — sin cambios.

---

## G. Qué no se tocó

- `_VentaCrearModal.cshtml` (ya correcto, sin modificación).
- `wwwroot/js/venta-create.js`.
- `wwwroot/css/venta-module.css`.
- `wwwroot/css/shared-components.css`.
- Controllers, Services, Models, ViewModels, DTOs.
- Migraciones, endpoints, payloads, reglas de negocio.
- Cotización, Inventario, Misa, stock, caja, crédito.
- `AGENTS.md`, `CLAUDE.md`, `.claude/settings.local.json`, `skills-lock.json`.

---

## H. Accesibilidad

| Técnica | Implementación |
|---|---|
| Enlace label/control | `for="VendedorUserId"` en label |
| Indicador visible | `*` en rojo (`text-red-400`) |
| No depende solo de color | Texto `sr-only` "obligatorio" para lectores de pantalla |
| Semántica ARIA | `aria-required="true"` en el select |
| `aria-hidden="true"` en el asterisco | Evita que el lector lea "asterisco" redundantemente |

Patrón compatible con WCAG 1.3.1 (Info and Relationships) y 1.4.1 (Use of Color).

---

## I. Validaciones

### Build
```
Compilación correcta. 0 Advertencias. 0 Errores.
```

### Tests VentaCreate
```
Correctas! — Con error: 0, Superado: 100, Omitido: 0, Total: 100
```
(Anterior referencia: 97/97. Ahora 100/100 con 3 tests nuevos.)

### git diff --check
Warnings solo en `AGENTS.md` y `CLAUDE.md` (preexistentes, no commiteados). Archivos de esta fase limpios.

### Playwright
No ejecutado. Cambio es indicador de label (texto + asterisco + sr-only). No altera flujo funcional, layout general ni interacciones JS.

---

## J. Riesgo funcional

**Bajo.** Solo se agregó un atributo `for`, un `span` visual y `aria-required`. No se modificó lógica, validación backend, payload ni endpoint. El comportamiento del formulario es idéntico.

---

## K. Próximo paso recomendado

Retomar Misa/Inventario según el orden de fases planificado.

Si se desea extender accesibilidad de requerido: revisar si otros campos obligatorios en
Create_tw.cshtml (ClienteId, TipoPago) también deberían tener indicador visual de requerido.
Eso sería una nueva microfase separada.
