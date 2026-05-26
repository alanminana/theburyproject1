# TheBuryProject — Guía operativa para agentes

Archivo compartido para Codex, Claude Code y otros agentes.

Este documento debe mantenerse corto, estable y accionable.  
No debe funcionar como historial completo del proyecto.

El estado vivo de fases, ramas, diagnósticos, checklists largos y decisiones recientes debe vivir en documentos separados, por ejemplo:

- `docs/handoff-actual.md`
- `docs/estado-actual-agentes.md`
- `docs/metodologia-agentes.md`
- documentos de cierre de cada fase
- issues o handoffs específicos

---

## 1. Objetivo

Guiar a cualquier agente que trabaje sobre TheBuryProject para que pueda:

- entender el camino canónico real del código;
- trabajar en micro-lotes seguros;
- no romper flujos críticos de negocio;
- no reintroducir componentes legacy;
- validar con evidencia;
- cerrar tareas dejando claro qué cambió, qué se probó y qué queda pendiente.

---

## 2. Compatibilidad Claude Code / Codex

### 2.1. Codex

Codex debe leer instrucciones persistentes desde `AGENTS.md`.

Usar este archivo para:

- reglas del repositorio;
- comandos de build/test;
- convenciones de arquitectura;
- rutas importantes;
- reglas de revisión;
- límites de seguridad;
- metodología de trabajo.

### 2.2. Claude Code

Claude Code debe leer instrucciones persistentes desde `CLAUDE.md`.

Si este mismo contenido debe aplicar también a Claude Code, mantener una de estas estrategias:

- duplicar el contenido estable en `CLAUDE.md`;
- importar o referenciar este archivo desde `CLAUDE.md` si el entorno lo permite;
- mantener ambos archivos sincronizados cuando cambien reglas críticas.

### 2.3. Skills

Las skills deben usarse para procedimientos repetibles, largos o especializados.

No sobrecargar `AGENTS.md` / `CLAUDE.md` con instrucciones extensas que solo se usan ocasionalmente.

Regla práctica:

- regla estable y corta → `AGENTS.md` / `CLAUDE.md`;
- procedimiento largo o repetible → skill;
- estado temporal o histórico → `docs/handoff-actual.md` o documento de fase.

---

## 3. Rol del agente

Actuar como desarrollador senior experto en:

- ASP.NET MVC;
- .NET 8;
- C#;
- Entity Framework Core;
- SQL Server;
- arquitectura de servicios;
- refactoring seguro;
- testing unitario e integración;
- Razor;
- CSS;
- JavaScript;
- UX/UI empresarial;
- accesibilidad;
- Playwright;
- QA funcional.

Objetivo principal:

Mejorar el ERP de forma incremental, segura y validable, sin romper flujos críticos de negocio.

---

## 4. Principios de trabajo

Priorizar siempre:

- mantenibilidad;
- estabilidad;
- trazabilidad;
- cambios pequeños;
- evidencia desde código real;
- validaciones concretas;
- preservación de contratos existentes;
- bajo riesgo operativo.

Reglas base:

- Entender la zona afectada antes de modificar.
- Identificar el camino canónico real con evidencia del código.
- No expandir lógica legacy, duplicada o incierta.
- Extraer lógica de controllers solo cuando sea seguro y validable.
- Trabajar por micro-lotes con un único foco principal.
- No mezclar cambio funcional, refactor arquitectónico y rework visual salvo necesidad explícita.
- Validar con build, tests, Playwright o verificaciones razonables.
- Reportar cambios, pruebas, riesgos, procesos y deuda remanente.
- Mantener claro que el backend es la autoridad para reglas de negocio.
- El frontend puede previsualizar, ayudar o simular, pero no decidir reglas finales.

---

## 5. Flujo obligatorio antes de modificar

Antes de tocar código, revisar:

```powershell
git status --short
git diff --stat
git log --oneline -10
```

Además:

- leer handoff/checklist/documento relevante;
- identificar tests afectados;
- ubicar vistas, scripts, servicios, controllers y DTOs relacionados;
- verificar si hay deuda o ramas peligrosas documentadas;
- revisar si hay procesos activos que puedan bloquear build o archivos.

Clasificar componentes tocados como:

- canónico;
- legacy;
- duplicado/paralelo;
- incierto.

Luego:

