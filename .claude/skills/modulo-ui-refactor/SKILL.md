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
