# QA-PRD-P0 — Permisos de Productos y Stock (B1, B2, B9 parcial)

Rama `fix/qa-prd-p0-permisos` (worktree `theburyproject1-prd-p0`), base `qa/productos-tx-20261013` @ 409b9d7. Evidencia original (`REPORT.md`, specs, `evidence/`) sin modificar. Sin merge ni push.

## Antes / después

| ID | Antes | Después |
|---|---|---|
| B1 | `MovimientoStock/Create` (GET y POST) exigía solo `movimientos.view` (clase). Un rol "Ver" registró una salida (stock 10→7). | GET y POST exigen además `movimientos.create`. "Ver" sigue consultando Index/Kardex/ListJson. Los dos endpoints de conciliación de `ProductoController` que también escriben movimientos (`AjustarStockAgregadoAUnidadesFisicas`, `AjustarStockAgregadoHaciaAbajo`) exigen ahora `productos.edit` **y** `movimientos.create`. |
| B2 | `Producto/EditAjax` exigía solo `productos.edit/update`: se cambiaron costo y precio (1000/1500 → 10/11) sin `precios.apply`. | Sin `precios.apply`, si el request cambia `PrecioCompra`, `PercepcionesCompra`, `OtrosCostosCompra`, `PrecioVenta` o `PorcentajeIVACompra` (tolerancia 0,01 por redondeo del formulario) → **403 + JSON `success:false`**, antes de abrir la transacción: no se escribe producto, crédito personal ni `PreciosHistoricos`. Si los importes no cambian, la edición general funciona y los importes quedan en el valor persistido. |
| B9 | Rol solo-lectura veía Nuevo producto, Ajuste Masivo, "Ajustar selección" y 110 botones Editar. | Cada control sigue el permiso que exige el servidor (tabla abajo). |

## Diferencias de permisos

| Operación | Antes | Después |
|---|---|---|
| `MovimientoStock/Create` GET/POST | movimientos.view | movimientos.view + **movimientos.create** |
| `Producto/AjustarStockAgregadoAUnidadesFisicas`, `...HaciaAbajo` | productos.edit | productos.edit + **movimientos.create** |
| `Producto/EditAjax` (costos/precio) | productos.edit | productos.edit + **precios.apply** *solo si cambia un importe* |
| Edición de datos generales (`EditAjax`) | productos.edit | sin cambios |
| `Producto/CreateAjax` | productos.create | sin cambios (ver "Decisiones") |

| Control de UI | Visible con |
|---|---|
| Nuevo producto | productos.create |
| Editar (fila del catálogo) | productos.view + productos.update |
| Ajuste Masivo / Ajustar selección | precios.simulate (aplicar sigue exigiendo precios.apply en el servidor) |
| Costo, IVA compra, percepciones, otros gastos, ganancia % y precio de venta del modal Editar | editables con precios.apply; si no, `readonly`/`disabled` con leyenda |
| Registrar ajuste / "Agregar stock" / modal de ajuste (Kardex y Ficha de inventario) | movimientos.create |

## Decisiones y supuestos (a confirmar)

- **Crear producto no exige `precios.apply`.** El precio inicial es parte de la definición del producto y los campos son obligatorios; exigirlo bloquearía a quien solo tiene `productos.create`. Queda como decisión de negocio abierta.
- **Crear producto con stock inicial** genera un movimiento sin pedir `movimientos.create`. Mismo criterio: no cambiado, anotado como riesgo.
- **`ActualizarComisionVendedor`** (productos.edit) cambia la comisión, un importe de negocio, pero no es costo ni precio; no se tocó.
- El botón "Editar" ya no aparece para solo-lectura; esos usuarios consultan el producto desde Inventario. Si se quiere un "Ver" de solo lectura sobre el modal, es un cambio de UX aparte.
- La fila que el JS inserta tras *crear* un producto (`producto-crear-modal.js`) sigue dibujando Editar/Eliminar sin consultar permisos hasta recargar; no se cambió (solo afecta a quien acaba de crear).

## Tests

Nuevo: `Integration/QaPrdPermisosHttpTests.cs` (11 casos, HTTP real contra `CustomWebApplicationFactory` + SQLite en memoria aislada; verifica por EF el estado de `Productos`, `MovimientosStock` y `PreciosHistoricos`):

- solo-ver: POST rechazado, 0 movimientos, stock 10; GET Create rechazado; Kardex visible sin controles de escritura.
- operador (`movimientos.create`): registra (stock 13, 1 movimiento) y ve el control.
- sin `precios.apply`: cambio de costo+precio → 403, DB intacta, 0 históricos; cambio solo de precio → 403; edición de nombre con importes iguales → OK sin histórico.
- con `precios.apply`: modifica importes, 1 histórico.
- render de `/Catalogo`: solo-lectura sin Crear/Ajuste/Editar; sin `precios.apply` importes `readonly`/`disabled`; con permiso, editables.

Verificación de que protegen: sin el cambio en `Controllers/` y `Views/` fallan 7 de 11 (los 4 que pasan son los casos positivos). Con el cambio: 11/11.

Ajustes a tests existentes (cambio de política deliberado): los tests que llaman `ProductoController.EditAjax` directamente ahora llevan el claim `precios.apply` (`ProductoControllerPrecioTests`, `ProductoCreditoPersonalControllerTests`); el contrato `ElCatalogoSoloOfreceEditarConProductosView` espera la guarda `puedeVerProducto && puedeEditarProducto`. Seeds nuevos en `CustomWebApplicationFactory`.

Suite filtrada (Producto|Catalogo|Movimiento|Permiso|Kardex|Inventario|Stock): **1212/1212** verdes. Build sin errores.

## Pendiente / no ejecutado

- **E2E contra `bury-qa-prd` no re-ejecutado**: el stack corre la imagen `qa-9504428` (sin estos cambios) y QA-ORQUESTADOR no habilitó una imagen nueva. Cuando lo haga: `& "$env:USERPROFILE\.bury-qa\qa-run.ps1" prd -- npx playwright test -c e2e-tx/productos/playwright.config.js`. Esperado: S22a, PR13c y U04 pasan a verde; P06b sigue verde (el operador edita sin tocar importes). Las "comprobaciones SQL" se hicieron por EF sobre la base de test aislada; la verificación `sqlcmd` sobre `BuryQA_PRD` queda para esa corrida.
- Suite completa de `TheBuryProyect.Tests` no corrida (solo el subconjunto relacionado).
- Verificación visual con Playwright de Catálogo/Kardex/Ficha con roles reales: pendiente de la misma infraestructura (hoy cubierto por tests de render).