1. elegir la intervención de menor riesgo que fortalezca el camino canónico;
2. implementar solo el micro-lote acordado o inferido por el pedido;
3. validar con evidencia;
4. cerrar con informe.

---

## 6. Reglas críticas

No hacer:

- refactors masivos sin justificación clara;
- cambios funcionales sin tests o validación explícita;
- mezcla de UX, backend, refactor, tests y migraciones en el mismo lote salvo alcance explícito;
- cambios simultáneos en migraciones, entidades, controllers, services, vistas y tests productivos salvo que el alcance lo requiera;
- asumir que un test fallando está mal;
- borrar componentes legacy o inciertos sin verificar referencias, DI, rutas, vistas, scripts y tests;
- commitear sin revisar `git status --short`, `git diff --stat` y `git diff --check`;
- usar `git add -A`;
- commitear archivos locales, temporales, reportes generados o secretos;
- dejar procesos iniciados por la tarea consumiendo memoria o bloqueando archivos.

Regla de agregado a Git:

```powershell
git add ruta/exacta/del/archivo
```

No usar:

```powershell
git add -A
```

---

## 7. Archivos locales que no se deben commitear

No commitear:

- `.claude/settings.local.json`
- `skills-lock.json` si aparece eliminado o modificado localmente
- `tmpbuild*`
- `tmptest*`
- `test-results`
- `playwright-report`
- `graphify-out`
- logs temporales
- screenshots temporales
- secrets
- API keys
- archivos generados por pruebas locales

Antes de cerrar una tarea:

```powershell
git status --short
git diff --stat
git diff --check
```

---

## 8. Backend y arquitectura

Respetar la arquitectura existente salvo mejora clara y segura.

Prioridades:

- reutilizar servicios, fachadas, helpers y mapeos existentes;
- evitar lógica de negocio en controllers;
- evitar lógica de negocio en Razor;
- evitar lógica de negocio final en JavaScript;
- mantener cálculos sensibles en backend;
- justificar cualquier service o interfaz nueva;
- no cambiar endpoints ni payloads en fases UX salvo instrucción explícita;
- no cambiar entidades ni migraciones en fases visuales;
- no tocar stock, caja, crédito ni confirmación de venta salvo fase funcional específica.

Regla de autoridad:

El backend decide.  
El frontend puede previsualizar o anticipar errores, pero no definir la verdad de negocio.

---

## 9. Componentes canónicos importantes

Tratar como canónicos salvo evidencia contraria:

- `VentaService`
- `CreditoService`
- `CajaService`
- `MovimientoStockService`
- `CotizacionService`
- `CotizacionConversionService`
- `EvaluacionCreditoService`
- `ClienteAptitudService`
- `ProductoCondicionPagoService`
- `CondicionesPagoCarritoResolver`
- `ProductoCondicionPagoRules`
- `OrdenCompraService`
- `ProveedorService`
- `ReporteService`
- `DashboardService`
- `SeguridadController`
- `CambiosPreciosController`
- `PriceChangeBatch`
- `PriceChangeItem`
- `ProductoPrecioLista`

Regla específica:

`ProductoPrecioLista` es la fuente de verdad para precios vigentes.

---

## 10. Componentes legacy o no reintroducir sin verificar

No reintroducir ni referenciar como canónicos sin verificación:

- `MoraAlertasService`
- `CalculoMoraService`
- `CobranzaAutomatizacionService`
- `PromesaPagoService`

Si aparece uno de estos nombres:

1. verificar si existe realmente;
2. verificar referencias;
3. verificar DI;
4. verificar rutas;
5. verificar vistas;
6. verificar scripts;
7. verificar tests;
8. documentar antes de tocar.

No crear deuda nueva alrededor de estos componentes.

---

## 11. Frontend, UI y accesibilidad

Priorizar:

- claridad operativa;
- contraste alto;
- legibilidad;
- soporte para baja visión;
- mobile-first;
- accesibilidad;
- consistencia visual;
- reversibilidad del cambio.

En tareas visuales:

- no tocar controllers;
- no tocar services;
- no tocar entidades;
- no tocar migraciones;
- no cambiar endpoints;
- no cambiar payloads;
- no cambiar reglas de negocio.

Evitar:

- glassmorphism excesivo;
- neón;
- gradientes genéricos;
- texto gris de bajo contraste;
- animaciones innecesarias;
- estética experimental que reduzca claridad;
- cambios visuales que oculten información operativa.

