Módulo UI Refactor

Objetivo

Analizar o mejorar un módulo concreto sin romper su comportamiento, sus contratos con backend ni la coherencia visual del ERP.

Cuando exista un objetivo visual explícito —por ejemplo una captura, mockup, referencia o pedido concreto de rediseño— el resultado debe alcanzar ese objetivo dentro del alcance autorizado, sin usar la preservación técnica como excusa para hacer cambios visuales insuficientes.

Trabajar sobre:

vistas y parciales Razor;

CSS realmente cargado por el módulo;

JavaScript realmente cargado por el módulo;

layout, bundles o imports relacionados;

ViewModels, controllers y tests únicamente para entender contratos e impacto.

Determinar el modo

Inferir el módulo y el modo a partir del pedido, rutas o archivos mencionados.

analizar, revisar, diagnosticar, opinar: solo diagnóstico.

corregir, modificar, aplicar, refactorizar, implementar: se permiten cambios.

Si el módulo no puede inferirse con evidencia, pedirlo antes de editar.

No volver a pedir datos que ya estén en la conversación o en el repositorio.

Inicio obligatorio

Antes de auditar una pantalla:

leer docs/ui/ERP-UI-STANDARD.md;

leer docs/ui/UI-REFACTOR-STATUS.md.

Leer AGENTS.md y CLAUDE.md.

Ejecutar:

git status --short
git diff --stat

Identificar cambios previos del usuario y no pisarlos.

Delimitar el alcance solicitado por el usuario.

Usar micro-lotes solo cuando exista riesgo funcional, técnico o de regresión que justifique separar la implementación.

No fragmentar artificialmente un rediseño visual coherente en múltiples micro-lotes si puede implementarse y validarse de forma segura en una sola iteración.

Consultar codebase-memory-mcp si hay dependencias cruzadas o camino canónico incierto.

Leer directamente los archivos afectados.

Mapeo del módulo

Localizar con evidencia:

vistas principales y parciales;

CSS y JavaScript cargados;

ids, nombres y data-* usados por scripts;

endpoints y ViewModels relevantes;

componentes compartidos;

tests existentes;

variantes legacy o duplicadas.

No asumir que un archivo con nombre parecido gobierna la pantalla real.

Auditoría (4 capas)

Ejecutar siempre las 4 capas de docs/ui/ERP-UI-STANDARD.md §14 — pasar la primera no exime de las otras tres. Una pantalla puede estar técnicamente normalizada y aun así requerir mejoras de UX o visuales.

Técnica — clasificar cada hallazgo: funcional, contrato Razor/backend, estructura HTML, CSS, JavaScript, responsive técnico, accesibilidad técnica, contenido redundante u obsoleto, duplicación, código posiblemente muerto, deuda fuera de scope. Evidencia, impacto y corrección mínima por hallazgo. No declarar código muerto solo por falta de referencias textuales; verificar carga dinámica, layout, bundling y uso desde JavaScript.

Visual — jerarquía, composición, proporciones, densidad, spacing, color, contraste semántico, prioridad visual, scanability, aprovechamiento del viewport, agrupación de información, peso visual y redundancia visual (clasificar como útil, accidental o cognitiva).

Flujo UX — la pantalla como tarea real: qué intenta hacer el usuario, qué ve primero, qué lo bloquea, acciones compitiendo, pasos o información innecesarios, ambigüedad entre guardar/confirmar/aplicar/continuar, dead ends, paridad de prioridad funcional en mobile. Sin cambiar reglas de negocio.

Estados reales — no validar solo happy path: vacío, con datos, completado, error, bloqueado, no viable, estado terminal, mobile.

Cierre distingue Técnicamente: ✅/🟡, Visualmente: ✅/🟡, Flujo UX: ✅/🟡.

Buena paridad, responsive y tests no habilitan por sí solos "no hace falta tocarla".

Toda propuesta debe responder al menos una pregunta de valor real de §14 del estándar:

