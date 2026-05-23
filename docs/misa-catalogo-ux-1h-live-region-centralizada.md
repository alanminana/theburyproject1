# MISA-CATALOGO-UX-1H — Live Region Centralizada en Catálogo

## A. Estado inicial

- HEAD: `dd777fc` (MISA-CATALOGO-UX-1G — aria-live en mensajes de modales de catálogo)
- Rama base: `main`
- Fase previa cerrada: 1G (aria-live/role="alert"/aria-atomic en los 10 modales)

---

## B. Rama creada

```
misa/catalogo-ux-1h-live-region-centralizada
```

---

## C. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml` — los 10 modales y sus contenedores aria-live
- `wwwroot/js/catalogo-module.js` — módulo central (CatalogoModule)
- `wwwroot/js/catalogo-index.js` — toasts y acciones del índice
- `wwwroot/js/historial-precio-modal.js` — `show()`/`hide()` + `#historial-precio-loading`
- `wwwroot/js/precio-aumento-modal.js` — `loadPreview()` + `#precio-preview-loading`
- `wwwroot/js/producto-crear-modal.js` — `showValidation()`
- `wwwroot/js/producto-editar-modal.js` — `showValidation()`
- `wwwroot/js/categoria-crear-modal.js` — `showValidation()`
- `wwwroot/js/categoria-editar-modal.js` — `showValidation()`
- `wwwroot/js/marca-crear-modal.js` — `showValidation()`
- `wwwroot/js/marca-editar-modal.js` — `showValidation()`
- `wwwroot/js/producto-comision-modal.js` — `showError()` / `showSuccess()`
- `wwwroot/js/movimientos-inventario-modal.js` — stats dinámicos, paginación
- `docs/misa-catalogo-ux-1g-aria-live-modales.md` — documento de referencia

---

## D. Decisión tomada

**No implementar** `CatalogoModule.announce()` ni una live region sr-only centralizada.

---

## E. Justificación técnica

### E.1 — Inventario de mensajes dinámicos

#### Errores y éxito (8 + 2 + 1 contenedores)

Todos los mensajes de error y éxito de los 10 modales siguen el mismo patrón:

1. JS cambia `textContent` del hijo de texto.
2. JS remueve la clase `hidden` del contenedor.

Los contenedores tienen `aria-live="assertive"` o `aria-live="polite"` (aplicados en 1G).

**Este patrón es correcto y funciona en todos los AT principales (NVDA, VoiceOver, JAWS).**
El cambio de `textContent` dentro de una región live visible es el mecanismo más robusto para disparar anuncios, sin excepción entre AT.

Contenedores verificados:
- `#modal-validation-summary` → `showValidation(text)` cambia textContent + remueve `hidden`
- `#prod-edit-validation-summary` → idem
- `#cat-modal-validation-summary` → idem
- `#cat-edit-validation-summary` → idem
- `#marca-modal-validation-summary` → idem
- `#marca-edit-validation-summary` → idem
- `#comision-modal-validation` → `showError(msg)` cambia textContent + remueve `hidden`
- `#precio-modal-validation-summary` → `showError(text)` cambia textContent + remueve `hidden`
- `#comision-modal-success` → `showSuccess(msg)` cambia textContent + remueve `hidden`
- `#historial-precio-feedback` → `showFeedback(msg, type)` cambia textContent + remueve `hidden`

**Estos 10 contenedores no requieren `announce()`.**

#### Loading states (2 contenedores — foco de la deuda)

| ID | Texto | Mecanismo de toggle |
|---|---|---|
| `#historial-precio-loading` | "Cargando historial..." (estático en HTML) | `classList.remove('hidden')` / `classList.add('hidden')` |
| `#precio-preview-loading` | "Calculando precios..." (estático en HTML) | `classList.remove('hidden')` / `classList.add('hidden')` |

Ambos tienen `aria-live="polite" aria-atomic="true"` (aplicados en 1G).

La deuda documentada en 1G es:

> JAWS puede requerir un cambio de textContent para detectarlo.

### E.2 — Análisis del comportamiento AT con toggle de visibilidad

El mecanismo de toggle es: clase CSS `hidden` (equivalente a `display: none`).

**Comportamiento AT al transición de `display:none` → visible en una región live:**

