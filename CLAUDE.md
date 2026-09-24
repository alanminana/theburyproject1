# Instrucciones del proyecto (Claude Code)

`AGENTS.md` es la fuente compartida para Claude Code, Codex y otros agentes. No duplicar aquí sus reglas.

Las instrucciones específicas por carpeta deben vivir en un `CLAUDE.md` anidado o en `.claude/rules/`. Los procedimientos ocasionales deben vivir en skills.

## Skills del proyecto

Claude puede invocar automáticamente estas skills cuando el pedido coincide claramente:

- `modulo-ui-refactor`
- `normalize-razor-structure`
- `clasificacion-warnings`
- `codebase-memory-protocolo`
- `context7-protocolo`
- `playwright-protocolo`
- `tailwind-protocolo`
- `timeouts-y-procesos`
- `ui-responsive-protocolo`
- `csharp-testing`
- `security-review`

`ui-module` es una skill manual (`disable-model-invocation`): Claude no la activa sola. `/ui-module <módulo>` es el pipeline completo de auditoría UX/UI, implementación, QA visual y polish de una pantalla existente (ver "UX/UI especializado").

`ui-ux-pro-max`, `impeccable` y `dotnet-patterns` son consultoras manuales (user-invocable-only en `.claude/settings.json`): Claude no las invoca automáticamente, solo mediante `/ui-ux-pro-max`, `/impeccable` o `/dotnet-patterns` cuando el usuario lo pida expresamente. Ver reglas en "UX/UI especializado" y ".NET especializado" más abajo.

## UI/UX

Para cualquier trabajo UI/UX:

- leer `docs/ui/ERP-UI-STANDARD.md`;
- leer `docs/ui/UI-REFACTOR-STATUS.md`;
- usar `.claude/skills/modulo-ui-refactor/SKILL.md`;
- preservar reglas de negocio, contratos backend, permisos y funcionalidad salvo pedido explícito;
- actualizar STATUS al cerrar una pantalla;
- actualizar STANDARD solo ante reglas reutilizables nuevas.

Toda auditoría UI/UX distingue técnica, visual, flujo UX y estados reales (ver `ERP-UI-STANDARD.md` §14).

### Pantallas cerradas

- Una pantalla marcada como cerrada en `UI-REFACTOR-STATUS.md` no debe modificarse incidentalmente.
- Si el usuario pide explícitamente revisar, corregir, rediseñar, comparar o volver a trabajar sobre esa pantalla, queda reabierta para el alcance solicitado.
- El estado cerrado no puede usarse para rechazar una corrección visual solicitada explícitamente por el usuario.

### Referencias visuales

Cuando el usuario proporciona una captura, mockup o imagen y pide que una pantalla se vea como esa referencia:

- la referencia se considera una fuente visual autorizada para esa tarea;
- puede justificar cambios de layout, grid, spacing, jerarquía, densidad, cards, tabs, agrupación de información, composición y responsive;
- prevalece sobre la apariencia legacy actual de esa pantalla;
- no autoriza cambios de reglas de negocio, permisos, contratos backend, estados inexistentes, datos inventados ni comportamiento funcional no solicitado.

La referencia debe adaptarse a la identidad global existente del ERP, no copiarse de forma que introduzca un segundo design system.

### Criterio de éxito visual

En tareas cuyo objetivo principal sea visual o UX:

- build verde no implica éxito visual;
- tests verdes no implican éxito visual;
- ausencia de overflow no implica éxito visual;
- cumplimiento técnico del design system no implica éxito visual.

El criterio principal de aceptación es que el resultado renderizado cumpla el objetivo visual solicitado.

Antes de declarar una tarea visual como terminada:

1. abrir la pantalla real;
2. capturarla en el viewport objetivo;
3. compararla con la referencia o con el objetivo solicitado;
4. identificar diferencias visuales relevantes;
5. corregir las diferencias que estén dentro del alcance;
6. repetir la comparación;
7. recién después realizar el cierre técnico y proponer commit.

No declarar "Listo para commit" únicamente porque build, tests, consola y responsive técnico estén correctos. El cierre queda además sujeto a "Regla de completitud" y "Cierre visual obligatorio".

### Libertad de refactor visual

Cuando el usuario pide explícitamente un rediseño o convergencia visual, se permite modificar dentro de esa pantalla:

- layout;
- grids;
- composición;
- jerarquía;
- spacing;
- tamaños;
- densidad;
- agrupación de información;
- cards;
- tabs;
- orden visual;
- presentación responsive;
- componentes visuales locales.

