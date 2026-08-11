---
name: modulo-ui-refactor
description: Auditoría y refactor de un módulo UI de TheBuryProject en ASP.NET MVC .NET 8. Usar cuando el usuario nombre un módulo y pida analizar o modificar sus vistas Razor, CSS o JavaScript, manteniendo la lógica y la coherencia visual del ERP. No usar para tareas exclusivamente backend ni para diseños de marketing.
argument-hint: "[módulo] [analizar|aplicar]"
---

# Módulo UI Refactor

## Objetivo

Analizar o mejorar un módulo concreto sin romper su comportamiento, sus contratos con backend ni la coherencia visual del ERP.

Trabajar sobre:

- vistas y parciales Razor;
- CSS realmente cargado por el módulo;
- JavaScript realmente cargado por el módulo;
- layout, bundles o imports relacionados;
- ViewModels, controllers y tests únicamente para entender contratos e impacto.

## Determinar el modo

Inferir el módulo y el modo a partir del pedido, rutas o archivos mencionados.

- `analizar`, `revisar`, `diagnosticar`, `opinar`: solo diagnóstico.
- `corregir`, `modificar`, `aplicar`, `refactorizar`, `implementar`: se permiten cambios.
- Si el módulo no puede inferirse con evidencia, pedirlo antes de editar.

No volver a pedir datos que ya estén en la conversación o en el repositorio.

## Inicio obligatorio

Antes de auditar una pantalla:

- leer `docs/ui/ERP-UI-STANDARD.md`;
- leer `docs/ui/UI-REFACTOR-STATUS.md`.

1. Leer `AGENTS.md` y `CLAUDE.md`.
2. Ejecutar:

```powershell
git status --short
git diff --stat
```

3. Identificar cambios previos del usuario y no pisarlos.
4. Delimitar el micro-lote.
5. Consultar `codebase-memory-mcp` si hay dependencias cruzadas o camino canónico incierto.
6. Leer directamente los archivos afectados.

## Mapeo del módulo

Localizar con evidencia:

- vistas principales y parciales;
- CSS y JavaScript cargados;
- ids, nombres y `data-*` usados por scripts;
- endpoints y ViewModels relevantes;
- componentes compartidos;
- tests existentes;
- variantes legacy o duplicadas.

No asumir que un archivo con nombre parecido gobierna la pantalla real.

## Auditoría (4 capas)

Ejecutar siempre las 4 capas de `docs/ui/ERP-UI-STANDARD.md` §14 — pasar la primera no
exime de las otras tres. Una pantalla puede estar técnicamente normalizada y aun así
requerir mejoras de UX.

1. **Técnica** — clasificar cada hallazgo: funcional, contrato Razor/backend,
   estructura HTML, CSS, JavaScript, responsive técnico, accesibilidad técnica,
   contenido redundante u obsoleto, duplicación, código posiblemente muerto, deuda
   fuera de scope. Evidencia, impacto y corrección mínima por hallazgo. No declarar
   código muerto solo por falta de referencias textuales; verificar carga dinámica,
   layout, bundling y uso desde JavaScript.
2. **Visual** — jerarquía, densidad, spacing, color, contraste semántico, prioridad
   visual, scanability, redundancia visual (clasificar como útil, accidental o
   cognitiva).
3. **Flujo UX** — la pantalla como tarea real: qué intenta hacer el usuario, qué ve
   primero, qué lo bloquea, acciones compitiendo, pasos o información innecesarios,
   ambigüedad entre guardar/confirmar/aplicar/continuar, dead ends, paridad de
   prioridad funcional en mobile. Sin cambiar reglas de negocio.
4. **Estados reales** — no validar solo happy path: vacío, con datos, completado,
   error, bloqueado, no viable, estado terminal, mobile.

Cierre distingue `Técnicamente: ✅/🟡`, `Visualmente: ✅/🟡`, `Flujo UX: ✅/🟡`. Buena
paridad, responsive y tests no habilitan por sí solos "no hace falta tocarla". Toda
propuesta debe responder al menos una pregunta de valor real de §14 del estándar
(reduce carga cognitiva, aclara prioridad, elimina redundancia/ambigüedad, reduce
pasos, evita errores, mejora lectura/responsive/accesibilidad); si ninguna aplica, no
cambiar.

## Hallazgos priorizados

Priorizar los hallazgos de las 4 capas antes de implementar, no solo listarlos:

- bloqueante — impide completar la tarea o es un error funcional/backend;
- alto — fricción real, redundancia cognitiva o contradicción de acciones (visual vs.
  funcional; no afirmar bug funcional sin verificar la lógica real);
- medio — mejora visual, de flujo o de microcopy con valor real (§14 del estándar)
  pero no bloquea;
- bajo / fuera de scope — deuda documentada, no se implementa en este micro-lote;
  alimenta el roadmap mínimo del entregable.

Implementar como máximo bloqueante + alto en el micro-lote actual, salvo pedido
explícito de ampliar el alcance.

## Apoyo UX/UI especializado

`modulo-ui-refactor` conserva siempre la responsabilidad sobre:

- alcance del micro-lote;
- contratos Razor/backend;
- preservación de comportamiento;
- protección del working tree;
- archivos permitidos;
- implementación;
- validación técnica;
- validación con Playwright.

Las skills visuales especializadas son consultivas.

