---
name: ui-responsive-protocolo
description: Protocolo obligatorio de UI responsive y validación funcional+visual conjunta de TheBuryProject: matriz de viewports, modales y drawers, y checklist de alineación entre backend y UI.
---

## UI responsive real: protocolo obligatorio

Este protocolo aplica a tareas con impacto visual real:

* UI;
* CSS;
* Razor visual;
* responsive;
* modales;
* drawers;
* tablas;
* tabs;
* formularios;
* navegación;
* componentes interactivos;
* estados visuales.

No aplica a reemplazos mecánicos sin impacto visual esperado, por ejemplo:

* `bg-gradient-to-r` a `bg-linear-to-r`;
* `flex-shrink-0` a `shrink-0`;
* `min-w-[200px]` a `min-w-50`;

salvo que el cambio altere layout, responsive, contraste, interacción o exista una regresión reportada.

### Antes del cambio

1. Levantar o confirmar la URL real.
2. Confirmar el usuario o rol necesario.
3. Confirmar los datos de prueba.
4. Reproducir visualmente el problema.
5. No modificar CSS o Razor antes de reproducirlo.
6. Usar Playwright MCP o navegador equivalente.
7. Registrar el comportamiento actual.

### Matriz mínima de viewports

Probar:

* `1440x900` — desktop;
* `1280x720` — laptop de baja altura;
* `1024x720` — desktop pequeño o tablet landscape;
* `900x720` — breakpoint intermedio;
* `768x1024` — tablet;
* `390x844` — mobile;
* `360x800` — mobile pequeño.

No todos los casos requieren documentar capturas de los siete viewports, pero todos deben validarse cuando el cambio afecta una pantalla completa, modal complejo, drawer, tabla o navegación responsive.

### Validaciones por viewport

Comprobar:

* ausencia de overflow horizontal no intencional;
* botones principales visibles y clickeables;
* contenido legible;
* contraste suficiente;
* controles alcanzables;
* tamaño táctil razonable;
* foco visible;
* orden de tabulación usable;
* tablas con scroll o layout alternativo;
* tabs operativas;
* mensajes de validación visibles;
* dropdowns dentro del viewport;
* contenido no oculto detrás de headers o footers;
* estados loading, empty y error;
* ausencia de errores nuevos en consola.

### Modales y drawers

Para modales y drawers:

1. abrir desde la UI real;
2. no abrirlos únicamente mediante JavaScript manual;
3. recorrer todas las tabs o secciones;
4. verificar Guardar y Cancelar;
5. probar `1280x720`;
6. probar al menos un viewport mobile;
7. comprobar Escape cuando corresponda;
8. comprobar foco inicial;
9. comprobar retorno de foco al cerrar;
10. verificar scroll interno.

Si el contenido supera la altura disponible:

* el scroll debe estar en el cuerpo del modal o drawer;
* las acciones deben permanecer visibles o ser alcanzables;
* la página de fondo no debe desplazarse accidentalmente.

### Después del cambio

1. repetir el flujo original;
2. repetir la matriz de viewports;
3. revisar consola;
4. revisar requests si el flujo guarda información;
5. ejecutar tests focalizados;
6. registrar evidencia;
7. comparar con el comportamiento inicial.

### Bloqueo

Si Playwright MCP o un navegador equivalente no está disponible en una tarea visual real:

* detener la validación visual;
* reportar la limitación;
* no afirmar que quedó responsive;
* no cerrar como listo para commit únicamente con build.

---

---

## Validación funcional y visual conjunta

Cuando el pedido requiera confirmar que una funcionalidad y su presentación visual están alineadas:

### Análisis funcional

Validar:

* reglas de negocio;
* campos;
* cálculos;
* persistencia;
* permisos;
* estados;
* validaciones;
* mensajes;
* flujos alternativos;
* errores;
* idempotencia si aplica;
* contratos frontend/backend.

### Análisis visual

Validar:

* correspondencia con el objetivo funcional;
* jerarquía;
* textos;
* labels;
* acciones primarias y secundarias;
* estados habilitado y deshabilitado;
* validaciones visibles;
* responsive;
* accesibilidad;
* consistencia con el resto del módulo;
* ausencia de elementos obsoletos.

### Alineación

Confirmar expresamente:

* que cada regla funcional tiene representación visual cuando corresponde;
* que la UI no ofrece acciones que el backend rechaza sin explicación;
* que el backend no requiere datos imposibles de ingresar desde la UI;
* que los textos coinciden con la terminología funcional;
* que no permanecen componentes de una versión anterior;
* que los estados guardados se reflejan correctamente al volver a abrir la pantalla;
* que mobile y desktop exponen el mismo flujo funcional.

---
