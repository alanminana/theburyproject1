# Fase 10.1 — Reporte global de unidades físicas

**Fecha:** 2026-05-15  
**Estado:** Cerrado

---

## A. Diagnóstico previo

### Qué existía

| Componente | Clasificación | Notas |
|---|---|---|
| `ProductoUnidad` | Canónico | Entidad con estado, código interno, serie, ubicación, cliente, VentaDetalle |
| `ProductoUnidadMovimiento` | Canónico | Historial de transiciones por unidad |
| `EstadoUnidad` (enum) | Canónico | 9 estados: EnStock, Reservada, Vendida, Entregada, Devuelta, EnReparacion, Faltante, Baja, Anulada |
| `IProductoUnidadService` | Canónico | Interfaz con operaciones read y write |
| `ProductoUnidadService` | Canónico | Implementación con filtros por producto, historial, transiciones |
| `ProductoController.Unidades` | Canónico | Vista por producto individual |
| `ProductoController.UnidadHistorial` | Canónico | Historial individual por unidad |
| `ProductoUnidadesViewModels` | Canónico | ViewModels para pantalla por producto |

### Qué faltaba

No existía un punto de entrada para consultar **todas** las unidades físicas del sistema sin tener que entrar producto por producto.

El filtro existente (`ObtenerPorProductoFiltradoAsync`) sólo operaba sobre un `productoId` obligatorio.

---

## B. Diseño implementado

### Ruta

```
GET /Producto/UnidadesGlobal
```

### Nombre visible

Inventario físico de unidades

### Acceso desde navegación

Botón "Inventario físico" en la barra de acciones del Catálogo (`Views/Catalogo/Index_tw.cshtml`).

---

## C. Filtros disponibles

| Parámetro | Tipo | Descripción |
|---|---|---|
| `productoId` | `int?` | Filtra por producto específico |
| `estado` | `EstadoUnidad?` | Filtra por estado exacto |
| `texto` | `string?` | Busca en código interno, serie, nombre de producto y código de producto |
| `soloDisponibles` | `bool` | Solo unidades en estado EnStock |
| `soloVendidas` | `bool` | Solo unidades en estado Vendida |
| `soloFaltantes` | `bool` | Solo unidades en estado Faltante |
| `soloBaja` | `bool` | Solo unidades en estado Baja |
| `soloDevueltas` | `bool` | Solo unidades en estado Devuelta |
| `soloSinNumeroSerie` | `bool` | Solo unidades sin número de serie |

Los filtros son GET server-side. No hay búsqueda client-side.

---

## D. Service / Query

### Método nuevo en interfaz

```csharp
Task<ProductoUnidadesGlobalResultado> BuscarUnidadesGlobalAsync(ProductoUnidadesGlobalFiltros filtros);
```

### Archivos creados

- `Services/Models/ProductoUnidadesGlobalFiltros.cs`
- `Services/Models/ProductoUnidadesGlobalResultado.cs` (incluye `ProductoUnidadGlobalItem`)

### Implementación

- Proyección EF Core con `Select` (sin cargar `Historial`)
- Incluye nombre del cliente vía concatenación SQL (`Apellido + ", " + Nombre`)
- Calcula resumen por estado en memoria sobre el resultado ya proyectado
- Ordena por `Producto.Nombre` luego por `CodigoInternoUnidad`
- Excluye `IsDeleted` en unidades y productos
- No genera N+1: una sola query a la base de datos

### Deuda documentada (V1)

- El dropdown de productos en el controller usa `IProductoService.GetAllAsync()` (carga con includes).  
  Optimización futura: query liviana con sólo `Id`, `Codigo`, `Nombre` de productos con unidades.
- El campo "Último movimiento" no se carga en V1 (requeriría subquery o join adicional con potencial costo alto).

---

## E. UI / Navegación

### Vista

`Views/Producto/UnidadesGlobal.cshtml`

- Header con breadcrumb al Catálogo
- KPIs de resumen: Total, En stock, Vendidas, Faltantes, Baja, más estados opcionales si > 0
- Formulario de filtros (GET): producto, estado, texto, checkboxes
- Tabla responsive con badge de estado por color
- Acciones por fila: Ver historial / Ver producto
- Texto aclaratorio: "Este reporte muestra unidades físicas individuales. No reemplaza el stock agregado ni el Kardex SKU."
- Estado vacío con call-to-action para limpiar filtros

