# UI-5A — Normalización global de íconos

**Rama:** `kira/ui-5a-normalizacion-global-iconos`  
**Base:** `main` @ `6dde9ef`  
**Fecha:** 2026-05-19  
**Agente:** Kira UI-5A

---

## A. Objetivo

Auditar y normalizar el sistema de íconos global del ERP.  
Corregir íconos mal posicionados, tamaños inconsistentes y desalineación vertical/horizontal observados en Login, Layout, Sidebar, Header, Dashboard y componentes compartidos, sin tocar lógica de negocio ni rediseñar módulos.

---

## B. Problema detectado

Los íconos generales del ERP carecían de una regla base que garantizara:
- `line-height: 1` → el box de línea agregaba espacio vertical innecesario
- `vertical-align: middle` → íconos no centrados respecto al texto en contextos no-flex
- `flex-shrink: 0` → íconos que podían comprimirse en layouts ajustados
- `user-select: none` → texto seleccionable de íconos (los ligatures de font)

Adicionalmente:
- Los botones del header (notifications, search, assignment, help) no tenían `font-size` explícito, heredando los 16px del browser en vez de los 24px esperados para iconos de header.
- El ícono `logout` usaba `text-base` (16px) mientras todos los demás íconos del sidebar usan `text-lg` (18px).
- El ícono de checkbox `check` en Login usaba `absolute left-[3px]` — offset manual frágil.
- El ícono `assignment` del header y `bug_report` del FAB no tenían `aria-hidden="true"` siendo decorativos.
- `account_circle` no tenía `shrink-0`.

---

## C. Archivos revisados

- `Areas/Identity/Pages/Account/Login.cshtml` ✓
- `Views/Shared/_Layout.cshtml` ✓
- `Views/Dashboard/Index.cshtml` (revisado, sin cambios — tamaños intencionales)
- `wwwroot/css/layout.css` (revisado, sin cambios necesarios)
- `wwwroot/css/shared-components.css` ✓
- `wwwroot/css/tailwind-input.css` ✓
- `wwwroot/css/standalone-tokens.css` ✓
- `.gitignore` ✓
- `TheBuryProyect.Tests/Unit/LayoutUiContractTests.cs` (revisado — no testea clases de íconos)
- `e2e/ui-4e-layout-visual.spec.js` (revisado — no testea clases de íconos)

---

## D. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `wwwroot/css/tailwind-input.css` | Extender regla base `.material-symbols-outlined` (futura compilación) |
| `wwwroot/css/shared-components.css` | Nueva sección: base + clases semánticas de íconos |
| `wwwroot/css/standalone-tokens.css` | Agregar normalización base para páginas standalone |
| `Areas/Identity/Pages/Account/Login.cshtml` | Eliminar inline style redundante; fix checkbox check |
| `Views/Shared/_Layout.cshtml` | Header icons: agregar `icon-erp-lg`; logout: `icon-erp`; fixes aria-hidden |
| `.gitignore` | Agregar patrones `tmpbuild*/` y `theburyproject1tmpbuild*/` |

---

## E. Tipos de íconos encontrados

**Sistema único:** Material Symbols Outlined (Google Fonts variable font).  
No se encontraron SVG inline, Font Awesome, Bootstrap Icons ni ninguna otra librería de íconos.

Variaciones encontradas:
- `font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24` — ya en tailwind-input.css y Login inline
- Tamaños usados antes de UI-5A: `text-sm`(14px), `text-base`(16px), `text-lg`(18px), `text-xl`(20px), `text-2xl`(24px), `text-[14px]`, `text-[18px]`, `text-[22px]`, `icon-size-22`(22px)

---

## F. Clases y patrones definidos

### Regla base global (en shared-components.css y standalone-tokens.css)

```css
.material-symbols-outlined {
    line-height: 1;
    vertical-align: middle;
    flex-shrink: 0;
    user-select: none;
}
```

### Tamaños semánticos del ERP

```css
.icon-erp-sm { font-size: 16px; }   /* labels inline, checkboxes, indicadores pequeños */
.icon-erp    { font-size: 18px; }   /* default ERP: nav, inputs, botones canónicos */
.icon-erp-md { font-size: 20px; }   /* cards header, acciones destacadas */
.icon-erp-lg { font-size: 24px; }   /* header, quick-actions, elementos prominentes */
```

### Roles de uso

```css
.icon-erp-inline { vertical-align: -0.125em; }  /* ajuste para íconos en texto corrido */
.icon-erp-muted  { opacity: 0.55; }              /* decorativo, no prioritario */
```

### Regla extendida en tailwind-input.css (futura compilación)

```css
@layer base {
    .material-symbols-outlined {
        font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24;
        font-size: 24px;        /* default; overridden by text-* utilities */
        line-height: 1;
        vertical-align: middle;
        flex-shrink: 0;
        user-select: none;
    }
}
```

---

## G. Cambios en Login