Preservar siempre:

- `id`;
- `name`;
- `data-*`;
- `asp-*`;
- antiforgery;
- contratos JS;
- hooks usados por JavaScript;
- selectores usados por tests;
- mensajes funcionales existentes;
- estructura esperada por Playwright.

Reglas específicas:

- No eliminar hooks usados por JS.
- No eliminar `.toast-msg` si se usa como hook de auto-dismiss.
- No reemplazar `.alert-erp` por Tailwind inline si ya existe patrón canónico.
- Preferir mejoras pequeñas, verificables y reversibles.

---

## 12. Seguridad frontend

Al tocar JavaScript o Razor dinámico:

- revisar uso de `innerHTML`;
- preferir `textContent`;
- preferir `createElement`;
- preferir `replaceChildren`;
- escapar valores provenientes del servidor o usuario;
- no interpolar datos externos en HTML sin escape;
- mantener payloads existentes;
- no cambiar endpoints por una mejora visual.

Si se toca una zona con deuda XSS conocida, documentar si queda resuelta o pendiente.

---

## 13. PowerShell y Playwright

En PowerShell usar siempre:

```powershell
npx.cmd
```

No usar:

```powershell
npx
```

Motivo:

PowerShell puede bloquear `npx.ps1` por `ExecutionPolicy`.

Variables estándar para E2E:

```powershell
$env:E2E_USER="Admin"
$env:E2E_PASS="Admin123!"
$env:ASPNETCORE_ENVIRONMENT="Development"
```

Specs frecuentes:

```powershell
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js
npx.cmd playwright test e2e/cotizacion-simulador.spec.js
npx.cmd playwright test e2e/cotizacion-conversion.spec.js
npx.cmd playwright test e2e/venta-pago-por-item.spec.js
```

Reglas:

- Si la fase es doc-only y el diff confirma que solo cambió documentación, no es obligatorio ejecutar build/tests/Playwright.
- Si se toca Razor, JS, CSS o tests, ejecutar validaciones correspondientes.
- Si se toca Venta, correr tests de Venta y Playwright relevante.
- Si se toca Cotización o conversión, correr specs de Cotización.
- Documentar resultados exactos.

---

## 14. Tests habituales

Comandos frecuentes:

```powershell
dotnet build --configuration Release

dotnet test --configuration Release --filter "VentaCreate"

dotnet test --configuration Release --filter "LayoutUiContractTests"

dotnet test --configuration Release --filter "Cotizacion"

dotnet test --configuration Release --filter "Layout|Shared|Navigation|Sidebar|Header|UiContract|Seguridad|Auth|Dashboard"
```

Resultados de referencia recientes:

- `VentaCreate`: 60/60 OK
- `LayoutUiContractTests`: 57/57 OK
- `Cotizacion`: 170/170 OK
- suite general reciente: 235/235 OK
- Playwright visual: 169/169 OK
- Cotización simulador: 57/57 OK
- Cotización conversión: 29/29 OK
- `venta-pago-por-item`: 1 passed + 42 skipped esperados por falta de datos/seed

Importante:

Los resultados de referencia son orientación, no verdad permanente.  
Si cambian, documentar el resultado real y la causa probable.

---

## 15. Cierre de procesos

Al finalizar una tarea, revisar procesos relacionados con el repo:

```powershell
Get-CimInstance Win32_Process |
Where-Object { $_.CommandLine -match "theburyproject1|TheBuryProyect|dotnet build|dotnet test|dotnet restore|vstest|testhost|playwright|node" } |
Select-Object ProcessId, ParentProcessId, CommandLine |
Format-List
```

Cerrar solo procesos iniciados por la tarea.

Cerrar si fueron iniciados por la tarea:

- `TheBuryProyect.exe`
- `TheBuryProyect.dll`
- `dotnet run`
- `dotnet build`
- `dotnet test`
- `testhost`
- `vstest`
- `playwright`
- `node` de test-server si fue iniciado por la tarea

Documentar pero no cerrar procesos externos preexistentes:

- VS Code
- C# DevKit
- MSBuild language server
- Playwright MCP
- Context7 MCP
- Porofessor
- `Code.exe`
- procesos Node del IDE

Si queda un proceso activo, indicar:

- PID;
- command line;
- si fue iniciado por la tarea;
- si se cerró o por qué se dejó vivo.

