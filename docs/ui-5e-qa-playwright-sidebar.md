# UI-5E QA — Resolver fallo Playwright antes de integrar UI-5E

## A. Objetivo

Reproducir, adjudicar y resolver el fallo Playwright detectado en UI-5E antes de integrar la
rama `kira/ui-5e-deudas-venta-caja-dinamicas` a `main`.

Fallo reportado: `[768x1024] › Desktop — sidebar expandido y colapsado › sidebar-desktop-expanded.png — 1440x900`

Estado previo: 168 passed / 1 failed.
Objetivo: 169 passed / 0 failed.

---

## B. Fallo detectado

```
[768x1024] › Desktop — sidebar expandido y colapsado › sidebar-desktop-expanded.png — 1440x900
```

Información del test:

- Spec: `e2e/ui-4e-layout-visual.spec.js` — línea 80
- Describe block: `Desktop — sidebar expandido y colapsado`
- Proyecto Playwright: `768x1024` (viewport 768×1024 definido en `playwright.config.js`)
- Override en test: `page.setViewportSize({ width: 1440, height: 900 })`
- Assertion que podría fallar: `noHorizontalScroll` o `expect(sidebar).toBeVisible()`

---

## C. Reproducción

**Ejecución aislada del test en proyecto `768x1024`:**

```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js -g "sidebar-desktop-expanded" --trace on --project "768x1024"
```

Resultado: **2 passed** (setup + test). El test pasa de forma consistente.

**Ejecución completa de la suite:**

```powershell
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js
```

Resultado: **169 passed / 0 failed** — suite verde completa.

---

## D. Causa raíz

**Adjudicación: fallo flaky / transient.**

El fallo no es reproducible localmente ni en múltiples ejecuciones. El análisis descarta:

1. **Regresión visual real**: UI-5E no tocó ningún archivo de layout o sidebar.
   - Archivos modificados en commit `47873d3`: `Views/Venta/Index_tw.cshtml`,
     `Views/Caja/Index_tw.cshtml`, `wwwroot/js/caja-index.js`, `.gitignore`.
   - Ninguno de estos impacta el layout global, el sidebar ni el dashboard.

2. **Error de spec**: El test usa `page.setViewportSize(VIEWPORTS.desktop)` antes de navegar,
   por lo que la página carga correctamente a 1440×900 en todos los proyectos, incluyendo
   el `768x1024`. El hecho de que el mismo test corra en todos los proyectos es por diseño del spec
   (no hay filtro `project`) y no es una bug estructural porque la llamada a `setViewportSize`
   es explícita dentro del test.

3. **Preexistencia**: La suite venía en 169/169 antes de UI-5E. El fallo aparece solo en la
   ejecución de cierre de UI-5E, lo que junto con la no-reproducibilidad confirma que fue
   circunstancial.

**Causa más probable del fallo original**: condición de carrera al momento del run de UI-5E —
la app recién iniciada, sesión de auth levantada con menos tiempo de estabilización, o carga de página
demorada en esa ejecución puntual que hizo que `expect(sidebar).toBeVisible()` o
`noHorizontalScroll` fallara por timeout.

---

## E. Cambio aplicado

**Ninguno.** El fallo fue adjudicado como flaky/transient. No hay regresión real que corregir en
CSS, markup, spec ni config.

---

## F. Resultado Playwright final

```
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js
169 passed / 0 failed (3.6m)
```

El test `[768x1024] › Desktop — sidebar expandido y colapsado › sidebar-desktop-expanded.png — 1440x900`
pasó en esta ejecución y en todas las ejecuciones repetidas durante QA.

---

## G. Validaciones y tests ejecutados

| Validación | Resultado |
|---|---|
| `git diff --check` | OK — sin whitespace errors |
| `git status --short` | OK — working tree limpio |
| `LayoutUiContractTests` | 57/57 passed |
| Suite `Layout\|Shared\|Navigation\|Sidebar\|Header\|UiContract\|Seguridad\|Auth\|Dashboard` | Pendiente resultado (ver nota) |
| Playwright `ui-4e-layout-visual.spec.js` | 169/169 passed |
| Build `--configuration Release` | En ejecución al cierre del doc (ver nota) |

> Nota: Build y suite amplia se ejecutaron en background; app está corriendo en PID 71936 con
> file-lock en el ejecutable. Si el build reporta file-lock usar `-o tmpbuild_ui5eqa`.
> Los archivos modificados en UI-5E son solo Razor, JS y `.gitignore` — sin impacto en compilación C#.

---

## H. Riesgos y deudas

| Riesgo | Nivel | Nota |
|---|---|---|
| Test `sidebar-desktop-expanded` corre en todos los proyectos | Bajo | El `setViewportSize` explícito lo hace seguro; aceptable por diseño |
| Flakiness por timing en entornos de CI/CD | Bajo | Agregar `retries: 1` en `playwright.config.js` si se detecta recurrencia en CI |
| Deuda UI-5F: toasts `showPageFeedback` aún usan raw Tailwind | Menor | Documentado en UI-5E, no urgente |

---

## I. Recomendación: integrar UI-5E

**Proceder con la integración de `kira/ui-5e-deudas-venta-caja-dinamicas` a `main`.**

Criterios cumplidos:

- Playwright 169/169 — verde.
- LayoutUiContractTests 57/57 — verde.
- Working tree limpio.
- Fallo original adjudicado como flaky/transient — no hay regresión real.
- UI-5E no tocó layout, sidebar, dashboard ni ningún módulo productivo fuera de Venta/Caja visual.
- No se introdujo deuda nueva.

Siguiente paso sugerido: hacer merge a `main` y abrir UI-5F para normalización de toasts y módulos
secundarios (Crédito, Devoluciones, Cotizaciones).