Esto no requiere autorización adicional mientras:

- no cambien reglas de negocio;
- no se elimine funcionalidad;
- no cambien permisos;
- no se alteren contratos backend;
- no se introduzcan dependencias nuevas innecesarias.

No interpretar "bajo riesgo" como hacer el menor cambio visual posible cuando eso impida alcanzar el resultado solicitado.

### Estrategia de iteración

Para un rediseño visual coherente, preferir:

1. diagnóstico corto;
2. implementación completa dentro del alcance;
3. captura real;
4. comparación visual;
5. una ronda de corrección;
6. build/tests/QA;
7. cierre (sujeto a "Regla de completitud" y "Cierre visual obligatorio").

Usar micro-lotes únicamente cuando exista un riesgo funcional, técnico o de regresión que justifique separarlos.

No fragmentar artificialmente una misma mejora visual en múltiples lotes.

## MCP

- `codebase-memory-mcp`: configuración personal del usuario.
- `context7`, `playwright` y `github`: configuración compartida del proyecto.
- No usar un MCP filesystem; Claude Code ya dispone de herramientas nativas de archivos.
- Verificar primero la versión instalada y el código real antes de aplicar información externa.
- Si al usar el MCP playwright ya existen procesos/sesión de Playwright abiertos porque otro agente los está usando y no terminó el proceso correctamente (no se puede asumir que esté libre para cerrar o matar), no forzar el cierre de esa sesión ni reutilizarla: abrir una nueva sesión de Playwright propia y continuar en esa.

## Cambios de configuración

`/doctor` sirve para auditar y proponer. No aplicar ni aceptar cambios de configuración sin revisar después `git diff` y los servidores o skills afectados.

## UX/UI especializado

Para refactor de módulos existentes:

- `modulo-ui-refactor` es el orquestador principal de arquitectura, contratos, protección del working tree, implementación y validación técnica.
- `modulo-ui-refactor` no puede rebajar, reinterpretar ni cerrar antes de tiempo un objetivo visual explícito del usuario.
- Una referencia visual proporcionada explícitamente por el usuario tiene autoridad visual dentro del alcance solicitado.

Pipeline `/ui-module <módulo>`:

- `modulo-ui-refactor` sigue siendo el orquestador principal de implementación.
- `ux-heuristics` (Wondel) es consultora de usabilidad Nielsen/Krug; `impeccable` es consultora de crítica y terminación visual.
- `ui-module` solo orquesta el pipeline; no introduce reglas propias de diseño ni cambia la jerarquía de autoridad (las consultoras siguen en el último nivel).
- Ejecutar `/ui-module` autoriza internamente esas consultoras y Playwright para esa ejecución; fuera de `/ui-module` siguen siendo manuales.
- La combinación `ux-heuristics + impeccable` solo ocurre dentro de este pipeline o por pedido explícito; no ejecutar varias skills visuales indiscriminadamente.
- Secuencia: contexto → `ux-heuristics` → Impeccable critique → consolidación → `modulo-ui-refactor` → Playwright → Impeccable polish → Playwright final → cierre.
- `/ui-module` es un pipeline de ejecución completa y sus fases obligatorias (`ux-heuristics`, `impeccable critique`, implementación con `modulo-ui-refactor`, QA visual real, `impeccable polish`, QA visual final) son gates. Si alguna no puede ejecutarse, el módulo no puede quedar `Listo para commit`. No sustituirlas por "lo revisé manualmente", "hice una pasada equivalente", "los tests están verdes" ni "visualmente parece correcto".
- Si una herramienta requerida está rota, ausente o modificada en el working tree: diagnosticarlo, intentar resolverlo sin pisar trabajo ajeno, y si no es posible de forma segura continuar solo con lo permitido y marcar `REQUIERE AJUSTE`.
- Antes de implementar, construir la "Matriz de cobertura del módulo".

`ui-ux-pro-max` e `impeccable` son herramientas consultivas y no reemplazan las reglas del proyecto, el diseño global existente ni las reglas de negocio.

No combinar múltiples skills visuales salvo pedido explícito o necesidad concreta.

