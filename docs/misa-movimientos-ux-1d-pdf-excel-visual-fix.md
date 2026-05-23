# MISA-MOVIMIENTOS-UX-1D - PDF/Excel visual fix en modal Movimientos

## A. Objetivo

Resolver la expectativa falsa generada por los botones PDF y Excel visibles en el modal Movimientos de Catalogo sin implementar export real.

La decision aplicada fue deshabilitarlos visual y semanticamente, preservando el texto visible y evitando cambios en backend, JavaScript, endpoints o servicios.

## B. Base y contexto

- Fase: `MISA-MOVIMIENTOS-UX-1D`.
- Tipo: Razor-only / UX visual / bajo riesgo.
- Base indicada por prompt: `fa059bf` - `MISA-MOVIMIENTOS-UX-1C` integrada.
- Estado local observado antes de crear la rama: `main` tenia commits posteriores, con `6181316` como `HEAD` local.
- Documento base: `docs/misa-movimientos-ux-0-auditoria.md`.
- Documentos de continuidad revisados:
  - `docs/misa-movimientos-ux-1a-accesibilidad-semantica.md`
  - `docs/misa-movimientos-ux-1b-copy-jerarquia.md`
  - `docs/misa-movimientos-ux-1c-mobile-scroll.md`

## C. Deuda detectada

La auditoria `MISA-MOVIMIENTOS-UX-0` detecto que el modal Movimientos mostraba botones PDF y Excel, pero:

- no tenian handler JS;
- no tenian `id`;
- no tenian `data-*`;
- no habia endpoint de export en `MovimientoStockController`;
- no habia metodo de export en `IMovimientoStockService`;
- no existia implementacion real de export para este flujo.

Esto generaba una accion aparentemente disponible que no hacia nada.

## D. Archivo auditado

- `Views/Catalogo/Index_tw.cshtml`

Tambien se revisaron por busqueda las referencias relevantes en:

- `wwwroot/js/movimientos-inventario-modal.js`
- `Controllers/MovimientoStockController.cs`
- `Services/Interfaces/IMovimientoStockService.cs`
- `Services/MovimientoStockService.cs`

## E. Hallazgo PDF/Excel

Los botones PDF y Excel estan dentro de la Actions Bar del modal `#modal-movimientos` en `Views/Catalogo/Index_tw.cshtml`.

Antes del cambio eran botones normales `type="button"` con clases visuales activas y hover, pero sin ningun contrato funcional asociado.

La busqueda confirmo que no son hooks criticos: no tienen selectores dedicados, no son referenciados por el JavaScript del modal y no apuntan a rutas Razor/backend.

## F. Cambio aplicado

En los botones PDF y Excel del modal Movimientos se agrego:

- `disabled`
- `aria-disabled="true"`
- `title="Exportacion PDF no disponible todavia"`
- `title="Exportacion Excel no disponible todavia"`
- clases Tailwind existentes para estado deshabilitado:
  - `cursor-not-allowed`
  - `bg-slate-900/40`
  - `text-slate-500`
  - `opacity-70`

Tambien se removio el aspecto activo de `hover:bg-slate-800` para que no parezcan acciones disponibles.

Se preservo el texto visible:

- `PDF`
- `Excel`

## G. Contratos preservados

Se preservaron:

- estructura del modal;
- `id="modal-movimientos"`;
- `aria-labelledby`;
- botones y filtros existentes;
- texto visible PDF / Excel;
- tabla y columnas del modal;
- hooks JS existentes;
- paginacion client-side;
- fetch a `/MovimientoStock/ListJson`;
- rutas, payloads y permisos existentes.

## H. Que no se toco

No se tocaron:

- backend;
- controllers;
- services;
- interfaces;
- models;
- viewmodels;
- migrations;
- endpoints;
- payloads;
- permisos;
- JavaScript;
- CSS;
- `MovimientoStock/Index`;
- `MovimientoStock/Kardex`;
- `AlertaStock`;
- `Producto/Unidades`;
- Ventas/Kira;
- Cotizacion;
- Caja;
- Credito;
- stock funcional;
- tests;
- specs Playwright.

## I. Riesgo funcional

Riesgo bajo.

El cambio solo afecta atributos HTML y clases visuales de dos botones que no tenian implementacion funcional. Al quedar `disabled`, no disparan acciones de formulario ni clicks accionables.

No se alteran datos, endpoints, permisos ni reglas de negocio.

## J. Validaciones

Validaciones previstas para el cierre:

- `dotnet build --configuration Release`
- si hay file-lock: `dotnet build --configuration Release -o tmpbuild_misa_movimientos_ux_1d`
- `git diff --check`
- `git status --short`

## K. Tests/Playwright omitidos con motivo

No se planifican tests unitarios ni suite general porque la fase no modifica tests, logica backend, reglas de negocio, servicios, endpoints ni payloads.

No se planifica Playwright porque el cambio es Razor-only localizado sobre dos botones no funcionales, sin tocar JavaScript ni flujos.

## L. Deuda restante

Sigue pendiente la implementacion funcional real de export PDF/Excel si el producto decide incorporarla.

Para esa deuda haria falta una fase funcional separada con:

- endpoint backend;
- metodo de service;
- permisos si corresponde;
- handler JS;
- tests de backend/contrato;
- validacion E2E o funcional.

## M. Proximo paso recomendado

`MISA-MOVIMIENTOS-QA` para una revision final del frente Movimientos, o `MISA-MOVIMIENTOS-UX-1E` si se decide avanzar con filtros temporales.