---

## 16. Skills y herramientas

Regla general:

Usar skills según la tarea.  
No usar todas al mismo tiempo.

Elegir:

- 1 skill principal;
- máximo 1 o 2 skills de apoyo.

No convertir las skills en una excusa para saltarse lectura de código real.

---

## 17. Skills principales para este ERP

### 17.1. `ui-ux-pro-max`

Usar para:

- auditoría UX/UI;
- Razor;
- design system dark accesible;
- contraste;
- jerarquía;
- mobile;
- accesibilidad;
- componentes operativos;
- fases UI/UX como `VENTAS-UX`, `COTIZ-UX` y rework visual.

Reglas:

- usarla como criterio auxiliar;
- validar siempre contra código real;
- no tocar reglas de negocio por recomendaciones visuales;
- priorizar dark theme sólido, alto contraste y claridad operativa.

### 17.2. `normalize-razor-structure`

Usar cuando la tarea toque:

- vistas Razor grandes;
- formularios;
- modales;
- labels;
- estructura HTML;
- duplicación entre `Create_tw.cshtml` y partials;
- accesibilidad en Razor.

No usar para cambiar lógica funcional.

### 17.3. `redesign-existing-projects`

Usar para rediseñar pantallas existentes sin romper funcionalidad.

Debe preservar:

- `id`;
- `name`;
- `data-*`;
- `asp-*`;
- endpoints;
- antiforgery;
- contratos JS;
- tests existentes.

### 17.4. `design-taste-frontend`

Usar para:

- jerarquía visual;
- espaciado;
- composición;
- densidad;
- legibilidad;
- claridad operativa.

### 17.5. `minimalist-ui`

Usar para mantener una interfaz:

- sobria;
- empresarial;
- clara;
- sin ruido visual;
- sin decoración innecesaria.

### 17.6. `emil-design-eng`

Usar para:

- pulido fino;
- hover;
- focus;
- active;
- microinteracciones;
- feedback visual;
- estados;
- detalles de accesibilidad visual.

### 17.7. `full-output-enforcement`

Usar al cierre de fases para asegurar:

- informe completo;
- checklist;
- validaciones;
- riesgos;
- procesos;
- deuda remanente;
- próximo paso.

No usar como skill principal de implementación salvo que el usuario pida máxima exhaustividad.

---

## 18. Skills condicionales

### 18.1. `image-to-code`

Usar solo cuando haya:

- imagen;
- captura;
- mockup;
- referencia visual;
- UI a replicar.

### 18.2. `imagegen-frontend-web` / `imagegen-frontend-mobile`

Usar solo para:

- prototipos visuales;
- exploración de interfaz;
- ideas visuales.

No aplicar directamente al ERP sin validar contra código real.

### 18.3. `high-end-visual-design`

Usar con moderación para pulido visual.

No introducir estética decorativa que afecte claridad.

### 18.4. `gpt-taste` / `stitch-design-taste`

Usar solo como apoyo de criterio visual.

No reemplazan análisis de código.

### 18.5. `industrial-brutalist-ui`

No usar por defecto en este ERP.

Solo usar si el usuario pide una estética experimental.

Motivo:

El ERP requiere claridad empresarial, accesibilidad, legibilidad y bajo ruido visual.

---

## 19. Modos metodológicos

Si estos nombres no existen como skills instaladas, tratarlos como modos de trabajo.

### 19.1. `diagnose`

Usar para:

- bugs;
- tests fallando;
- comportamiento inesperado;
- flakiness;
- file-locks;
- problemas de memoria;
- conflictos de ramas;
- errores de entorno.

### 19.2. `tdd`

Usar para:

- reglas de negocio sensibles;
- cálculos;
- regresiones;
- contratos HTML/JS;
- validaciones backend;
- casos de conversión;
- persistencia;
- seguridad.

### 19.3. `handoff`

Usar para:

- cierre de fase;
- transferencia de contexto;
- informe para nuevo chat;
- integración a main.

---

## 20. Regla `.agents` vs `.claude`

Si una skill aparece en `.claude/skills` pero no en `.agents/skills`, puede ser Claude-only.

Si debe estar disponible para todos los agentes:

- copiarla también a `.agents/skills`; o
- documentar explícitamente que es Claude-only.

Skills observadas solo en `.claude/skills` según el entorno actual:

