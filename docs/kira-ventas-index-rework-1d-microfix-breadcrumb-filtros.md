# KIRA-VENTAS-INDEX-REWORK-1D: Microfix breadcrumb y filtros avanzados

## Objetivo
Corregir dos bloqueos QA en la pantalla `/Venta` sin tocar backend, controllers, services, models, migrations, endpoints ni reglas de negocio.

## Qué se arregló
- Se agregó un breadcrumb visible en `Views/Venta/Index_tw.cshtml` con el enlace a `asp-controller="Venta" asp-action="Index"` y el texto actual "Centro de ventas".
- Se mantuvo el formulario original `form asp-action="Index" method="get" id="form-filtros"` y todos los nombres de campos (`Numero`, `FechaDesde`, `FechaHasta`, `Estado`, `TipoPago`, `EstadoAutorizacion`).
- Se convirtió la sección de filtros avanzados a `<details class="advanced-filters"><summary>Filtros avanzados</summary>...</details>` para cumplir con el requerimiento de QA.
- Se agregó CSS de soporte en `wwwroot/css/venta-index-rework.css` para el breadcrumb, el contenedor de filtros y el panel `<details>`.

## Archivos modificados
- `Views/Venta/Index_tw.cshtml`
- `wwwroot/css/venta-index-rework.css`
- `docs/kira-ventas-index-rework-1d-microfix-breadcrumb-filtros.md`

## Validaciones realizadas
- `node --check` sobre JS relacionados: sintaxis válida.
- `dotnet build --configuration Release`: OK.
- `dotnet test --configuration Release --filter "IndexView"`: OK.
- `dotnet test --configuration Release --filter "VentaCreate"`: OK.
- Verificación local con navegador autenticado en `http://localhost:5187/Venta`:
  - El breadcrumb está presente.
  - El panel de filtros avanzados está implementado como `<details>/<summary>`.
  - El formulario aún usa `GET` y los campos permanecen con los mismos nombres.
  - El enlace `Nueva Venta` apunta a `/Venta/Create`.

## Notas
- No se modificaron `Controllers`, `Services`, `Models`, `Migrations`, ni endpoints.
- Se preservaron los selectores y estructuras esperadas por el flujo actual.
- Queda pendiente no tocar los otros archivos modificados en el working tree que no son parte de este microfix.