reduce carga cognitiva;

aclara prioridad;

elimina redundancia o ambigüedad;

reduce pasos;

evita errores;

mejora lectura;

mejora responsive;

mejora accesibilidad;

acerca de forma demostrable el resultado a una referencia visual explícitamente solicitada.

Si ninguna aplica, no cambiar.

Referencia visual proporcionada por el usuario

Cuando el usuario adjunta una captura, mockup o imagen y pide que una pantalla se vea como esa referencia:

la referencia se considera una fuente visual autorizada para ese alcance;

tiene prioridad sobre la apariencia legacy actual de esa pantalla;

puede justificar cambios de layout, grid, spacing, jerarquía, densidad, cards, tabs, orden visual, distribución, composición y responsive;

puede justificar reemplazar una estructura visual existente si esa estructura impide alcanzar el objetivo pedido;

no autoriza por sí sola cambios de reglas de negocio, permisos, contratos backend, estados inexistentes, datos inventados, rutas o comportamiento funcional no solicitado.

La regla "no aplicar una estética externa al ERP" no debe usarse para rechazar una referencia visual explícitamente proporcionada por el usuario.

Cuando la referencia visual contradiga componentes ya existentes del ERP:

preservar identidad global, tipografía, iconografía y tokens salvo pedido explícito;

adaptar la composición de la referencia al design system real;

no rebajar el objetivo visual a "lo que ya existe" si el usuario pidió explícitamente una mejora.

Hallazgos priorizados

Priorizar los hallazgos de las 4 capas antes de implementar, no solo listarlos:

bloqueante — impide completar la tarea o es un error funcional/backend;

alto — fricción real, redundancia cognitiva o contradicción de acciones (visual vs. funcional; no afirmar bug funcional sin verificar la lógica real);

medio — mejora visual, de flujo o de microcopy con valor real (§14 del estándar) pero no bloquea;

bajo / fuera de scope — deuda documentada, no se implementa si no afecta el objetivo solicitado; alimenta el roadmap mínimo del entregable.

En mantenimiento correctivo normal, priorizar bloqueante + alto.

Cuando el usuario pida explícitamente un rediseño, convergencia visual, réplica de una referencia, "que se vea así", "que quede terminado", "10/10" o cierre visual completo:

implementar también los hallazgos visuales medios necesarios para alcanzar ese objetivo;

no dejar diferencias visuales relevantes para un "siguiente micro-lote" únicamente por clasificación de prioridad;

no interpretar bajo riesgo como cambiar lo mínimo posible cuando eso impida alcanzar el resultado solicitado;

usar micro-lotes solo si existe una razón real de riesgo o dependencia, no como regla automática.

Apoyo UX/UI especializado

modulo-ui-refactor conserva siempre la responsabilidad sobre:

contratos Razor/backend;

preservación de comportamiento;

protección del working tree;

archivos permitidos;

implementación;

validación técnica;

validación con Playwright;

coherencia con la arquitectura real del ERP.

No tiene autoridad para rebajar, reinterpretar o cerrar antes de tiempo un objetivo visual explícito del usuario.

Las skills visuales especializadas son consultivas.

UI/UX Pro Max

Usar únicamente cuando el usuario la invoque explícitamente o cuando el pedido indique expresamente que debe participar.

Priorizar consultas relacionadas con:

accesibilidad;

interacción;

responsive;

formularios;

tablas y visualización de datos;

jerarquía;

estados de interfaz.

Para TheBuryProject:

usar html-tailwind como referencia técnica cuando corresponda;

priorizar dominios ux, web y chart;

no usar --persist;

no crear design-system/MASTER.md;

no crear overrides de páginas;

no considerar sus recomendaciones una fuente de verdad;

contrastar siempre sus recomendaciones con la interfaz real y los componentes existentes.

Impeccable

Los hallazgos de Impeccable son candidatos de diagnóstico y priorización.