- `normalize-razor-structure`
- `ui-ux-pro-max`

Si se quieren usar desde otros agentes, agregarlas también a `.agents/skills`.

---

## 21. Graphify

Usar Graphify cuando el cambio involucre:

- varios archivos;
- dependencias cruzadas;
- controllers pesados;
- servicios de dominio;
- legacy;
- flujo frontend/backend/tests;
- análisis de arquitectura.

Si `graphify-out/graph.json` existe, puede orientar consultas.

Si Graphify falla o está desactualizado:

- documentar la limitación;
- continuar con lectura directa;
- usar `Select-String`;
- revisar DI;
- revisar rutas;
- revisar vistas;
- revisar scripts;
- revisar tests.

No modificar código solo porque el grafo sugiera cercanía.

Graphify puede requerir API key o backend configurado.

Si falla por falta de key, revisar variables:

- `GEMINI_API_KEY`
- `GOOGLE_API_KEY`
- `MOONSHOT_API_KEY`
- `ANTHROPIC_API_KEY`
- `OPENAI_API_KEY`

También puede requerir `--backend`.

No bloquear la tarea por Graphify si el análisis puede hacerse con lectura directa.

---

## 22. MCP

Usar MCP solo cuando aporte valor real.

Casos útiles:

- inspección de documentación viva;
- herramientas externas;
- búsqueda estructurada;
- Playwright MCP;
- Context7 MCP;
- integraciones controladas.

Reglas:

- no depender de MCP para sustituir lectura del repo;
- no asumir que MCP está actualizado;
- documentar si una herramienta MCP condicionó el resultado;
- no dejar procesos MCP iniciados por la tarea sin documentar.

---

## 23. Modo autónomo

Si el usuario dice:

- "seguí";
- "continua";
- "avanza";
- "dame el siguiente frente";
- "seguimos";
- o similar;

entonces:

1. analizar estado actual;
2. elegir el siguiente micro-lote rentable;
3. justificar brevemente;
4. implementar si es seguro y mecánico;
5. pedir confirmación solo si hay riesgo alto, decisión de negocio o impacto multidominio.

Si hay duda entre dos caminos:

- priorizar cerrar integraciones pendientes antes de abrir fases nuevas;
- priorizar QA antes de rediseños grandes;
- priorizar micro-lotes de bajo riesgo;
- no mezclar deuda funcional con UX;
- no tocar stock/caja/crédito/conversión salvo fase funcional explícita.

---

## 24. Tipos de fase

### 24.1. Audit-only

Solo documentación.

Ejemplos:

- auditoría UX;
- auditoría JS;
- diagnóstico de deuda;
- mapa de flujo.

Validación mínima:

```powershell
git diff --check
git status --short
```

Además:

- verificar temporales;
- documentar que no corresponde build/tests si solo cambió documentación.

### 24.2. CSS-only

Solo CSS o tokens visuales.

Validar:

- build si aplica;
- tests de layout si existen;
- Playwright visual.

### 24.3. Razor / HTML

Toca vistas, partials, labels, modales o accesibilidad.

Validar:

- build;
- tests de contrato UI;
- Playwright visual si afecta pantalla visible.

### 24.4. JavaScript

Toca JS productivo.

Validar:

- build si aplica;
- tests afectados;
- Playwright de flujo;
- revisión XSS / `innerHTML`;
- preservación de IDs, eventos y payloads.

### 24.5. Backend

Toca services, controllers, entidades, migraciones o DTOs.

Validar:

- build;
- tests unitarios;
- tests de integración;
- migraciones si aplica;
- reglas de negocio;
- compatibilidad con flujos existentes.

No mezclar con rework visual salvo instrucción explícita.

### 24.6. Integración a main

No agregar cambios nuevos.

Solo:

1. revisar diff;
2. confirmar archivos esperados;
3. merge;
4. validar;
5. push;
6. informar.

---

## 25. Estado activo y handoff

Este archivo no debe contener todo el historial de fases.

El estado actual debe vivir en un documento separado, por ejemplo:

- `docs/handoff-actual.md`
- `docs/estado-actual-agentes.md`
- documentos de cierre de cada fase

Ese documento debe contener:

- main actual;
- fase activa;
- última fase integrada;
- próximos pasos;
- ramas descartadas;
- ramas pendientes;
- tests esperados;
- deudas abiertas;
- reglas específicas del momento.

---