- `ui-ux-pro-max` puede usarse para heurísticas de UX, accesibilidad, responsive, tablas, formularios y visualización de datos.
- No usar `--persist` de `ui-ux-pro-max` ni crear `design-system/MASTER.md` o overrides de páginas salvo pedido explícito.
- `impeccable` debe invocarse con un comando concreto. Para este ERP priorizar `audit`, `critique`, `harden`, `adapt`, `clarify`, `optimize` y `polish`.
- No ejecutar automáticamente `impeccable init`, `document`, `extract`, `hooks`, `doctor`, `bolder`, `delight` ni `overdrive`.

Las recomendaciones externas no pueden reemplazar reglas de negocio ni la identidad global del ERP.

Cuando el usuario proporciona explícitamente una captura o mockup como objetivo, esa referencia sí puede justificar cambios de composición, layout, spacing, densidad, jerarquía, agrupación y responsive dentro de esa pantalla.

No declarar una pantalla visualmente cerrada sin revisar el resultado renderizado.

Si el usuario expresa insatisfacción visual reiterada, la pantalla debe reabrirse para ese alcance y compararse nuevamente contra la evidencia visual aportada; no responder solo con build/tests o con el estado previo de cierre.

Cuando el usuario pide explícitamente una calidad visual alta, 10/10, polish, critique o una convergencia fuerte contra referencia, no dejar diferencias visuales relevantes para un siguiente micro-lote si pueden resolverse de forma segura en la misma iteración.

### Criterio de autoridad visual

Ante una tarea de UI/UX, la prioridad debe interpretarse así:

1. reglas de negocio y comportamiento funcional real;
2. contratos backend, permisos y datos reales;
3. instrucciones explícitas actuales del usuario;
4. referencia visual explícitamente proporcionada por el usuario;
5. `ERP-UI-STANDARD.md`;
6. coherencia visual global existente del ERP;
7. `modulo-ui-refactor`;
8. recomendaciones de skills consultivas.

La coherencia existente del ERP no debe usarse para conservar defectos visuales legacy cuando el usuario pide explícitamente corregirlos.

### Cierre visual obligatorio

No cerrar una tarea visual por cumplimiento técnico solamente.

Antes de usar expresiones como:

- "Listo para commit";
- "cerrado";
- "terminado";
- "completo";
- "10/10";

Claude debe haber revisado el resultado renderizado real y, cuando exista referencia visual, comparado la pantalla contra ella.

Si todavía quedan diferencias relevantes de composición, proporción, densidad, jerarquía, spacing, alineación, aprovechamiento del viewport o responsive, el veredicto debe ser `REQUIERE AJUSTE`.

Además de lo anterior, las expresiones "cerrado", "terminado", "completo", "10/10" y "Listo para commit" están prohibidas mientras quede cualquier gate obligatorio pendiente (ver "Regla de completitud"). Antes de usarlas, comprobar que:

- no quedan gates obligatorios pendientes;
- no quedan superficies relevantes en `NO VALIDADA`, salvo excepciones justificadas expresamente aceptadas por el usuario;
- las viewports obligatorias fueron revisadas;
- el resultado renderizado real fue inspeccionado;
- las fases formales exigidas por el pipeline fueron realmente ejecutadas y no se sustituyeron por revisiones informales;
- los tests técnicos aplicables fueron ejecutados;
- no quedan regresiones conocidas;
- `UI-REFACTOR-STATUS.md` refleja el estado real.

Si falta cualquiera de esos puntos, el veredicto es `REQUIERE AJUSTE` y se lista exactamente qué falta.

### Regla de completitud

Las fases obligatorias definidas por el flujo activo (`CLAUDE.md`, skills invocadas o pipeline `/ui-module`) son gates de cierre, no sugerencias. Una fase obligatoria no puede omitirse silenciosamente ni sustituirse por una validación distinta. Por ejemplo:

- una revisión manual no sustituye `impeccable critique`;
- una revisión visual informal no sustituye `impeccable polish` cuando esa fase está requerida;
- build verde o tests verdes no sustituyen QA visual;
- medir overflow por script no sustituye la captura visual requerida;
- revisar Create no valida Edit; revisar Index no valida drawers, modales ni pantallas relacionadas;
- revisar desktop no valida mobile; revisar una viewport no valida las demás;
- la ausencia de datos no convierte un estado en validado;
- un CI verde no prueba por sí solo que el objetivo visual o UX se cumplió.

Si una fase obligatoria no puede ejecutarse:

1. intentar resolver el impedimento de forma segura;
2. usar los mecanismos ya existentes del proyecto;
3. documentar exactamente qué bloquea la validación;
4. continuar únicamente con lo que siga siendo seguro;
5. marcar el resultado final como `REQUIERE AJUSTE`.

