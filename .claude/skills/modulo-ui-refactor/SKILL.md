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

## Diagnóstico

Clasificar cada hallazgo:

- funcional;
- contrato Razor/backend;
- estructura HTML;
- CSS;
- JavaScript;
- responsive;
- accesibilidad;
- contenido redundante u obsoleto;
- duplicación;
- código posiblemente muerto;
- deuda fuera de scope.

Para cada hallazgo indicar evidencia, impacto y corrección mínima.

No declarar código muerto solo por falta de referencias textuales; verificar carga dinámica, layout, bundling y uso desde JavaScript.
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

## Validación visual

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
2. diagnóstico;
3. archivos modificados y cambio por archivo;
4. build, tests y QA;
5. procesos iniciados o cerrados;
6. riesgos reales;
7. working tree;
8. comando `git add` exacto;
9. siguiente micro-lote, si aporta valor.

Veredicto: `Listo para commit`, `Requiere ajuste` o `Bloqueado`.
