# QA-PRD-P1 — Correcciones B3–B9 (entrega)

Base: `6e2a7859` (diagnóstico, 22 tests rojos a propósito) sobre `10ecd0a` (QA-PRD-P0).
Rama/worktree: `fix/qa-prd-p1-integridad` en `theburyproject1-prd-p1`. Un único commit local, sin push.
Ver el diagnóstico original por bug en `QA-PRD-P1-REPORTE.md`.

## Resultado por bug

| Bug | Estado | Qué se hizo | Pendiente de decisión |
|---|---|---|---|
| B3 códigos duplicados | **Corregido** | `Trim()` de bordes en `CreateAsync`/`UpdateAsync` y en `ExistsCodigoAsync` (compara `Codigo.Trim()` contra el código normalizado) | ¿Colapsar espacios internos / mayúsculas? (no se tocó) |
| B4 movimientos repetidos | **Corregido en código; migración sin aplicar** | `RequestId` (Guid?) en el formulario → `MovimientoStock.RequestId` + índice único filtrado; el servicio devuelve el movimiento ya creado (consulta previa + recuperación ante carrera) | Aprobar/aplicar la migración |
| B5 ajuste masivo | **Corregido** | `filtrados` sin filtro efectivo exige `ConfirmarTodoElCatalogo=true` **y** `CantidadEsperada` = conteo real; `CantidadEsperada` informada se verifica siempre | ¿Se permite el alcance total por esta vía, y con qué umbral? |
| B6 reversión | **Corregido (política conservadora)** | Si algún producto cambió de precio después del evento → se bloquea el evento completo, no se escribe nada, no se marca revertido, se informa cuáles | Bloquear todo vs. revertir parcial; "revertir igualmente" con permiso (**no implementado**) |
| B7 historial | **Corregido** | `UpdateAsync` crea un `CambioPrecioEvento` individual (+ detalle) en el mismo `SaveChanges` cuando cambia el precio de venta; `PreciosHistoricos` se sigue escribiendo | ¿Unificar a un solo historial? |
| B8 bajo costo | **Advertencia no bloqueante** | `advertencias[]` en `CreateAjax`/`EditAjax` (+ toast), `Advertencias` y mensaje en el ajuste directo | Bloqueo/confirmación/permiso: **BLOCKED**, no implementado |
| B9 UI dinámica | **Corregido** | Permisos efectivos publicados en el `<tbody>` (`data-can-*`); filas nuevas de producto y marca los respetan; "Eliminar producto", Comisión y acciones de Marca gateados en la vista | Extender a Unidades/UnidadesGlobal/Alertas (no se barrió) |

## B4 — idempotencia (diseño)
- `AjusteStockViewModel.RequestId` (`Guid?`); los 3 formularios que postean a `MovimientoStock/Create` (página, modal, ficha de inventario) renderizan un `Guid` nuevo por carga. Reenviar el mismo formulario (doble clic, refresco, reintento) repite el `RequestId`.
- `MovimientoStockService.RegistrarAjusteAsync(..., Guid? requestId = null)`:
  1. consulta previa por `RequestId` → si existe, lo devuelve (mismo producto y tipo; si pertenece a otra operación **se rechaza**, no se devuelve un dato ajeno);
  2. escribe dentro de la transacción; ante `DbUpdateException` (violación del índice único o conflicto de concurrencia) hace rollback, relee y devuelve el ganador; si no existe, era un conflicto real y se propaga como antes.
- **No** se deduplica por contenido: distinto `RequestId` con igual producto/cantidad = movimientos legítimos; sin `RequestId` (venta, OC, scripts) el comportamiento no cambia.
- `IMovimientoStockService.RegistrarAjusteAsync` ganó un parámetro opcional final → se actualizaron los 5 stubs de tests que implementan la interfaz (solo la firma).

### Migración (PENDIENTE DE APROBAR; no se aplicó a ninguna base)
Archivo: `Migrations/20261014175359_QaPrdP1MovimientoStockRequestId.cs` (+ Designer y snapshot). Contenido completo:

```csharp
Up:   AddColumn<Guid> RequestId (uniqueidentifier, NULL) en MovimientosStock
      CreateIndex IX_MovimientosStock_RequestId UNIQUE, filtro "RequestId IS NOT NULL"
Down: DropIndex + DropColumn
```
Aditiva, columna nullable (en SQL Server es solo metadato) e índice filtrado vacío al crearse: sin reescritura de tabla ni bloqueo prolongado, sin tocar datos. Revertible. Generada con `dotnet ef migrations add`; el diff del snapshot contiene únicamente esta propiedad e índice.

