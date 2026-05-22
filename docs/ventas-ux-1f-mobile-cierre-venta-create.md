# VENTAS-UX-1F — Mobile visual y experiencia de cobro en Venta/Create

## A. Objetivo

Mejorar la experiencia mobile y la jerarquía visual del cierre de venta en las dos
pantallas canónicas: el modal de Nueva Venta (`Index_tw` + `_VentaCrearModal.cshtml`)
y la vista standalone de creación (`Create_tw.cshtml`).

Problema base: en mobile, el botón Confirmar y el resumen de totales quedan al fondo
de columnas con mucho contenido. El usuario debe scrollear para llegar a ellos.

---

## B. Base y contexto

- Base: `main` @ `78653c8` (VENTAS-UX-1E-B integrada)
- Rama: `kira/ventas-ux-1f-mobile-cierre-venta-create`
- Fase visual/frontend puro. Sin cambios en backend, controllers, services, entidades
  ni migraciones.
- No se tocó JS (`venta-create.js`). Los cambios de mirroring son inline en Razor.

---

## C. Archivos auditados

| Archivo | Rol |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Modal de Nueva Venta (incluido en Index_tw) |
| `Views/Venta/Create_tw.cshtml` | Vista standalone de creación |
| `wwwroot/css/venta-module.css` | CSS específico del módulo venta |
| `wwwroot/css/shared-components.css` | CSS compartido; contiene clases reutilizables |
| `wwwroot/js/venta-create.js` | JS productivo (solo lectura) |
| `e2e/ui-4e-layout-visual.spec.js` | Playwright visual (solo lectura) |

---

## D. Hallazgos mobile

**Layout grid:**
- Ambas vistas usan `grid grid-cols-1 gap-N lg:grid-cols-3`.
- En mobile (grid-cols-1) la columna izquierda (secciones 1–5) renderiza primero,
  seguida por la columna derecha (sección 6: Totales + Confirmar) al fondo.
- El usuario debe scrollear todo el formulario para llegar al botón Confirmar.

**Classes ya disponibles en `shared-components.css`:**
- `sticky-action-footer` (línea 1318): `position:fixed;bottom:0;hidden on md+`. 
  Comentada como "No está aplicado en vistas todavía."
- `total-breakdown-card`, `commercial-summary-bar`, `payment-option-card` — existentes
  y documentadas pero no usadas en esta fase.

---

## E. Hallazgos de jerarquía visual

**`Create_tw.cshtml`:**
- La sección 6 usa `bg-primary` sólido con buen contraste. Buen diseño.
- Ya existe `id="hero-total"` en la hero section (arriba) que actualiza el total en
  tiempo real. Pero esta sección scrollea fuera de vista.
- El botón Confirmar (`type="submit" id="btn-confirmar"`) es el único submit del form.

**`_VentaCrearModal.cshtml`:**
- Sección 6 usa clase `vm-totals` (bg dark + border).
- El botón Confirmar es `type="button" id="btn-confirmar" onclick="VentaCrearModal.submit()"`.
- El header del modal ya es `sticky top-0 z-10`.
- No había ninguna indicación de total visible al abrir el modal sin scrollear.

---

## F. Cambios aplicados en Razor

### `_VentaCrearModal.cshtml`

**1. Barra sticky mobile de resumen (nueva):**
Insertada entre el `vm-error-summary` y el `<div class="p-6">` del formulario.

```html
<div class="vm-mobile-summary-bar" role="region" aria-label="Resumen de compra">
    <div class="vm-mobile-summary-bar__info">
        <span class="material-symbols-outlined" style="font-size:15px;flex-shrink:0">receipt_long</span>
        <span>Total: <strong id="vm-modal-sticky-total">$0,00</strong></span>
    </div>
    <button type="button"
            class="vm-btn-confirm-sm"
            aria-hidden="true"
            tabindex="-1"
            onclick="VentaCrearModal.submit()">
        <span class="material-symbols-outlined" style="font-size:14px">point_of_sale</span>
        Confirmar
    </button>
</div>
```

