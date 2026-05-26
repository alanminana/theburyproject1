# KIRA-VENTAS-INDEX-REWORK-1A - Visual parity Centro de Ventas

## A. Objetivo

Recrear `Views/Venta/Index_tw.cshtml` desde cero como Razor real, con una estructura visual cercana al HTML objetivo descrito para el Centro de Ventas y preservando contratos funcionales del Index original.

## B. Archivo objetivo usado

No hubo archivo HTML objetivo disponible en el workspace ni pegado como bloque completo en el prompt. Se usaron como referencia visual las clases, secciones y restricciones enumeradas en el prompt: hero, KPIs, tabs, filtros, tabla desktop, cards mobile, recargo/descuento y devolución.

## C. Archivo original usado como contrato

Se usó `HEAD:Views/Venta/Index_tw.cshtml` como contrato funcional porque el archivo local estaba eliminado al inicio de la tarea.

## D. Archivos modificados

- `Views/Venta/Index_tw.cshtml`
- `Views/Venta/_VentaModuleStyles.cshtml`
- `wwwroot/css/venta-index-rework.css`
- `wwwroot/js/venta-index-rework.js`
- `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`
- `docs/kira-ventas-index-rework-1a-visual-parity.md`

## E. Qué se recreó visualmente

- Hero de Centro de Ventas con acciones principales.
- Seis KPIs derivados de ventas reales.
- Tabs: Operaciones, Pendientes, Cotizaciones y presupuestos, Devoluciones, Configuración de pagos.
- Filtros GET con layout compacto.
- Tabla desktop y cards mobile.
- Barra sticky mobile para acciones principales.

## F. Qué se adaptó a Razor

- `@model IEnumerable<TheBuryProject.ViewModels.VentaViewModel>`.
- Variables originales de permisos, caja, filtros, ventas del día y totales.
- `foreach` real sobre `ventas`.
- Estados, autorización, tipo de pago, totales y acciones desde `VentaViewModel`.
- Cotizaciones/presupuestos y devoluciones elegibles calculadas desde datos reales disponibles.

## G. Qué se descartó del HTML objetivo

- `<!DOCTYPE>`, `html`, `head`, `body`.
- CDN Tailwind.
- CSS inline.
- JS inline.
- Datos demo.
- Modal de Nueva Venta.
- `btn-abrir-modal-crear-venta`.
- `openModal('modal-nueva-venta')`.
- `href="#"` para acciones reales.

## H. Contratos preservados

- `TempData["Success"]` con `role="status"`.
- `TempData["Error"]` y `TempData["Warning"]` con `role="alert"`.
- Panel de caja cerrada con `panel-caja-cerrada`, `btn-abrir-caja`, `msg-caja-cerrada` y links a Caja.
- Link `Nueva Venta` a `asp-controller="Venta" asp-action="Create"`.
- Form `asp-action="Index" method="get" id="form-filtros"`.
- Names `Numero`, `FechaDesde`, `FechaHasta`, `Estado`, `TipoPago`, `EstadoAutorizacion`.
- Acciones `Details`, `Cancelar` y `data-open-devolucion-modal`.
- Modal real `modal-recargo` con `data-venta-modal="configuracion-pagos"`.
- Partial `_VentaDevolucionModal` condicionado a permisos/caja.

## I. Qué no se tocó

No se tocaron controllers, services, models, migrations, endpoints, payloads, stock, caja backend, crédito backend, cotización backend, reglas de negocio ni `Views/Venta/Create_tw.cshtml`.

## J. Riesgo funcional

Riesgo medio hasta validar build/browser porque el Index fue recreado completo y el árbol local tenía múltiples cambios y borrados preexistentes. El cambio evita tocar backend y mantiene rutas/ids/hooks críticos.

## K. Validaciones ejecutadas

- `node --check wwwroot/js/venta-index-rework.js` - OK.
- `node --check wwwroot/js/venta-index.js` - OK.
- `node --check wwwroot/js/venta-module.js` - OK.
- `dotnet build --configuration Release` - OK, 0 warnings, 0 errors.
- `git diff --check -- Views/Venta/Index_tw.cshtml Views/Venta/_VentaModuleStyles.cshtml wwwroot/css/venta-index-rework.css wwwroot/js/venta-index-rework.js TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs docs/kira-ventas-index-rework-1a-visual-parity.md` - OK.
- `git diff --check` global - falla por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, cambios preexistentes fuera del lote.
- `git status --short` - muestra cambios preexistentes adicionales fuera del lote.

## L. Tests ejecutados

- `dotnet test --configuration Release --filter "IndexView"` - OK, 7/7.
- `dotnet test --configuration Release --filter "VentaCreate"` - falla, 77 passed / 39 failed / 116 total. Las fallas observadas son `FileNotFoundException` por `Views/Venta/Create_tw.cshtml` y `Views/Venta/Edit_tw.cshtml`, que ya estaban eliminados al inicio y estaban fuera del alcance permitido.

## M. Playwright/manual

No ejecutado. El árbol local tiene `Views/Venta/Create_tw.cshtml` eliminado, por lo que el smoke de navegación completa a `/Venta/Create` no es confiable sin tocar un archivo prohibido por el prompt.

## N. Deudas restantes

- No se pudo contrastar contra un archivo HTML objetivo completo porque no estaba disponible localmente.
- La validación visual fina requiere Playwright/browser con datos reales.
- El archivo de tests tenía cambios preexistentes al inicio; se agregaron contratos sobre ese estado.

## O. Próximo prompt recomendado

`KIRA-VENTAS-INDEX-REWORK-1B - QA visual desktop/mobile del Centro de Ventas y ajuste fino de paridad`.