Usar únicamente mediante invocación explícita y con un comando concreto.

Para módulos existentes priorizar:

critique: análisis UX y heurístico;

audit: accesibilidad, responsive, performance e integridad técnica;

harden: estados límite, errores e internacionalización;

adapt: responsive;

clarify: textos, labels y mensajes;

optimize: performance de UI;

polish: cierre visual.

No ejecutar automáticamente:

init;

document;

extract;

hooks;

doctor;

bolder;

delight;

overdrive.

No crear PRODUCT.md, DESIGN.md, design systems ni nueva autoridad visual salvo pedido explícito.

En TheBuryProject usar el modo conceptual Operate: priorizar scanability, consistencia, expectativas de aplicaciones administrativas, densidad útil y eficiencia de tarea sobre expresividad visual.

Cuando el usuario pida explícitamente critique, polish, audit o una calidad visual alta, sus hallazgos pueden ampliar el alcance visual dentro de la misma pantalla siempre que:

sigan dentro del objetivo solicitado;

no cambien reglas de negocio;

no introduzcan nuevas dependencias innecesarias;

no abran frentes ajenos al módulo.

Cuando participan ambas

Este flujo aplica únicamente cuando el usuario haya pedido explícitamente usar ambas skills.

No mezclar sus instrucciones directamente.

Orden:

mapear primero la pantalla y sus contratos con modulo-ui-refactor;

obtener heurísticas relevantes de ui-ux-pro-max;

usar impeccable critique o impeccable audit como segunda evaluación;

reconciliar hallazgos contra la implementación real del ERP;

clasificar y priorizar únicamente los hallazgos demostrables;

implementar el conjunto mínimo suficiente para resolver el objetivo solicitado.

"Mínimo suficiente" significa evitar cambios ajenos al objetivo, no hacer la menor cantidad posible de cambios visuales.

Ante conflicto, prevalecen:

comportamiento y reglas de negocio;

contratos existentes;

instrucciones de AGENTS.md y CLAUDE.md;

objetivo visual explícito autorizado por el usuario;

coherencia visual global existente del ERP;

modulo-ui-refactor;

recomendaciones de skills especializadas.

Reglas de modificación

Preservar reglas de negocio.

Preservar rutas, bindings, ids, nombres y data-* salvo que exista evidencia de que pueden cambiarse sin romper contratos y sea necesario para el objetivo solicitado.

No mover lógica crítica al frontend.

No duplicar listeners.

No crear nuevos archivos si uno existente puede asumir la responsabilidad de forma clara.

No mezclar rework visual con refactor backend salvo necesidad demostrada.

No aplicar una estética externa al ERP sin pedido explícito.

Una captura o mockup proporcionado explícitamente por el usuario se considera autorización visual dentro del alcance de esa pantalla.

Eliminar redundancias únicamente con evidencia.

Preferir una implementación visual completa y revisable antes que una sucesión artificial de micro-lotes.

Trabajar por micro-lotes solo cuando exista riesgo real que lo justifique.

Para normalización estructural pura de Razor, usar normalize-razor-structure.

Para warnings de Tailwind o editor, usar clasificacion-warnings y tailwind-protocolo.

Criterio de cierre visual

Build verde, tests verdes, consola limpia y ausencia de overflow son condiciones necesarias, pero no suficientes para declarar una tarea visual terminada.

Cuando exista una referencia visual o un objetivo visual explícito, antes de emitir Listo para commit:

abrir la pantalla real en el viewport principal;

capturar el resultado renderizado;

compararlo contra la referencia o contra el objetivo visual pedido;

enumerar las diferencias visuales estructurales todavía existentes;

corregir las diferencias relevantes que estén dentro del alcance;

repetir la captura y la comparación;

validar al menos un estado adicional relevante si la pantalla cambia sustancialmente según datos;

recién después ejecutar el cierre técnico y proponer commit.