### Matriz de cobertura del módulo

En refactors o revisiones de módulos completos, antes de implementar, inventariar las superficies reales: Index, Details, Create, Edit, Delete, drawers, modales, tabs, paneles, vistas secundarias, estados vacío/error/success/loading, variantes por permisos relevantes, desktop/mobile y flujos principales asociados.

Registrar por cada superficie aplicable: nombre, estado/variante, desktop, mobile, interacción principal, accesibilidad relevante, validación técnica aplicable y resultado.

Resultados permitidos: `VALIDADA`, `NO VALIDADA`, `NO APLICA`. Una superficie no revisada queda `NO VALIDADA`. Nunca inferir que una superficie está validada porque otra similar funciona.

### Estados y datos de prueba

Si faltan datos para representar un estado real, orden de preferencia:

1. datos existentes;
2. seeds/fixtures ya disponibles;
3. datos de desarrollo seguros;
4. datos temporales de QA, si el proyecto lo permite;
5. setup existente de tests;
6. si no puede reproducirse de forma segura, marcar el estado como `NO VALIDADO`.

No inventar datos ni alterar datos que deban preservarse. "No había datos → no lo probé → cerrado" no es aceptable: un estado relevante no reproducible queda explícitamente pendiente.

### Acciones destructivas

Delete, dar de baja, cancelar, anular, eliminar relaciones u otra operación destructiva no se ejecutan sobre datos que deban preservarse solo para completar QA. Preferir fixture, entidad temporal, base de desarrollo reproducible, test automatizado o rollback/transacción si existe un mecanismo seguro.

Si no existe una forma segura: validar visualmente hasta el punto previo a la acción, verificar permisos, copy, confirmación y estado previo, y marcar la acción final como `NO VALIDADA` (nunca validada por inferencia).

### Protección del working tree

No modificar, restaurar, borrar, resetear ni incluir en commits cambios ajenos. Con archivos de modificaciones mezcladas: identificar cuáles pertenecen a la tarea, usar staging selectivo, no asumir autoría de cambios previos. Un working tree sucio no es motivo para omitir QA; sí lo es para evitar operaciones destructivas sobre esos archivos.

### Informe final obligatorio

En tareas UI/UX relevantes, el cierre incluye de forma compacta: módulo/pantallas trabajadas, superficies validadas, superficies no validadas, viewports probados, tests/checks ejecutados, fases formales ejecutadas, riesgos pendientes y el estado final:

`Estado: LISTO PARA COMMIT` o `Estado: REQUIERE AJUSTE` (en este caso, listar los pendientes concretos).

### Evitar sobrevalidación

La validación técnica debe ser proporcional al cambio.

- No crear tests adicionales para cada ajuste puramente visual si no protegen un contrato real, una interacción o una regresión demostrada.
- No usar build/tests verdes como sustituto de evaluación visual.
- No convertir un ajuste visual simple en una ampliación innecesaria de cobertura automatizada.

Proporcionalidad por tipo de cambio (esto no obliga a correr la suite completa del repositorio ante un cambio visual menor, ni exime de los gates de "Regla de completitud"):

- cambio puramente visual: validación visual y técnica proporcional;
- cambio de interacción: tests/interacciones relacionados;
- cambio de reglas, datos, permisos o contratos: tests técnicos relevantes más QA visual correspondiente;
- cambio con contrato e2e existente: no romper ni eliminar el comportamiento porque parezca visualmente redundante; si un e2e demuestra que forma parte de un contrato real, conservarlo o actualizar el contrato solo con una decisión explícita que lo autorice.

## .NET especializado

Componentes adaptados de affaan-m/ECC a la arquitectura real del ERP (services sobre `AppDbContext`, sin Repository Pattern; stack de test real sin FluentAssertions/Moq/NSubstitute/Testcontainers/Bogus):

- `dotnet-patterns`: referencia consultiva manual para patrones C#/.NET (`/dotnet-patterns`).
- `csharp-testing`: apoyo para tests C# con el stack real de `TheBuryProyect.Tests` (xUnit, `CustomWebApplicationFactory`, SQLite en memoria, stubs manuales); se activa solo ante tareas de testing C#.
- `.claude/agents/csharp-reviewer.md`: agente de revisión C#/.NET; invocación deliberada, no automática en todo cambio .cs.

El código real, `AGENTS.md`/`CLAUDE.md` y las skills propias del proyecto prevalecen siempre sobre estas recomendaciones adaptadas de ECC.