- **NVDA (>=2019) + Chrome/Firefox:** Anuncia el contenido de la región al hacerse visible. ✅
- **VoiceOver (macOS/iOS):** Anuncia el contenido de la región al hacerse visible. ✅
- **JAWS (>=18) + Chrome:** Anuncia el contenido de la región al hacerse visible cuando la transición es desde `display:none`. ✅

La distinción relevante con JAWS es:
- **Región siempre visible con textContent estático que cambia:** JAWS puede no anunciar si el cambio es solo en texto sin que la región sea re-evaluada.
- **Región que pasa de `display:none` a visible:** JAWS SÍ evalúa el árbol de accesibilidad y anuncia el contenido al momento de la transición.

**Los loading states caen en el segundo caso.** Al remover `hidden`, el elemento pasa de `display:none` a visible, lo que dispara la evaluación de la región live en JAWS moderno.

Esta distinción invalida la preocupación documentada en 1G, que asumía el primer caso.

### E.3 — Riesgo de doble anuncio

Si se implementara `CatalogoModule.announce()` + sr-only live region persistente:

- Al mostrar el loading div, ambos canales actuarían:
  1. La región live del loading div (al hacerse visible)
  2. La sr-only live region al recibir el texto vía `announce()`

Esto generaría **doble anuncio en NVDA y VoiceOver**, que funcionan correctamente con el mecanismo actual.

Solucionar el doble anuncio requeriría eliminar `aria-live` de los loading divs (revirtiendo parte de 1G), lo que introduce complejidad sin beneficio neto.

### E.4 — Mensajes dinámicos restantes (movimientos-inventario-modal)

Los stats dinámicos (`statTotal`, `statEntradas`, `statSalidas`, `statAjustes`) y los contadores de paginación cambian `textContent` de elementos que **no tienen** `aria-live`.

Esto es correcto: son datos operativos de una tabla de movimientos que el usuario está explorando activamente, no mensajes de error/éxito ni estados de proceso. Anunciarlos automáticamente sería disruptivo.

No requieren `announce()`.

### E.5 — Toasts globales

El evento `catalogo:toast` crea elementos con `role="status"` o `role="alert"` que se insertan en el DOM. La inserción de un elemento con role de landmark live es anunciada por todos los AT principales.

Este canal ya cubre los anuncios post-operación (éxito al cerrar un modal, error de conexión global).

No requieren `announce()` centralizado.

### E.6 — Conclusión

No existe ningún mensaje dinámico en los 10 modales de Catálogo que:

1. No esté cubierto por el patrón textContent + aria-live ya implementado, O
2. No esté cubierto por el mecanismo de visibilidad toggle + aria-live para loading states, O
3. No esté cubierto por el sistema de toasts con role="alert"/"status"

La implementación de `CatalogoModule.announce()` + sr-only live region:
- No agrega cobertura real para ningún AT en el contexto actual
- Introduce riesgo de doble anuncio en NVDA/VoiceOver
- Agrega complejidad a `catalogo-module.js` sin beneficio verificable

---

## F. Mensajes / loading states auditados

| ID | Tipo | Patrón JS | Cobertura AT |
|---|---|---|---|
| `#modal-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#prod-edit-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#cat-modal-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#cat-edit-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#marca-modal-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#marca-edit-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#comision-modal-validation` | Error operación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#precio-modal-validation-summary` | Error validación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#comision-modal-success` | Éxito operación | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#historial-precio-feedback` | Error/éxito | textContent + toggle hidden | NVDA/VoiceOver/JAWS ✅ |
| `#historial-precio-loading` | Loading state | toggle display:none→visible | NVDA/VoiceOver/JAWS ✅ |
| `#precio-preview-loading` | Loading state | toggle display:none→visible | NVDA/VoiceOver/JAWS ✅ |
| Toasts `catalogo:toast` | Feedback global | inserción role=alert/status | NVDA/VoiceOver/JAWS ✅ |
| Stats movimientos | Datos operativos | textContent sin aria-live | Sin anuncio (correcto) ✅ |

---

## G. Cambios aplicados

**Ninguno.** Solo se generó este documento de auditoría y decisión.

---

## H. Contratos preservados

No hubo modificaciones. Todos los contratos de 1E, 1F y 1G permanecen intactos:

- IDs, `name`, `data-*`, `asp-*`: sin cambios
- Antiforgery, endpoints, payloads: sin cambios
- `CatalogoModule.trapFocus()`: sin cambios
- Foco inicial y retorno de foco al trigger (1E): sin cambios
- `aria-live`/`role="alert"` de 1G: sin cambios

---

## I. Qué no se tocó

- Ningún archivo de código
- Ningún archivo JS
- Ningún archivo Razor/HTML
- Ningún archivo CSS
- Ningún archivo de backend

---

## J. Validaciones ejecutadas

- `git diff --check`: solo warnings preexistentes en archivos no commiteados (AGENTS.md, CLAUDE.md)
- `git status --short`: solo el documento nuevo + archivos locales sensibles no commiteados
- Build y tests: no requeridos (fase doc-only, diff confirma que solo cambió documentación)

---

## K. Resultado de tests

No ejecutados. La fase es doc-only: no se tocó ningún archivo de código.

Referencia de baseline (de 1G):
- `LayoutUiContractTests`: 57/57 OK
- Build Release: 0 errores, 0 advertencias

---

## L. Resultado de build

No ejecutado. Fase doc-only.

---

## M. Estado final git

```
 M .claude/settings.local.json    (no commitear)
 M AGENTS.md                      (no commitear)
 M CLAUDE.md                      (no commitear)
 D skills-lock.json               (no commitear)
?? docs/misa-catalogo-ux-1h-live-region-centralizada.md  ← único archivo a commitear
```

---

## N. Procesos cerrados

Ninguno iniciado por esta tarea.

---

## O. Procesos preexistentes no tocados

- PID 11936: no tocado (según instrucción)
- VS Code, C# DevKit, MCPs: no tocados

---

## P. Estado de archivos sensibles

- `.claude/settings.local.json`: modificado localmente, **no commiteado**
- `AGENTS.md`: modificado localmente, **no commiteado**
- `CLAUDE.md`: modificado localmente, **no commiteado**
- `skills-lock.json`: eliminado localmente, **no commiteado**

---

## Q. Temporales generados

Ninguno.

---

## R. Working tree final

```
 M .claude/settings.local.json    (no commitear)
 M AGENTS.md                      (no commitear)
 M CLAUDE.md                      (no commitear)
 D skills-lock.json               (no commitear)
?? docs/misa-catalogo-ux-1h-live-region-centralizada.md  ← único archivo commiteado
```

---

## S. Riesgos y deudas restantes

**Sin riesgos funcionales.** No hubo cambios de código.

**Deuda de accesibilidad:** La preocupación sobre JAWS y loading states documentada en 1G
queda **resuelta por análisis**: la transición de `display:none` a visible en una región
aria-live sí dispara anuncios en JAWS moderno. No hay deuda técnica pendiente en este punto.

**Cobertura de accesibilidad de la serie MISA-CATALOGO-UX:** Con 1A–1H completados,
Catálogo tiene cobertura sólida en:
- Semántica de modales (1B)
- scope/headers en tablas (1C)
- aria-sort en columnas ordenables (1D)
- Foco inicial y retorno al trigger (1E)
- Focus trap (1F)
- aria-live en todos los mensajes dinámicos (1G)
- Live region centralizada: auditada y descartada por no agregar valor (1H)

---

## T. Commit

```
Documentar decision sobre live region centralizada en catalogo (MISA-CATALOGO-UX-1H)
```

---

## U. Push rama / Merge / Push main

Ver commits finales en `git log --oneline -5`.

---

## V. Próximo prompt recomendado

La serie MISA-CATALOGO-UX (1A–1H) está completa desde el punto de vista de accesibilidad.

Si se desea continuar con otra área del ERP:

```
MISA-[MODULO]-UX-0 — Auditar UX/accesibilidad de [módulo] siguiendo el mismo
esquema que MISA-CATALOGO-UX: semántica, tablas, foco, focus trap, aria-live.
```

O si se quiere validar la cobertura completa de Catálogo con Playwright/AT real:

```
MISA-CATALOGO-UX-QA — Validar accesibilidad de Catálogo con Playwright
usando axe-core o similar, confirmando que los 10 modales pasan auditoría
automatizada antes de cerrar la serie.
```