No declarar Listo para commit mientras queden diferencias relevantes de:

composición;

proporciones;

jerarquía;

densidad;

aprovechamiento del viewport;

spacing;

alineación;

peso visual;

agrupación de información;

responsive;

legibilidad;

similitud con la referencia;

que contradigan directamente el objetivo visual del usuario.

Si el usuario expresa que el resultado sigue sin gustarle o aporta una nueva captura mostrando una diferencia concreta:

reabrir visualmente la pantalla para ese alcance;

no escudarse en que ya estaba marcada como cerrada;

no responder únicamente con build/tests;

comparar la nueva evidencia visual y corregirla.

Validación visual (Playwright real)

Cuando el cambio afecte layout, interacción, CSS, JavaScript, modal, drawer, tabla, tabs o responsive:

reproducir el estado inicial con Playwright MCP;

registrar URL y flujo;

aplicar el cambio;

repetir el flujo;

revisar consola y requests;

validar como mínimo:

1440x900;

1280x720;

768x1024;

390x844;

360x800.

Agregar 1024x720 o 900x720 cuando el problema esté cerca de un breakpoint.

Agregar 1920x1080 o el viewport de la referencia cuando el problema sea composición, aprovechamiento del ancho o similitud visual en desktop amplio.

Observar, además de responsive:

estado vacío;

con datos;

errores/bloqueos;

prioridad de acciones;

scroll;

redundancias visibles;

modales;

feedback;

mobile;

densidad;

jerarquía;

proporciones;

similitud con la referencia si existe.

No ejecutar acciones destructivas.

No afirmar que quedó responsive si no se probó en navegador.

No afirmar que quedó visualmente terminado si solo se verificó overflow, consola o breakpoints.

Validación técnica

Elegir según impacto:

dotnet build del proyecto afectado;

tests unitarios o de integración focalizados;

tests Playwright focalizados;

git diff --check;

revisión del diff final.

No ejecutar la suite completa salvo cierre de fase, pre-merge o pedido explícito.

Los tests son una red de seguridad funcional/técnica; no sustituyen la validación visual.

No crear tests triviales de CSS o markup solo para justificar un ajuste visual, salvo que protejan un contrato real o una regresión demostrada.

Iteración visual

Para rediseños o convergencia visual, usar preferentemente este flujo:

diagnóstico corto;

implementación visual completa dentro del alcance;

captura real;

comparación visual;

una ronda de corrección;

build/tests/QA;

commit si el usuario lo autoriza.

Evitar:

auditoría extensa seguida de un cambio mínimo si el objetivo ya está claro;

fragmentar el mismo rediseño en múltiples lotes sin necesidad;

cerrar una pantalla y reabrirla repetidamente por diferencias visuales previsibles;

usar el roadmap como sustituto de completar el objetivo actual.

Límites

No hacer commit ni push sin pedido explícito.

No usar git add -A.

No tocar stashes.

No limpiar cambios previos.

No abrir nuevos frentes fuera del módulo.

No ocultar errores de validación.

No repetir comandos colgados indefinidamente.

No sacrificar el objetivo visual explícito únicamente para minimizar el diff.

Entregable

Usar este orden:

resumen y veredicto;

diagnóstico por capas (técnica / visual / flujo UX / estados) con hallazgos priorizados;

comparación visual con la referencia, si existe;

archivos modificados y cambio por archivo;

build, tests y QA;

procesos iniciados o cerrados;

riesgos reales;

working tree;

comando git add exacto;

roadmap mínimo, solo si queda deuda real fuera del objetivo actual.

Veredicto:

Listo para commit — la pantalla cumple el objetivo visual solicitado y el cierre técnico;

Requiere ajuste — quedan diferencias relevantes dentro del alcance;

Bloqueado — existe una dependencia funcional/técnica que impide continuar de forma segura.

Nunca usar Listo para commit únicamente porque build/tests estén verdes.