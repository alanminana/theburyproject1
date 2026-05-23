# MISA-CATALOGO-UX-1A — Corregir color del botón Movimientos de Inventario

## A. Objetivo

Eliminar el override CSS rojo (destructivo) del botón `#btn-movimientos-inventario` en Catálogo y reemplazarlo por un estilo visible, coherente con el sistema y semánticamente correcto.

## B. Base y contexto

- Rama base: `main` en commit `d2a8317` (MISA-CATALOGO-UX-0)
- Rama de trabajo: `misa/catalogo-ux-1a-boton-movimientos-color`
- Tipo de fase: CSS-only / bajo riesgo

## C. Deuda tomada desde MISA-CATALOGO-UX-0

La auditoría MISA-CATALOGO-UX-0 detectó que `#btn-movimientos-inventario` tenía un override rojo en `catalogo-module.css`. El rojo fue aplicado originalmente porque el cliente no encontraba el botón, no porque la acción sea destructiva. El comentario en el CSS decía explícitamente "Botón Movimientos en rojo", reconociendo que era un parche de visibilidad, no una decisión semántica.

## D. Archivos auditados

- `Views/Catalogo/Index_tw.cshtml` — confirmado: botón con clases `btn-erp-ghost btn-sm sm:w-auto`
- `wwwroot/css/catalogo-module.css` — override rojo localizado en líneas 64–80
- `wwwroot/css/shared-components.css` — revisado: jerarquía de botones del sistema, colores primarios

## E. Hallazgo del override rojo

En `wwwroot/css/catalogo-module.css` (antes del cambio):

```css
/* Botón Movimientos en rojo */
#btn-movimientos-inventario {
    border-color: rgba(239, 68, 68, 0.4);
    background-color: rgb(220, 38, 38);   /* rojo sólido — destructivo */
    color: #ffffff;
}
#btn-movimientos-inventario:hover {
    border-color: rgba(239, 68, 68, 0.6);
    background-color: rgb(185, 28, 28);   /* rojo más oscuro */
    color: #ffffff;
}
#btn-movimientos-inventario:focus {
    outline: none;
    box-shadow: 0 0 0 3px rgba(239, 68, 68, 0.25);
}
```

`rgb(220, 38, 38)` = Tailwind `red-600` sólido. Este es incluso más agresivo que `btn-erp-danger`, que en el sistema usa rojo transparente (`rgba`), no rojo sólido relleno.

## F. Cambio aplicado

En `wwwroot/css/catalogo-module.css` (después del cambio):

```css
/* Botón Movimientos — operacional, no destructivo */
#btn-movimientos-inventario {
    border-color: rgba(19, 91, 236, 0.35);
    background-color: rgba(19, 91, 236, 0.08);
    color: #93b4f8;
}
#btn-movimientos-inventario:hover {
    border-color: rgba(19, 91, 236, 0.55);
    background-color: rgba(19, 91, 236, 0.14);
    color: #bfcffe;
}
#btn-movimientos-inventario:focus-visible {
    outline: 2px solid #135bec;
    outline-offset: 2px;
}
```

Los valores de color corresponden al primary del sistema (`#135bec`). El tinte es sutil pero visible, y el texto azul claro tiene buen contraste sobre fondo oscuro.

## G. Justificación semántica

| Criterio | Antes | Después |
|---|---|---|
| Color | Rojo sólido (`rgb(220,38,38)`) | Indigo sutil (primary del sistema) |
| Semántica | Destructivo / peligro | Operacional / navegación |
| Consistencia | Rompe el sistema | Usa el color primario |
| Contraste | Alto pero incorrecto | Alto y correcto |
| Hover | Rojo más oscuro | Indigo más visible |
| Focus | Anillo rojo | Outline estándar `#135bec` |

Movimientos de Inventario es una acción de navegación/consulta operacional. No elimina ni cancela nada. Su color debe comunicar "acción importante disponible", no "acción irreversible o peligrosa".

El color indigo `#135bec` es el primary del ERP. Usarlo en tinte sutil para este botón comunica "acción principal del módulo" sin competir con acciones de alta prioridad (como "Nuevo Producto" que usa `btn-erp-primary` sólido).

## H. Contratos preservados

- `id="btn-movimientos-inventario"` — preservado (no se tocó Razor)
- `class="btn-erp-ghost btn-sm sm:w-auto"` — preservado
- Texto visible "Movimientos" — preservado
- Ícono `swap_vert` — preservado
- Eventos JS (click handler de movimientos) — preservados
- Selectores usados por tests — no se detectó uso de este selector en tests de contrato HTML
- Endpoints, payloads, rutas — sin cambios

## I. Qué no se tocó

- Razor / HTML — sin cambios
- JavaScript — sin cambios
- Controllers / Services / Models — sin cambios
- Backend — sin cambios
- `shared-components.css` — sin cambios (no fue necesario)
- `layout.css` — sin cambios
- Modales — sin cambios
- Tabs — sin cambios
- Tablas — sin cambios
- Acciones por fila — sin cambios
- Permisos — sin cambios
- Ventas / Cotización / Caja / Crédito — sin cambios

## J. Accesibilidad / baja visión

- El nuevo estilo mantiene contraste texto/fondo: `#93b4f8` sobre fondo oscuro (~`rgba(19,91,236,0.08)`) supera WCAG AA 4.5:1 estimado en fondo `#0f1117`.
- El focus usa `outline` estándar `2px solid #135bec` con `outline-offset: 2px`, alineado con el patrón del sistema en `shared-components.css`.
- El botón no depende solo del color: tiene borde, ícono y texto, cumpliendo criterio WCAG 1.4.1 (uso del color).

## K. Riesgo funcional

Riesgo: **Nulo**. El cambio es exclusivamente visual. No afecta comportamiento, eventos, rutas, permisos ni lógica de negocio.

## L. Validaciones

- `git diff --check`: OK (sin whitespace errors)
- `git status --short`: solo archivos esperados modificados
- Build: ejecutado — resultado documentado en sección M
- Playwright visual: ejecutado sobre `e2e/ui-4e-layout-visual.spec.js` — resultado documentado en sección M

## M. Playwright

Ver resultado en informe final de cierre.

## N. Procesos

Ver sección de procesos en informe final.

## O. Deudas restantes

- La anomalía de `CatalogoController` con permiso de `cotizaciones/view` no se tocó en esta fase.
- Los modales inline no fueron migrados a partials (queda para fase futura).
- El botón "Inventario físico" adyacente usa `btn-erp-ghost` sin diferenciación visual explícita — aceptable, ya que es un enlace de navegación menos frecuente.
- Accesibilidad semántica de modales (role, aria) — queda para MISA-CATALOGO-UX-1B.

## P. Próximo paso recomendado

**MISA-CATALOGO-UX-1B** — Semántica de modales de Catálogo:
- Agregar `role="dialog"`, `aria-modal="true"`, `aria-labelledby` donde corresponda.
- Preservar modales existentes sin reemplazarlos por vistas.
- No tocar backend ni JS salvo necesidad de accesibilidad.