### Verificación de la idempotencia bajo concurrencia (condición de cierre de B4)
`QaPrdP1IdempotenciaConcurrenciaTests` usa **archivo SQLite en WAL con un DbContext y una conexión por solicitud** (no la conexión única en memoria de los tests HTTP):
- 8 solicitudes simultáneas con el mismo `RequestId` (largada común) → 1 movimiento, stock +5 una sola vez, todas devuelven el mismo movimiento.
- **Carrera determinista**: un interceptor confirma el movimiento "ganador" entre la consulta previa y la escritura del "perdedor" → ejercita la recuperación por índice único. *Prueba de mutación*: deshabilitar el `catch` de recuperación hace fallar este test (verificado y restaurado).
- 4 solicitudes simultáneas con `RequestId` distintos → no se deduplican; stock final = 10 + 5 × movimientos confirmados.
- `RequestId` reutilizado en otro producto → rechazo; sin `RequestId` → dos movimientos.
- Limitación honesta: SQLite abre transacciones de escritura con `BEGIN IMMEDIATE`, así que serializa; el comportamiento de contención con SQL Server real (bloqueos, rowversion) solo se verifica en el E2E contra `bury-qa-prd` (ver abajo).

## B5 — contrato
`filtros efectivos` = categoría > 0, marca > 0, búsqueda no vacía, stock bajo. **"Solo activos" por sí solo no acota** (equivale a casi todo el catálogo) y requiere la misma confirmación. Rechazo = `Exitoso=false`, sin tocar precios, sin evento ni historial; el mensaje y `ProductosEnAlcance` informan el conteo real. `ProductosEnAlcance` vs `ProductosActualizados` expone cantidad esperada/afectada (puede diferir si algunos ya tenían el precio resultante). La UI actual manda `seleccionados` con ids → no cambia.

## B6 — invariantes
Precondición por detalle: `Producto.PrecioVenta == detalle.PrecioNuevo`. Además `DbUpdateConcurrencyException` (RowVersion) durante la escritura → rollback con mensaje claro. El evento original queda intacto y auditable; el intento rechazado deja un `LogWarning` con usuario y productos en conflicto (no se persiste un evento por intentos fallidos). Orden correcto de reversión (primero el posterior) sigue funcionando.

## B7 — caminos que modifican precios (inventario final)
| Camino | PreciosHistoricos | CambioPrecioEvento/Detalle |
|---|---|---|
| `AplicarCambioPrecioDirectoAsync` | sí | sí |
| `RevertirCambioPrecioEventoAsync` | sí | sí (`reversion`) |
| `ProductoService.UpdateAsync` (EditAjax/Edit) | sí | **sí (nuevo)** si cambia `PrecioVenta` |
| `PrecioHistoricoService.RevertirCambioAsync` | no | no — **sin ningún llamador** (código muerto; solo interfaz) |
| Batch `AplicarCambioPrecios` | — | escribe `ProductoPrecioLista`, no `Producto.PrecioVenta` |
| `CreateAsync` | n/a (precio inicial) | n/a |
Un cambio solo de costo no genera evento (el catálogo revierte precio de venta); sí queda en `PreciosHistoricos` como antes. No se duplica el evento: el historial viejo y el nuevo coexisten igual que en el ajuste directo.

## B8 — política vigente
No existe regla que prohíba vender bajo costo; la simulación y el batch solo advierten. Se alinearon `CreateAjax`/`EditAjax` y el ajuste directo a **advertir sin bloquear**. Referencia de costo = `PrecioCompra` (la misma comparación que ya registra `ValidarProductoAsync`); si el costo "efectivo" debe incluir envío/percepciones/IVA es decisión pendiente.

## B9 — controles
Auditados: Eliminar producto (menú), Comisión, Eliminar/Editar marca, y las filas dinámicas de `producto-crear-modal.js` y `marca-crear-modal.js`. Servidor: `Marca/Delete` rechaza sin `marcas.delete` aun forzando el POST (test). Pendiente: Categorías ya estaba gateada; Unidades/UnidadesGlobal/Alertas sin barrer.