### Navegación

Botón "Inventario físico" agregado en la barra de acciones del Catálogo, junto a "Movimientos" y "Ajuste Masivo".

### Links por fila

- `/Producto/UnidadHistorial/{unidadId}` — ver historial individual
- `/Producto/Unidades/{productoId}` — ver unidades del producto

---

## F. Tests

**Archivo:** `TheBuryProyect.Tests/Integration/ProductoUnidadServiceGlobalTests.cs`

| Test | Qué valida |
|---|---|
| `BuscarUnidadesGlobal_SinFiltros_RetornaTodas` | Devuelve unidades de todos los productos |
| `BuscarUnidadesGlobal_ExcluyeSoftDeleted` | Excluye unidades soft-deleted |
| `BuscarUnidadesGlobal_FiltraPorProducto` | Filtra por productoId |
| `BuscarUnidadesGlobal_FiltraPorEstado` | Filtra por estado exacto |
| `BuscarUnidadesGlobal_FiltraPorTextoEnSerie` | Busca por número de serie |
| `BuscarUnidadesGlobal_FiltraPorTextoEnCodigoInterno` | Busca por código interno |
| `BuscarUnidadesGlobal_FiltraPorTextoEnNombreProducto` | Busca por nombre de producto |
| `BuscarUnidadesGlobal_CalculaResumenCorrecto` | Resumen por estado es correcto |
| `BuscarUnidadesGlobal_SoloDisponibles_DevuelveSoloEnStock` | Filtro SoloDisponibles |
| `BuscarUnidadesGlobal_ProductoInexistente_DevuelveVacio` | ProductoId inexistente no rompe |
| `BuscarUnidadesGlobal_ProductoSinTrazabilidadRequerida_ConUnidades_Aparece` | Producto sin trazabilidad requerida con unidades aparece |

**Resultado:** 365 tests pasando (11 nuevos), 0 errores.

---

## G. Qué NO se tocó

- `VentaService` — sin cambios
- `CajaService` — sin cambios
- `MovimientoStockService` — sin cambios
- `StockActual` / Kardex SKU — sin cambios
- Reglas de transición de `ProductoUnidad` — sin cambios
- Migraciones — no se crearon
- Comprobantes — sin cambios
- Anulación de facturas — sin cambios

---

## H. Riesgos / Deuda

| Riesgo | Severidad | Estado |
|---|---|---|
| `GetAllAsync()` para dropdown de productos incluye joins pesados | Bajo | Deuda documentada. Optimizable con query liviana |
| Sin paginación en V1 — catálogos grandes pueden devolver muchos resultados | Medio | Aceptable en V1. Agregar paginación si el volumen crece |
| Último movimiento por unidad no se muestra en V1 | Bajo | Documentado. Requiere subquery extra |
| El filtro de texto busca en 4 campos — en tablas grandes puede ser lento | Bajo | EF Core genera LIKE. Indexar si es necesario |

---

## I. Checklist

### Completado

- [x] Diagnóstico previo
- [x] Service models creados
- [x] Método en interfaz agregado
- [x] Implementación en service
- [x] ViewModels creados
- [x] Acción GET en controller
- [x] Vista Razor con filtros, KPIs, tabla y acciones
- [x] Botón de navegación en Catálogo
- [x] Tests de integración (11 tests nuevos)
- [x] Stub de test actualizado para compilar
- [x] Build limpio (0 errores, 0 warnings)
- [x] 365 tests pasando
- [x] diff-check limpio
- [x] Commit y push

### Pendiente (deuda)

- [ ] Paginación del reporte global (si el volumen lo requiere)
- [ ] Query liviana para dropdown de productos (en lugar de `GetAllAsync`)
- [ ] Último movimiento por unidad en la tabla (subquery adicional)
- [ ] Exportación CSV/Excel del reporte (si se solicita)
- [ ] Tests de controller (GET UnidadesGlobal devuelve vista correcta) — requieren setup de ApplicationDbContext completo en tests de integración de controller

### Siguiente micro-lote sugerido

Fase 10.2 — Paginación del reporte global de unidades, o bien continuar con otra zona del sistema según prioridad operativa.
