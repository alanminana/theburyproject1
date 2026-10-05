---
name: TheBuryProject ERP
description: ERP web oscuro, sobrio y amigable para uso diario prolongado en un comercio; superficies violeta-negras, acento lima único, contraste alto.
colors:
  accent: "#a5d926"
  accent-hover: "#bce35e"
  on-accent: "#15110a"
  success: "#75d65c"
  success-text: "#99e186"
  warning: "#f59e0b"
  warning-text: "#f8b74a"
  danger: "#f43f5e"
  danger-text: "#f77188"
  info: "#8cd9a2"
  info-text: "#aae3ba"
  violet: "#8f1fad"
  bg: "#0e0b14"
  surface-deep: "#13101b"
  surface: "#181421"
  surface-low: "#1b1628"
  surface-raised: "#1f1a2c"
  surface-high: "#271f3a"
  border-strong: "#3d3355"
  text: "#f4f2f8"
  text-secondary: "#d9d3e6"
  text-muted: "#c1b9d3"
  text-meta: "#afa6c3"
  text-placeholder: "#9d93b5"
typography:
  body:
    fontFamily: "Hanken Grotesk, Inter, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
  title:
    fontFamily: "Hanken Grotesk, Inter, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 700
  label:
    fontFamily: "Hanken Grotesk, Inter, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 700
    letterSpacing: "0.05em"
  mono:
    fontFamily: "JetBrains Mono, ui-monospace, SFMono-Regular, monospace"
    fontSize: "0.8125rem"
    fontWeight: 500
rounded:
  input: "10px"
  button: "12px"
  panel: "12px"
  card: "16px"
  hero: "24px"
  pill: "9999px"
spacing:
  sm: "8px"
  md: "16px"
  lg: "24px"
  control-height: "44px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.button}"
    height: "{spacing.control-height}"
    padding: "0 20px"
  button-primary-hover:
    backgroundColor: "{colors.accent-hover}"
  button-secondary:
    backgroundColor: "{colors.surface-high}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.button}"
    height: "{spacing.control-height}"
    padding: "0 20px"
  input:
    backgroundColor: "{colors.surface-low}"
    textColor: "{colors.text-secondary}"
    rounded: "{rounded.input}"
    height: "{spacing.control-height}"
    padding: "8px 14px"
  card:
    backgroundColor: "{colors.surface-low}"
    rounded: "{rounded.card}"
    padding: "24px"
  hero:
    backgroundColor: "{colors.surface-raised}"
    rounded: "{rounded.hero}"
    padding: "24px"
  badge:
    textColor: "{colors.text-muted}"
    rounded: "{rounded.pill}"
    padding: "3px 10px"
---

# Design System: TheBuryProject ERP

## Overview

**Creative North Star: "El Mostrador Sereno"**

Un sistema oscuro, sobrio y amigable pensado para sesiones largas en un comercio real: el dueño y tres empleados lo usan todo el día para vender, cobrar, abrir caja y seguir créditos. Los fondos son casi negros con un matiz violeta suave, el texto es claro y de alto contraste, y el color vive casi solo en los estados y en un único acento. La prioridad declarada es la comodidad visual, la claridad de la información y la accesibilidad, en especial para usuarios con problemas de vista.

La voz es cálida y amable: radios generosos (10–24px), bordes finos translúcidos en lugar de líneas duras, tipografía humanista (Hanken Grotesk) y copy en español rioplatense. Profesional, tranquilo y confiable; nada agresivo ni recargado. Es una herramienta de trabajo (modo Operate): scanability y consistencia por encima de la expresión.

**Key Characteristics:**
- Fondos oscuros suaves con matiz violeta (h≈258°); nunca negro puro como superficie de contenido.
- Un solo acento (lima) para la acción principal, el foco y la selección activa.
- Profundidad tonal por capas; la única sombra de reposo es el halo del botón primario.
- Controles de 44px de alto (objetivo táctil) y texto mínimo de 12px.
- Todo el color sale de `palette.css`; cambiar la paleta es editar una sola sección.

## Colors

Paleta oscura violeta-neutra con un acento lima y cuatro colores de estado. Toda la jerarquía de grises es una rampa neutra única; los estados se derivan por mezcla del color base.