1. **Eliminado:** `<style>` inline con `.material-symbols-outlined { font-variation-settings...; vertical-align: middle; }` — redundante desde que `standalone-tokens.css` cubre estas reglas.
2. **Corregido:** Ícono `check` del checkbox: `absolute text-sm pointer-events-none left-[3px]` → `absolute inset-0 flex items-center justify-center pointer-events-none text-sm`. Elimina el offset manual frágil y centra correctamente el ícono en el área del checkbox.

---

## H. Cambios en Layout / Sidebar / Header

1. **Header — notifications, search, assignment, help:** Agregado `icon-erp-lg` (24px). Sin tamaño explícito previo, heredaban los 16px del browser — demasiado pequeños para botones de header.
2. **Header — assignment:** Agregado `aria-hidden="true"` (decorativo, el botón tiene `aria-label`).
3. **Header — FAB bug_report:** Cambio `text-base` → `icon-erp` (18px) + agregado `aria-hidden="true"` (el botón tiene texto visible).
4. **Sidebar — logout:** Cambio `text-base` (16px) → `icon-erp` (18px). Ahora consistente con todos los demás íconos del sidebar (`text-lg` = 18px).
5. **Sidebar — account_circle:** Agregado `shrink-0`. Previene compresión en viewports ajustados.

---

## I. Cambios en Dashboard

No se realizaron cambios en `Views/Dashboard/Index.cshtml`.  
Revisión confirmó que los tamaños de íconos son intencionales:
- Badge header: `text-sm` (14px) → OK dentro de badge pequeño
- Quick actions: `text-2xl` (24px) → prominencia intencional
- Card icons: `text-[18px]` → correcto

La regla base global (via `shared-components.css`) mejora automáticamente la alineación vertical sin cambiar tamaños.

---

## J. Cambios en componentes compartidos

No se tocaron otros componentes compartidos. El sistema `.row-action .material-symbols-outlined` (18px/20px en media query) ya estaba bien definido y fue preservado.

---

## K. Accesibilidad

- Íconos decorativos (dentro de botones con `aria-label` o junto a texto visible): `aria-hidden="true"` verificado y corregido en `assignment` (header) y `bug_report` (FAB).
- Icon-only buttons (notifications, search, assignment, help, collapseSidebar, toggleSidebar): todos tienen `aria-label` ✓
- `user-select: none` en la regla base — los ligatures de Material Symbols son seleccionables como texto; esto los hace no-seleccionables para mejorar UX.

---

## L. Mobile / Responsive

- La regla base `flex-shrink: 0` previene que íconos se compriman en layouts flex ajustados en mobile.
- La corrección del checkbox check icon usa `inset-0 flex items-center justify-center` que funciona correctamente en todos los viewports.
- No se alteraron breakpoints ni comportamientos responsivos.

---

## M. Validaciones ejecutadas

```
dotnet build --configuration Release -o ._build_check/... → 0 errores, 1 warning (NETSDK1194 esperado)
git diff --check → OK (sin problemas de whitespace)
git status --short → solo archivos previstos
```

---

## N. Tests ejecutados

```
LayoutUiContractTests: 57/57 OK
Suite Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard: 230/230 OK
```

Configuración usada: `--configuration Debug` (Release bloqueado por app corriendo en PID 47040).  
Los tests no referencian clases de íconos — ningún contrato roto.

---

## O. Playwright

Ejecutado: `npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js`  
Con credenciales: `E2E_USER=Admin / E2E_PASS=Admin123!`  
Resultado: ver sección de informe final.

---

## P. Riesgos y deudas

1. **tailwind.css compilado:** `tailwind-input.css` se actualizó (fuente canónica), pero `tailwind.css` (output compilado) no refleja las nuevas propiedades de `@layer base` aún. Esto no es un problema funcional porque `shared-components.css` (cargado después) aplica las mismas reglas con mayor precedencia. Al próxima compilación de Tailwind, la regla base estará unificada.

2. **Dashboard íconos small:** El ícono `space_dashboard` en el badge hero usa `text-sm` (14px). Dentro de un badge pequeño esto es intencional. Si en el futuro se decide estandarizar, usar `.icon-erp-sm` (16px).

3. **icon-erp-* no disponible en Login:** Las clases semánticas `.icon-erp-*` están en `shared-components.css` que Login no carga. Para el Login esto no es problema actualmente — los íconos de Login ya tienen tamaños explícitos via clases Tailwind. Si en el futuro se necesita añadir `icon-erp-*` a Login, se puede mover las clases a `standalone-tokens.css`.

4. **icon-size-22 legacy:** La clase `.icon-size-22` (22px) en `shared-components.css` sigue existiendo para retrocompatibilidad. Usada actualmente solo en el botón `menu` del header mobile. Puede migrarse a `icon-erp-lg` en un micro-lote futuro.

---

## Q. Próximo paso recomendado

**UI-5B** — Auditoría y normalización de íconos en vistas de módulos.  
Con el sistema base definido en UI-5A, el próximo paso es aplicar progresivamente las clases `.icon-erp-*` en vistas de módulos prioritarios (Venta, Caja, Clientes, Catálogo) reemplazando los tamaños ad-hoc restantes.

O alternativamente: **UI-6** — Auditoría de tipografía y espaciado global (otra deuda transversal).
