# TheBuryProject — instrucciones compartidas para agentes

## Proyecto

ERP en ASP.NET MVC .NET 8 con C#, EF Core, Razor, Tailwind/CSS, JavaScript y Playwright.

Actuar como desarrollador senior. Priorizar mantenibilidad, estabilidad, accesibilidad y mejoras incrementales seguras.

## Método de trabajo

1. Revisar `git status --short`, `git diff --stat` y el diff relevante.
2. Identificar el camino canónico y clasificar lo tocado como canónico, legacy, duplicado o incierto.
3. Delimitar un único micro-lote y no mezclar problemas independientes.
4. Leer directamente los archivos afectados antes de modificar.
5. Reutilizar servicios, helpers, estilos y scripts existentes antes de crear otros.
6. Implementar el cambio mínimo que resuelva el pedido.
7. Validar según el impacto.
8. Revisar el diff final y reportar evidencia, riesgos y estado del working tree.

No afirmar que algo funciona sin evidencia suficiente.

## Scope y seguridad

- Mantenerse dentro del pedido.
- No borrar código legacy o incierto sin verificar referencias, DI, rutas, vistas, scripts y tests.
- No modificar reglas de negocio sin diagnóstico y cobertura adecuada.
- No introducir dependencias sin revisar primero las versiones y soluciones existentes.
- No tocar migraciones, entidades, backend, frontend y tests juntos salvo que el alcance funcional realmente lo exija.
- No revertir ni sobrescribir cambios previos del usuario.
- No usar archivos de credenciales, autenticación o uploads como contexto.
- Para datos locales de prueba, leer `.agent-local.md` solo cuando sea necesario. Nunca versionarlo.

## Arquitectura y backend

- El backend es la autoridad para cálculos y reglas sensibles.
- Mantener controllers orientados a coordinación.
- Evitar lógica de negocio en controllers, Razor o JavaScript.
- Reutilizar services, fachadas, helpers y mapeos.
- Verificar transacciones, concurrencia e idempotencia cuando corresponda.
- No crear abstracciones de un solo uso sin beneficio claro.
- No duplicar validaciones de negocio entre capas sin una razón explícita.

## Frontend y UI

- Preservar ids, nombres, bindings, rutas y atributos `data-*` que formen parte de contratos existentes.
- No cambiar reglas de negocio por una mejora visual.
- Mantener coherencia con el resto del ERP, legibilidad, contraste, foco visible y navegación por teclado.
- Evitar rediseños decorativos, animaciones innecesarias y estilos ajenos al sistema existente.
- Antes de tocar CSS o Razor visual, reproducir el problema en navegador cuando sea posible.
- No afirmar que una pantalla quedó responsive sin validarla en navegador real.
- No duplicar listeners ni mover lógica crítica al frontend.

Para cualquier trabajo UI/UX:

1. leer `docs/ui/ERP-UI-STANDARD.md`;
2. leer `docs/ui/UI-REFACTOR-STATUS.md`;
3. usar `.claude/skills/modulo-ui-refactor/SKILL.md`;
4. respetar pantallas marcadas como cerradas;
5. actualizar STATUS al cerrar una pantalla;
6. actualizar STANDARD solo ante reglas reusables nuevas.

## Herramientas

Usar la herramienta mínima necesaria:

- `codebase-memory-mcp`: dependencias cruzadas, símbolos, referencias, impacto y camino canónico incierto.
- Context7: documentación externa actual, después de verificar la versión instalada en el repositorio.
- Playwright MCP: reproducción y QA interactivo en navegador.
- Tests Playwright versionados: regresiones repetibles y flujos críticos.
- Lectura directa: autoridad final sobre el código real.

Ninguna herramienta reemplaza la lectura de los archivos afectados.

## Validación

Elegir validaciones proporcionales:

- build del proyecto afectado;
- tests focalizados;
- pruebas de integración cuando cambia una regla de negocio;
- Playwright para interacción, JavaScript, responsive o accesibilidad;
- consola y requests para flujos de navegador;
- `git diff --check` antes de cerrar.

No ejecutar la suite completa salvo pre-merge, cierre de fase o pedido explícito.

Si un comando queda colgado, detenerlo, diagnosticar y no repetirlo indefinidamente. Cerrar únicamente procesos iniciados por la ejecución actual.

## Git

- No usar `git add -A` ni `git add --all`.
- No hacer commit ni push sin pedido explícito.
- No limpiar el working tree destruyendo trabajo previo.
- Agregar únicamente archivos del scope.
- Antes de cerrar, ejecutar:

```powershell
git diff --check
git diff --stat
git status --short
```

## Entregable

Responder de forma breve y operativa:

1. resumen y veredicto;
2. diagnóstico, solo si fue necesario;
3. cambios aplicados;
4. validaciones ejecutadas;
5. riesgos reales;
6. working tree;
7. comando `git add` exacto;
8. siguiente micro-lote, solo si aporta valor.

Veredictos válidos:

- `Listo para commit`
- `Requiere ajuste`
- `Bloqueado`