### Primary
- **Lima Mostrador** (#a5d926): botón primario, anillo de foco, ítem activo de navegación, selección. Se usa con texto oscuro (#15110a) encima, nunca blanco.

### Secondary
- **Verde Ingreso** (#75d65c): éxito, ingreso, confirmado. En texto se usa su tono claro (#99e186).
- **Ámbar Atención** (#f59e0b): pendiente, advertencia. Texto en #f8b74a.
- **Rosa Egreso** (#f43f5e): error, egreso, mora. Texto en #f77188 (el 500 puro no alcanza contraste sobre fondo oscuro).
- **Menta Informativa** (#8cd9a2): información y enlaces.

### Tertiary
- **Violeta Categoría** (#8f1fad): énfasis secundario y categorías; uso escaso.

### Neutral
- **Noche Profunda** (#0e0b14): fondo de página.
- **Superficie Base** (#181421) y **Superficie Baja** (#1b1628): paneles, cards, inputs, tablas.
- **Superficie Elevada** (#1f1a2c) y **Alta** (#271f3a): hero, hover, controles secundarios.
- **Borde Firme** (#3d3355): borde de formularios (más visible que el borde de panel).
- **Tinta** (#f4f2f8) y escala de texto: secundario (#d9d3e6), muted (#c1b9d3), metadato (#afa6c3), placeholder (#9d93b5, el mínimo permitido).
- Bordes de panel: blanco al 7% (4,5% muted, 11% énfasis), no un hex.

### Named Rules
**The One Voice Rule.** El lima aparece solo en acciones principales, foco y selección activa. Si dos cosas en una pantalla compiten por el lima, una de ellas está mal.
**The Floor Contrast Rule.** Todo texto sobre superficie oscura llega a ≥6,4:1 (la escala `fg-1…fg-4`); el texto tenue por debajo de `fg-4` está prohibido.
**The Dark Ink Rule.** Sobre cualquier fondo de acento o de estado claro (lima, ámbar, verde) el texto es tinta oscura, no blanca.
**The Single Source Rule.** Ningún hex/rgb literal fuera de `palette.css`; los módulos consumen `--pal-*`.

## Typography

**Display/Body Font:** Hanken Grotesk (con Inter y system-ui como fallback)
**Label/Mono Font:** JetBrains Mono (cifras e identificadores)

**Character:** Humanista, abierta y legible; la monoespaciada reserva su rigor para importes, códigos y números que se comparan en columna.

### Hierarchy
- **Title** (700, ~1.125rem, tight): encabezados de panel y página.
- **Body** (400–500, 0.875rem/14px, 1.5): texto operativo, celdas, formularios.
- **Label** (700, 0.75rem/12px, +0.05em, mayúsculas): badges, encabezados de columna, metadatos.
- **Mono** (500, 0.8125rem): importes, cuotas, códigos.

### Named Rules
**The 12px Floor Rule.** Nada por debajo de 12px; los tamaños arbitrarios 9–11px se elevan a 12–13px por capa de utilidades.
**The Tabular Money Rule.** Los importes y comparables en columna usan mono/tabular, alineados a la derecha.

## Layout

Aplicación con barra lateral y contenido en un contenedor con padding responsivo (16/24/32px). Las páginas se centran dentro de un shell de ancho máximo (sm 960, md 1120, lg 1280, xl 1480px, o fluido para tableros densos). Cada página abre con un encabezado (título, subtítulo, acciones) y se compone de hero opcional, panel de filtros, grillas de KPIs y tablas o formularios. Ritmo de espaciado en múltiplos de 4/8px (8, 16, 24). En mobile las tablas se reorganizan a tarjetas (componente opt-in de table-cards), los wizards usan barras de acción pegajosas y los inputs suben a 16px para evitar el zoom de iOS. Los breakpoints se resuelven con media queries y container queries según el host.

## Elevation & Depth

Sistema tonal por capas: la profundidad sale de la rampa neutra (950 → 900 → 875 → 850 → 800) y de bordes blancos translúcidos, no de sombras. La única sombra de reposo es el halo lima del botón primario; el foco usa anillo, no sombra.

### Shadow Vocabulary
- **Halo primario** (`box-shadow: 0 4px 14px color-mix(in srgb, #a5d926 22%, transparent)`): solo en el botón primario.
- **Anillo de foco de campo** (`box-shadow: 0 0 0 3px color-mix(in srgb, #a5d926 18%, transparent)`): foco de input/select junto al borde lima.

### Named Rules
**The Flat-By-Default Rule.** Paneles, cards y tablas son planos en reposo; se separan por tono y borde. Una sombra nueva necesita justificación de estado.

## Shapes

Lenguaje de formas redondeadas y amables, con escalado por tamaño de superficie: inputs 10px, botones y paneles secundarios 12px, cards 16px, hero 24px, badges y chips en píldora completa. Bordes siempre de 1px; sin recortes ni geometrías especiales. Los iconos son Material Symbols Outlined (16/18/20/24px).

## Components

### Buttons
- **Shape:** esquinas suaves (12px), alto 44px, texto 14px.
- **Primary:** fondo lima, tinta oscura, peso 700, halo lima; hover aclara a #bce35e.
- **Secondary:** superficie alta (#271f3a), borde #3d3355, texto #e6e2f0, peso 600.
- **Ghost / Success / Warning / Danger / Icon:** variantes del mismo contorno; deshabilitado en gris neutro sin sombra.
- **Focus:** anillo de 2px lima con offset de 2px en todos.

### Chips y Badges
- **Style:** píldora, 12px, mayúsculas con +0,05em en badges; fondo tintado al 10–12% del color con borde al 20%.
- **State:** primary (lima), success, warning, danger (sin borde), info, neutral.

### Cards / Containers
- **Corner Style:** 16px (card), 12px (panel), 24px (hero).
- **Background:** #1b1628 (card/panel/tabla), #1f1a2c (hero).
- **Border:** 1px #271f3a.
- **Internal Padding:** 24px (card), 16px (métrica), 24–32px (hero).
- **Metric cards:** variantes semánticas con tinte al 6% y borde al 20%.

### Inputs / Fields
- **Style:** fondo #1b1628, borde #3d3355, radio 10px, alto 44px, texto 14px.
- **Focus:** borde lima + anillo de 3px lima al 18%.
- **Disabled:** opacidad 50%, sin eventos. Placeholder en #9d93b5.

### Navigation
- Barra lateral oscura; el ítem activo lleva una barra interior lima de 3px a la izquierda. En mobile colapsa y el título de página se muestra en el header.

### Tablas
- Contenedor con radio 16px y borde fino; filas separadas por borde translúcido, acciones de fila discretas (row-action) que ganan color solo al hover; en mobile se transforman en tarjetas.

## Do's and Don'ts

### Do:
- **Do** consumir color solo vía `--pal-*` / `--erp-*` / `--ml-*`; para tintes usar `color-mix(in srgb, var(--pal-accent) 13%, transparent)`.
- **Do** usar texto oscuro (#15110a) sobre el acento lima y sobre estados claros.
- **Do** dar a todo control 44px de alto y foco visible de 2px lima.
- **Do** mantener texto ≥12px y ≥6,4:1 de contraste sobre superficies oscuras.
- **Do** reutilizar los componentes canónicos (`btn-erp-*`, `card-erp`, `input-erp`, `badge-erp`, `chip-erp`, `table-erp-wrapper`) antes de crear variantes por módulo.

### Don't:
- **Don't** introducir un segundo design system ni hex literales fuera de `palette.css`.
- **Don't** usar blanco puro de texto sobre lima, ámbar o verde.
- **Don't** agregar sombras de reposo a cards o paneles; la profundidad es tonal.
- **Don't** usar el lima como decoración ni en más de una acción principal por pantalla.
- **Don't** recurrir a rojo 500 puro para texto sobre fondo oscuro; usar el tono claro (`danger-400`).
- **Don't** hacer la interfaz agresiva ni recargada: el criterio es comodidad en uso prolongado.

> **Tensión abierta con la intención del usuario.** El brief pide acentos "discretos, no chillones", pero el acento implementado (#a5d926) es un lima de alta saturación y luminosidad. Este documento registra el sistema tal como está; si se quiere suavizar, es un cambio de una línea en `--pal-accent` (sección 1 de `palette.css`) y requiere revisar el contraste de `--pal-on-accent`.