## Tests antes → después
| Suite | Antes (`6e2a7859`) | Después |
|---|---|---|
| QaPrdP1ServiceTests + QaPrdP1HttpTests (31) | 22 FAIL / 9 PASS | **30 PASS** (el test HTTP paralelo se reemplazó por los de concurrencia real) |
| QaPrdPermisosHttpTests (P0, 11) | 11 PASS | 11 PASS |
| QaPrdP1IdempotenciaConcurrenciaTests (nuevo) | — | 5 PASS (+1 mutación verificada) |
| QaPrdP1CorreccionesServiceTests (nuevo) | — | 18 PASS |
| QaPrdP1B9CorreccionesHttpTests (nuevo) | — | 4 PASS |
Suite completa (`dotnet test`, build aislado en el worktree): **5490 OK / 0 fallos / 4 omitidos preexistentes**. Una primera corrida mostró 12 fallos intermitentes de limpieza de la fábrica por el test HTTP *en paralelo* sobre una única conexión SQLite en memoria; ese test se retiró (la concurrencia real vive en `QaPrdP1IdempotenciaConcurrenciaTests`) y la suite quedó verde.

## Validaciones de stock y precios
- Stock: reintento con mismo `RequestId` → 1 movimiento / +5 una vez (secuencial, paralelo y carrera determinista); `RequestId` distinto → 2; sin `RequestId` → 2; cadena `StockAnterior→StockNuevo` consistente.
- Precios: rechazo de B5/B6 no modifica `Productos`, `CambioPrecioEventos` ni `PreciosHistoricos` (aserciones por conteo); reversión válida restaura el precio exacto.
- Bases: solo SQLite en memoria / archivo temporal borrado al final. **No se tocó la base de desarrollo ni `BuryQA_PRD`** hasta la corrida E2E.

## Datos preexistentes (B3)
No se ejecutó ninguna consulta contra bases reales. Consulta de solo lectura para que una persona detecte conflictos antes de desplegar:
```sql
SELECT LTRIM(RTRIM(Codigo)) AS CodigoNormalizado, COUNT(*) AS Cantidad, STRING_AGG(CAST(Id AS varchar(12)), ',') AS Ids
FROM Productos WHERE IsDeleted = 0
GROUP BY LTRIM(RTRIM(Codigo)) HAVING COUNT(*) > 1;
```
(`LTRIM/RTRIM` no recorta tabs; para eso `TRIM(CHAR(9)+CHAR(32) FROM Codigo)`.) Efecto de la regla sobre datos ya duplicados: no se borra ni fusiona nada; **crear** otro producto con ese código se rechaza y **editar** uno de los duplicados se rechaza con un mensaje que explica el motivo (solo hasta que la persona resuelva el grupo). Editar un producto legado con espacios de borde (sin duplicado) lo normaliza.

## Riesgos residuales
1. **B4 no se puede dar por cerrado** hasta (a) aprobar y aplicar la migración y (b) la corrida E2E contra SQL Server real (ver estado de la corrida abajo).
2. B6 bloquea el evento completo; si el negocio prefiere reversión parcial hay que decidirlo (el test fija solo el invariante "no pisar").
3. B5: "solo activos" solo exige confirmación; si la UI futura lo usara como filtro legítimo, hay que enviar `ConfirmarTodoElCatalogo`/`CantidadEsperada`.
4. B7 mantiene dos historiales; un `PreciosHistorico` y un `CambioPrecioEvento` por cambio manual.
5. Pendientes de decisión de negocio: B3 (internos/mayúsculas), B6, B8 (política y costo), B5 (alcance total), B7 (unificar).

## E2E contra `bury-qa-prd` — **BLOCKED / pendiente**
No se ejecutó: el candado de `qa-run.ps1` lo tenía otra sesión (stack `fin`), el host tenía ~2,6 GB de RAM libre y construir/levantar la imagen exige además sobrescribir `state/prd/override.env`, que es configuración compartida de otra sesión. Para correrlo: commitear (hecho), `docker build -t theburyproject/erp:qa-<sha7> --label com.theburyproject.qa.image-commit=<sha7> D:\git	heburyproject1-prd-p1`, y lanzar `run-suite.ps1` con `-Stack prd -Wt D:\git	heburyproject1-prd-p1 -Img theburyproject/erp:qa-<sha7>` bajo `qa-run.ps1 prd`. Esperado en verde: PR11b, PR15 (sus `expect.soft` ya describen el comportamiento corregido). La migración se aplica en ese stack aislado al levantar. Hasta esa corrida B4 no se considera cerrado.
