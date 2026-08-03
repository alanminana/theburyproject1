---
name: playwright-protocolo
description: Protocolo Playwright de TheBuryProject: Playwright MCP para reproducir y validar en navegador, y Playwright CLI/E2E en Windows (npx.cmd), ejecución focalizada, escritura de tests y diagnóstico de fallos.
---

## Reglas de Playwright MCP

### Qué resuelve

Es la herramienta principal para explorar y validar el comportamiento real de la aplicación en navegador:

* navegar por la aplicación;
* reproducir errores;
* inspeccionar DOM y accesibilidad;
* interactuar con formularios;
* abrir modales y drawers;
* cambiar tabs;
* validar responsive;
* revisar consola;
* revisar requests relevantes;
* tomar capturas;
* comprobar el flujo real antes y después del cambio.

Playwright MCP no reemplaza los tests E2E versionados del repositorio.

### Usar Playwright MCP cuando

* el problema debe reproducirse en navegador;
* hay una tarea visual o responsive;
* intervienen modales, drawers, tabs, formularios o tablas;
* hay un problema de interacción;
* se necesita inspeccionar consola o requests;
* el comportamiento depende de JavaScript;
* se necesita comparar antes y después;
* se requiere evidencia visual.

### Tests Playwright del proyecto

Son la validación automatizada y repetible que debe quedar dentro del repositorio.

Usarlos para:

* prevenir regresiones;
* validar flujos críticos;
* ejecutar casos focalizados;
* documentar el comportamiento esperado;
* confirmar cambios antes de commit.

### Usar tests Playwright cuando

* el flujo debe quedar protegido contra regresiones;
* ya existe infraestructura E2E relacionada;
* el comportamiento puede automatizarse de forma estable;
* se modifica un flujo crítico;
* se corrige un bug reproducible;
* el pedido requiere validación repetible.

### Formato de evidencia Playwright

```text
Playwright:

- URL:
- usuario o rol:
- viewports:
- flujo:
- resultado:
- consola:
- requests:
- capturas:
```

Omitir los campos que no correspondan.

### Antes de modificar

Para bugs visuales o de interacción:

1. levantar o confirmar la aplicación;
2. abrir la URL real afectada;
3. reproducir el problema;
4. registrar viewport;
5. identificar el flujo exacto;
6. revisar consola si aplica;
7. revisar requests si aplica;
8. tomar evidencia suficiente;
9. recién después modificar código.

No corregir una pantalla basándose únicamente en la lectura de Razor o CSS cuando el problema puede reproducirse visualmente.

### Durante la validación

Usar preferentemente:

* selectores por rol;
* labels;
* nombres accesibles;
* texto estable;
* `data-testid` solo cuando no exista un selector semántico estable.

Evitar:

* selectores CSS frágiles;
* selectores dependientes de la posición;
* XPath innecesario;
* esperas fijas;
* interacción directa con elementos antes de que estén listos;
* asumir que un click funcionó sin verificar el resultado.

### Qué comprobar

Según el caso:

* URL final;
* título o encabezado esperado;
* visibilidad del componente;
* botones habilitados;
* navegación;
* mensajes de error;
* cambios en el DOM;
* resultado persistido;
* consola sin errores nuevos;
* request y response esperados;
* responsive;
* accesibilidad;
* foco;
* scroll;
* estados loading, empty y error.

### Evidencia mínima

```text
Playwright MCP: OK
URL: http://localhost:5187/Ventas/Create
Viewport: 1280x720
Flujo: abrir modal, completar producto, cambiar medio de pago y guardar
Resultado: modal usable, acciones alcanzables y sin overflow horizontal
Consola: sin errores nuevos
```

### Sesión de navegador

* No reutilizar ciegamente una sesión con estado desconocido.
* Confirmar usuario, permisos y datos de prueba.
* No modificar datos productivos.
* Usar datos de QA o desarrollo.
* Cerrar sesiones o procesos iniciados por el agente cuando corresponda.
* No eliminar datos fuera del scope para restaurar el entorno.

---

---

## Playwright CLI y tests E2E

### Windows y PowerShell

Si PowerShell bloquea `npx.ps1` por la política de ejecución, usar `npx.cmd`.

Ejemplos:

```powershell
npx.cmd playwright test
npx.cmd playwright test tests/ventas-create.spec.ts
npx.cmd playwright test --grep "Venta Create"
npx.cmd playwright test --project=chromium
npx.cmd playwright test --headed
```

No modificar globalmente la política de ejecución de PowerShell solo para ejecutar Playwright si `npx.cmd` resuelve el problema.

### Ejecución focalizada

No ejecutar toda la suite E2E por defecto.

Preferir:

1. test del caso modificado;
2. archivo de tests del módulo;
3. proyecto o navegador necesario;
4. suite completa solo en pre-merge o por pedido explícito.

### Escritura de tests

* Probar comportamiento, no implementación interna.
* Usar selectores accesibles y estables.
* Evitar `waitForTimeout`.
* Evitar depender de tiempos exactos.
* Esperar condiciones observables.
* Mantener cada test independiente.
* No compartir estado mutable entre tests.
* Usar datos controlados.
* No depender del orden de ejecución.
* Reutilizar helpers existentes antes de crear nuevos.
* No ocultar fallos mediante reintentos arbitrarios.
* No actualizar snapshots sin verificar visualmente el cambio.

### Diagnóstico de tests fallando

No asumir que el test está mal.

Clasificar primero:

* regresión funcional;
* cambio intencional no reflejado;
* selector frágil;
* dato de prueba inválido;
* problema de entorno;
* problema de timing;
* dependencia externa;
* test obsoleto;
* aplicación no iniciada;
* versión incompatible.

Registrar:

```text
Test:
Resultado:
Causa identificada:
Código o test afectado:
Acción aplicada:
Validación posterior:
```

---
