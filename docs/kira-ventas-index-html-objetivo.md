# KIRA-VENTAS-INDEX-REWORK - HTML objetivo Centro de Ventas

Este archivo es referencia visual. No es Razor ejecutable. No copiar literalmente html/head/body/CDN/CSS inline/JS inline/datos demo/modal Nueva Venta.

## No copiar al Razor

No deben copiarse literalmente al Razor:

- `<!DOCTYPE>`
- `html/head/body`
- CDN Tailwind
- Google Fonts inline
- `style` inline
- `script` inline
- datos demo hardcodeados
- `modal-nueva-venta`
- `btn-abrir-modal-crear-venta`
- `openModal('modal-nueva-venta')`
- `CreateAjax`
- `VentaCrearModal.submit()`
- `modal-confirmar-operacion`

## HTML objetivo completo

El HTML objetivo completo fue provisto en el prompt operativo `KIRA-VENTAS-INDEX-REWORK-1B3-FINAL` como referencia visual del Centro de Ventas.

Este documento conserva la advertencia operativa y las exclusiones obligatorias para la fase `KIRA-VENTAS-INDEX-REWORK-1C`, que debe contrastar `/Venta` contra esa referencia sin copiar contenedores de documento, CDNs, estilos inline, scripts inline, datos demo ni modales legacy.

La implementación Razor debe seguir usando:

- layout compartido del ERP;
- estilos productivos en `wwwroot/css/venta-index-rework.css`;
- JavaScript productivo en `wwwroot/js/venta-index-rework.js`;
- datos reales de `VentaViewModel`;
- links y acciones reales de MVC;
- modal real de recargos/descuentos existente;
- modal real de devolución existente;
- acceso a nueva venta por `asp-controller="Venta" asp-action="Create"`;
- contratos existentes de filtros, tabs, alertas y hooks JavaScript.