### UI/UX Pro Max

Usar únicamente cuando el usuario la invoque explícitamente o cuando el pedido indique expresamente que debe participar.

Priorizar consultas relacionadas con:

- accesibilidad;
- interacción;
- responsive;
- formularios;
- tablas y visualización de datos;
- jerarquía;
- estados de interfaz.

Para TheBuryProject:

- usar `html-tailwind` como referencia técnica cuando corresponda;
- priorizar dominios `ux`, `web` y `chart`;
- no usar `--persist`;
- no crear `design-system/MASTER.md`;
- no crear overrides de páginas;
- no considerar sus recomendaciones una fuente de verdad;
- contrastar siempre sus recomendaciones con la interfaz real y los componentes existentes.

### Impeccable
Los hallazgos de Impeccable son candidatos de diagnóstico: no ampliar el micro-lote ni implementar hallazgos adicionales automáticamente.
Usar únicamente mediante invocación explícita y con un comando concreto.

Para módulos existentes priorizar:

- `critique`: análisis UX y heurístico;
- `audit`: accesibilidad, responsive, performance e integridad técnica;
- `harden`: estados límite, errores e internacionalización;
- `adapt`: responsive;
- `clarify`: textos, labels y mensajes;
- `optimize`: performance de UI;
- `polish`: cierre visual conservador.

No ejecutar automáticamente:

- `init`;
- `document`;
- `extract`;
- `hooks`;
- `doctor`;
- `bolder`;
- `delight`;
- `overdrive`.

No crear `PRODUCT.md`, `DESIGN.md`, design systems ni nueva autoridad visual salvo pedido explícito.

En TheBuryProject usar el modo conceptual `Operate`: priorizar scanability, consistencia, expectativas de aplicaciones administrativas, densidad útil y eficiencia de tarea sobre expresividad visual.
### Cuando participan ambas

Este flujo aplica únicamente cuando el usuario haya pedido explícitamente usar ambas skills.

No mezclar sus instrucciones directamente.
Orden:

1. mapear primero la pantalla y sus contratos con `modulo-ui-refactor`;
2. obtener heurísticas relevantes de `ui-ux-pro-max`;
3. usar `impeccable critique` o `impeccable audit` como segunda evaluación;
4. reconciliar hallazgos contra la implementación real del ERP;
5. clasificar y priorizar únicamente los hallazgos demostrables;
6. implementar el micro-lote mínimo.

Ante conflicto, prevalecen:

1. comportamiento y reglas de negocio;
2. contratos existentes;
3. instrucciones de `AGENTS.md` y `CLAUDE.md`;
4. coherencia visual existente del ERP;
5. `modulo-ui-refactor`;
6. recomendaciones de skills especializadas.
## Reglas de modificación

- Preservar reglas de negocio.
- Preservar rutas, bindings, ids, nombres y `data-*`.
- No mover lógica crítica al frontend.
- No duplicar listeners.
- No crear nuevos archivos si uno existente puede asumir la responsabilidad de forma clara.
- No mezclar rework visual con refactor backend salvo necesidad demostrada.
- No aplicar una estética externa al ERP.
- Eliminar redundancias únicamente con evidencia.
- Trabajar por micro-lotes revisables.

Para normalización estructural pura de Razor, usar `normalize-razor-structure`.

Para warnings de Tailwind o editor, usar `clasificacion-warnings` y `tailwind-protocolo`.

## Validación visual (Playwright real)

Cuando el cambio afecte layout, interacción, CSS, JavaScript, modal, drawer, tabla, tabs o responsive:

1. reproducir el estado inicial con Playwright MCP;
2. registrar URL y flujo;
3. aplicar el cambio;
4. repetir el flujo;
5. revisar consola y requests;
6. validar como mínimo:

- 1440x900;
- 1280x720;
- 768x1024;
- 390x844;
- 360x800.

Agregar 1024x720 o 900x720 cuando el problema esté cerca de un breakpoint.

Observar, además de responsive: estado vacío, con datos, errores/bloqueos, prioridad
de acciones, scroll, redundancias visibles, modales, feedback y mobile — no solo el
happy path. No ejecutar acciones destructivas.

No afirmar que quedó responsive si no se probó en navegador.

## Validación técnica

Elegir según impacto:

- `dotnet build` del proyecto afectado;
- tests unitarios o de integración focalizados;
- tests Playwright focalizados;
- `git diff --check`;
- revisión del diff final.

No ejecutar la suite completa salvo cierre de fase, pre-merge o pedido explícito.

## Límites

- No hacer commit ni push sin pedido explícito.
- No usar `git add -A`.
- No tocar stashes.
- No limpiar cambios previos.
- No abrir nuevos frentes fuera del módulo.
- No ocultar errores de validación.
- No repetir comandos colgados indefinidamente.

## Entregable

Usar este orden:

1. resumen y veredicto;
2. diagnóstico por capas (técnica / visual / flujo UX / estados) con hallazgos priorizados;
3. archivos modificados y cambio por archivo;
4. build, tests y QA;
5. procesos iniciados o cerrados;
6. riesgos reales;
7. working tree;
8. comando `git add` exacto;
9. roadmap mínimo (siguiente micro-lote), solo si aporta valor real (§14 del estándar).

Veredicto: `Listo para commit`, `Requiere ajuste` o `Bloqueado`.
