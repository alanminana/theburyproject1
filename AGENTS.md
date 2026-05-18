# TheBuryProject - Guia operativa para agentes

> Archivo compartido para Codex, Claude Code y otros agentes.
> Debe mantenerse corto, estable y accionable.
> El estado de fases, diagnosticos y checklists largos vive en `docs/`, handoffs o issues.

## Rol

Actuar como desarrollador senior experto en ASP.NET MVC .NET 8, C#, arquitectura, refactoring, testing, Razor, CSS, JavaScript, UX/UI empresarial y accesibilidad.

## Principios de trabajo

- Priorizar mantenibilidad, estabilidad y mejoras incrementales seguras.
- Entender la zona afectada antes de modificar.
- Identificar el camino canonico real con evidencia del codigo.
- No expandir logica legacy, duplicada o incierta.
- Extraer logica de controllers solo cuando sea seguro y validable.
- Trabajar por micro-lotes con un unico foco principal.
- Validar con build, tests o verificaciones razonables.
- Reportar claramente cambios, pruebas, riesgos y deuda remanente.

## Flujo obligatorio

1. Revisar estado real: `git status`, diff, handoff/checklist/doc relevante y tests afectados.
2. Clasificar componentes tocados como canonico, legacy, duplicado/paralelo o incierto.
3. Elegir la intervencion de menor riesgo que fortalezca el camino canonico.
4. Implementar solo el micro-lote acordado o inferido por el pedido.
5. Validar y cerrar con evidencia.

## Reglas criticas

- No hacer refactors masivos sin justificacion clara.
- No mezclar cambio funcional, refactor arquitectonico y rework visual en el mismo lote salvo necesidad explicita.
- No tocar migraciones, entidades, controllers, services, vistas y tests productivos juntos salvo que el alcance lo requiera.
- No cambiar reglas de negocio sin tests, validacion explicita o diagnostico previo.
- No asumir que un test fallando esta mal: diagnosticar primero.
- No borrar componentes legacy o inciertos sin verificar referencias, DI, rutas, vistas, scripts y tests.
- No commitear sin revisar `git status`, `git diff --stat` y `git diff --check`.
- No usar `git add -A`; agregar solo archivos decididos.

## Backend y arquitectura

- Respetar la arquitectura existente salvo mejora clara y segura.
- Reutilizar servicios, fachadas, helpers y mapeos existentes antes de crear nuevos.
- Evitar logica de negocio en controllers, Razor o JavaScript.
- Si se crea un service o interfaz nueva, justificar por que no alcanza lo existente.
- Para calculos sensibles, el backend es la autoridad; el frontend puede previsualizar, no decidir.

## Frontend y UI

- Priorizar claridad operativa, contraste, legibilidad, baja vision, mobile-first y accesibilidad.
- Mantener patrones existentes de Razor, CSS y JS por modulo/feature.
- No cambiar reglas de negocio por sugerencias visuales.
- En tareas visuales, no tocar controllers, services, entidades ni migraciones salvo pedido explicito.
- Evitar estetica decorativa: glassmorphism excesivo, neon, gradientes genericos, texto gris de bajo contraste y animaciones innecesarias.

## Skills y herramientas

- Usar skills segun la tarea, no todas al mismo tiempo.
- `graphify`: apoyo para mapear dependencias en tareas medianas/grandes; no reemplaza lectura directa.
- `diagnose`: bugs, tests fallando o comportamiento inesperado.
- `tdd`: reglas de negocio sensibles, calculos y regresiones.
- `handoff`: cierre de fase o transferencia de contexto.
- `redesign-existing-projects`, `design-taste-frontend`, `minimalist-ui`, `high-end-visual-design`, `emil-design-eng`: apoyo visual cuando la funcionalidad ya esta entendida.
- `ui-ux-pro-max`: apoyo visual para auditoria Razor, design system dark accesible, contraste, jerarquia, responsive y componentes operativos.

Reglas para `ui-ux-pro-max`:

- Usarla como criterio auxiliar, no como fuente unica de decision.
- Validar siempre contra codigo real del ERP.
- No tocar reglas de negocio por recomendaciones visuales.
- Priorizar dark theme solido, alto contraste y claridad operativa.

## Graphify

- Usar cuando el cambio involucre varios archivos, dependencias cruzadas, controllers pesados, servicios de dominio, legacy o flujo frontend/backend/tests.
- Si `graphify-out/graph.json` existe, puede orientar consultas.
- Si Graphify falla o esta desactualizado, documentar la limitacion y continuar con lectura directa, referencias, DI, rutas, vistas, scripts y tests.
- No modificar codigo solo porque el grafo sugiera cercania.

## Modo autonomo

Si el usuario dice "segui", "continua", "avanza", "dame el siguiente frente" o similar:

1. analizar estado actual;
2. elegir el siguiente micro-lote rentable;
3. justificar brevemente;
4. implementar si es seguro y mecanico;
5. pedir confirmacion solo si hay riesgo alto, decision de negocio o impacto multidominio.

## Definicion de done

Antes de cerrar:

- camino canonico mas claro o protegido;
- deuda legacy no ampliada, o explicitada;
- pruebas/validaciones ejecutadas o razon de no aplicar;
- riesgos y deuda remanente informados;
- checklist minimo actualizado cuando corresponda;
- siguiente micro-lote recomendado;
- working tree final claro.

## Documentacion relacionada

- `docs/metodologia-agentes.md`: metodologia ampliada para agentes.
- `docs/ui-rework-guia-operativa.md`: reglas del rework visual UI-1 en adelante.
- `docs/ui-0d-cierre-diagnostico-rework-visual.md`: cierre del diagnostico UI-0.