Comportamiento: `position:sticky; top:4.75rem` (debajo del header). Visible solo en
`<768px`. Se oculta en desktop.

**2. Script inline de espejo:**
Al final del archivo (antes del sub-modal de tipo de pago). Observa `total-final` con
`MutationObserver` y refleja el valor en `vm-modal-sticky-total`.

### `Create_tw.cshtml`

**1. Footer sticky mobile (clase existente de shared-components.css):**
Insertado justo antes del cierre de `</form>` (después de `detalles-hidden-inputs`).

```html
<div class="sticky-action-footer" role="region" aria-label="Resumen y confirmación">
    <div ...>
        <span>Total</span>
        <span id="sticky-create-total">$0,00</span>
    </div>
    <button type="button" class="vm-btn-confirm-sm"
            aria-hidden="true" tabindex="-1"
            onclick="document.getElementById('btn-confirmar').click()">
        <span ...>point_of_sale</span>
        Confirmar
    </button>
</div>
```

`sticky-action-footer` es `position:fixed;bottom:0;z-index:40`. Oculto en `md+`.

**2. Script inline de espejo:** Igual al del modal, refleja `total-final` → `sticky-create-total`.

**3. Espaciador:** `<div class="h-20 md:hidden" aria-hidden="true">` para clearance mobile.

---

## G. Cambios aplicados en CSS

### `wwwroot/css/venta-module.css` (nuevas clases al final)

```css
.vm-mobile-summary-bar { position:sticky; top:4.75rem; z-index:9; display:flex; ... }
@media (min-width: 768px) { .vm-mobile-summary-bar { display:none; } }
.vm-mobile-summary-bar__info { ... }
.vm-mobile-summary-bar__info strong { color:#f1f5f9; font-variant-numeric:tabular-nums; }
.vm-btn-confirm-sm { display:inline-flex; ...; background-color:#135bec; ... }
.vm-btn-confirm-sm:hover  { background-color:#1a6aff; }
.vm-btn-confirm-sm:active { background-color:#1050d4; }
```

No se tocó `shared-components.css` — la clase `sticky-action-footer` ya existía allí.

---

## H. Qué no se tocó

- `wwwroot/js/venta-create.js` — solo lectura
- Controllers, Services, Models, Migrations, DTOs
- Entidades de dominio
- Endpoints ni payloads
- Cálculos de totales (subtotal, descuento, IVA, total)
- stock, caja, crédito, confirmación real de venta
- Cotización, Inventario, Catálogo (dominio de Misa)
- IDs de totales: `total-final`, `total-subtotal`, `total-descuento`, `total-iva`
- Hidden inputs: `hdn-subtotal`, `hdn-descuento`, `hdn-iva`, `hdn-total`
- `id="btn-confirmar"` principal (ambas vistas)
- `onclick="VentaCrearModal.submit()"` en el modal
- `type="submit"` del confirm en Create_tw
- `id="tbody-detalles"`, `id="select-tipo-pago"`, todos los contratos JS
- AGENTS.md, CLAUDE.md, .claude/settings.local.json, skills-lock.json

---

## I. Accesibilidad / baja visión

- Los botones duplicados en sticky/bar tienen `aria-hidden="true"` y `tabindex="-1"`.
  Screen readers y teclado ignoran los botones presentacionales; llegan al botón real.