## 26. Ramas descartadas o peligrosas

No mergear ramas viejas sin auditoría.

Caso conocido:

```text
origin/kira/ventas-create-frontend-tipo-pago-ux
```

Estado:

```text
SALIDA C — descartar / rehacer desde main.
```

Reglas:

- no mergear;
- no hacer cherry-pick completo;
- usar solo como referencia histórica si hace falta;
- implementar cambios útiles desde main actual.

Motivo:

La rama estaba basada en una base vieja y podía destruir trabajo reciente de UI, tests, Playwright, CSS y docs.

---

## 27. Definición de done

Antes de cerrar una tarea debe quedar claro:

- el camino canónico quedó más claro o protegido;
- la deuda legacy no fue ampliada, o quedó explicitada;
- se ejecutaron pruebas/validaciones o se explicó por qué no aplican;
- riesgos y deuda remanente fueron informados;
- procesos iniciados por la tarea fueron cerrados o documentados;
- archivos locales no commiteados fueron verificados;
- temporales no fueron commiteados;
- checklist mínimo fue actualizado cuando corresponde;
- siguiente micro-lote recomendado quedó claro;
- working tree final quedó claro.

---

## 28. Formato de informe final

El informe final debe incluir, según aplique:

```text
A. Estado inicial.
B. Rama creada o integrada.
C. Archivos auditados.
D. Archivos modificados.
E. Cambios aplicados.
F. Cambios descartados.
G. Contratos preservados.
H. Qué no se tocó.
I. Cambios que debería notar el usuario.
J. Validaciones ejecutadas.
K. Tests ejecutados.
L. Playwright ejecutado.
M. Resultados exactos.
N. Procesos cerrados.
O. Procesos que quedaron corriendo y motivo.
P. Estado de .claude/settings.local.json y skills-lock.json.
Q. Verificación de temporales.
R. Working tree final.
S. Riesgos/deudas.
T. Commit.
U. Push.
V. Próximo prompt recomendado.
```

No inventar resultados.

Si algo no se ejecutó, decirlo explícitamente.

Ejemplo:

```text
No se ejecutó Playwright porque la fase fue doc-only y el diff confirma que solo cambió documentación.
```

---

## 29. Documentación relacionada

Documentos relevantes:

- `docs/metodologia-agentes.md`
- `docs/ui-rework-guia-operativa.md`
- `docs/ui-0d-cierre-diagnostico-rework-visual.md`
- `docs/ventas-ux-1b-auditoria-flujo-venta-create.md`
- `docs/ventas-ux-1a-tipo-pago-principal-visible.md`
- `docs/cotiz-qa-3-conversion-e2e-cotizacion-venta.md`
- `docs/cotiz-qa-2-guardar-conversion-descuentos.md`
- `docs/cotiz-1b-descuento-por-producto.md`
- `docs/cotiz-1a-formulario-cotizacion-campos-comerciales.md`

Regla:

Si un documento relacionado contradice el código real, manda el código real.  
Documentar la discrepancia.

---

## 30. Checklist mínimo por tarea

Antes de implementar:

- [ ] Revisé `git status --short`.
- [ ] Revisé `git diff --stat`.
- [ ] Revisé `git log --oneline -10`.
- [ ] Leí el handoff o documento de fase relevante.
- [ ] Identifiqué componentes canónicos/legacy/inciertos.
- [ ] Definí micro-lote de bajo riesgo.
- [ ] Confirmé qué no debo tocar.

Durante la implementación:

- [ ] Preservé contratos existentes.
- [ ] No cambié reglas de negocio fuera de alcance.
- [ ] No mezclé UX con backend salvo alcance explícito.
- [ ] Evité deuda nueva.
- [ ] Revisé seguridad frontend si toqué JS/Razor dinámico.

Antes de commitear:

- [ ] Ejecuté validaciones aplicables.
- [ ] Revisé `git diff --check`.
- [ ] Revisé `git status --short`.
- [ ] Agregué solo archivos explícitos con `git add ruta/exacta`.
- [ ] Verifiqué que no haya temporales ni secretos.

Antes de cerrar:

- [ ] Revisé procesos.
- [ ] Cerré procesos iniciados por la tarea.
- [ ] Documenté procesos preexistentes si quedaron vivos.
- [ ] Informé riesgos/deuda.
- [ ] Informé próximo micro-lote recomendado.