- El botón real en cada vista conserva todos sus atributos y semántica original.
- `role="region" aria-label="..."` en los contenedores sticky da contexto sin duplicar.
- El `vm-btn-confirm-sm` mantiene contraste suficiente (#135bec sobre blanco = 4.5+:1).

---

## J. Riesgo funcional

**Bajo.** Los botones sticky son `type="button"` con `aria-hidden="true"` — no pueden
causar doble submit. El script MutationObserver es read-only respecto al estado de la app.
No se tocó ningún cálculo ni lógica de negocio.

**Riesgo potencial menor:** en algunos navegadores móviles antiguos, `position:sticky`
puede no funcionar correctamente dentro de ciertos contenedores flex/grid. En ese caso
la barra simplemente aparece en su posición natural en el flujo, sin sticking — degradación
graceful, no un fallo.

---

## K. Tests

**Nuevos tests en `VentaCreateUiContractTests.cs` (VENTAS-UX-1F):**

| Test | Qué verifica |
|---|---|
| `CreateView_StickyFooterMobile_ExisteEnFormulario` | `sticky-action-footer` y `sticky-create-total` en Create_tw |
| `CreateView_StickyFooterMobile_BtnEsTypeButton` | botón sticky es type=button, no submit |
| `CreateView_StickyFooterMobile_BtnTieneAriaHiddenYTabindexNegativo` | aria-hidden y tabindex=-1 |
| `VentaCrearModal_MobileSummaryBar_Existe` | `vm-mobile-summary-bar` y `vm-modal-sticky-total` en modal |
| `VentaCrearModal_MobileSummaryBar_BtnTieneAriaHiddenYTabindexNegativo` | accesibilidad barra modal |
| `CreateView_TotalesConservanSusIds` | total-final, total-subtotal, total-descuento, total-iva en Create |
| `CreateView_HiddenInputsTotalesConservados` | hdn-subtotal, hdn-descuento, hdn-iva, hdn-total en Create |
| `VentaCrearModal_TotalesConservanSusIds` | mismos IDs de totales en modal |
| `VentaCrearModal_BtnConfirmarConservaOnclickVentaCrearModalSubmit` | contrato modal btn-confirmar |
| `CreateView_BtnConfirmarPrincipalConservado` | btn-confirmar + type=submit en Create |

---

## L. Validaciones

| Validación | Resultado |
|---|---|
| `dotnet build --configuration Release` | **Compilación correcta — 0 errores, 0 advertencias** |
| `dotnet test --filter VentaCreate` | **89/89 OK** (79 existentes + 10 nuevos VENTAS-UX-1F) |
| `npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js` | **169/169 passed** |
| `git diff --check` | warnings solo en AGENTS.md/CLAUDE.md (preexistentes, no commiteados) |
| Temporales trackeados | ninguno |

---

## M. Playwright

- Test `venta-create-mobile.png` (390x844): pasó — visual solo, sin confirmar.
- Test `venta-create-desktop.png` (1440x900): pasó — el sticky footer no aparece en desktop.
- `169/169 passed` sin regresiones.

---

## N. Procesos

| Proceso | Estado |
|---|---|
| `dotnet run` (PID 24068, puerto 5187) | Iniciado por esta tarea para Playwright. **Cerrar al finalizar.** |
| VS Code, C# DevKit, MSBuild language server, Playwright MCP, Context7 MCP | Preexistentes — no tocar |

---

## O. Deudas restantes

1. La `vm-mobile-summary-bar` no hace hide/show dinámico según si el form está vacío.
   Podría refinarse en una fase futura para mostrar el CTA en un estado diferente.
2. El `top: 4.75rem` de la barra modal es una estimación del alto del header.
   Si el header cambia de tamaño (ej. se agrega una línea de texto), la barra podría
   no quedar exactamente debajo del header. Se puede mitigar con CSS custom property.
3. `shared-components.css` tiene clases como `total-breakdown-card` y
   `commercial-summary-bar` que podrían usarse para mejorar aún más la sección de
   totales en ambas vistas (VENTAS-UX-1G o futura fase).
4. El campo Observaciones y el bloque de excepción crediticia en el modal aún quedan
   al final del scroll — no son bloqueantes pero forman parte del flujo completo.

---

## P. Próximo paso recomendado

**VENTAS-UX-1G — Resumen pre-confirmación y claridad final antes de confirmar.**

Objetivo tentativo:
- Mostrar con más claridad qué operación se va a confirmar (cliente, total, tipo de pago).
- Reforzar el tipo de pago principal visible antes del botón confirmar.
- Mostrar cuotas/tarjeta si aplica, como preview conciso.
- Mostrar alertas relevantes (mora, cupo, documentación) de forma más prominente.
- Solo Razor/CSS — sin backend, cálculos, endpoints, payloads, stock, caja, crédito.
