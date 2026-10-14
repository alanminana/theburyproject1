# QA-PRD-P1 — Integridad de productos, precios y movimientos (B3–B9)

Base: `10ecd0a7` (QA-PRD-P0). Rama/worktree: `fix/qa-prd-p1-integridad` en `theburyproject1-prd-p1`.
Alcance de esta entrega: **reproducción + propuesta**. No se corrigió código de producción, no se tocaron datos,
no se corrió nada contra el stack PRD ni la base compartida (todo SQLite en memoria vía `CustomWebApplicationFactory`).
Único cambio fuera de los tests: helper `SeedUserWithProductoSoloCrearAsync` en la fábrica de tests.

Tests: `TheBuryProyect.Tests/Integration/QaPrdP1ServiceTests.cs` (B3, B5, B6, B8-directo) y `QaPrdP1HttpTests.cs` (B4, B7, B8-modal, B9).
Cada test aserta el comportamiento **deseado**: el FAIL es la reproducción. Los `*_Guarda_*` fijan lo que no debe cambiar y pasan hoy.
Corrida: 31 tests nuevos → 22 FAIL (defectos), 9 PASS (guardas); los 11 de `QaPrdPermisosHttpTests` (P0) siguen en verde.

| Bug | Estado | Tests que fallan | Guardas (PASS) |
|---|---|---|---|
| B3 | FAIL (reproducido) | 7 | 1 |
| B4 | FAIL (reproducido) | 2 | 2 |
| B5 | FAIL (reproducido) | 4 | 2 |
| B6 | FAIL (reproducido) | 2 | 2 |
| B7 | FAIL (reproducido) | 1 | 1 |
| B8 | FAIL (advertencia) / BLOCKED (bloqueo) | 2 | 1 |
| B9 | FAIL (reproducido) | 4 | 0 |

(B3 cuenta 4 casos de la `Theory` + 3 tests; B5 cuenta 4 casos de la `Theory`.)

---

## B3 — Códigos duplicados que difieren por espacios
- **Causa raíz:** `ProductoService.CreateAsync`/`UpdateAsync` validan con `ExistsCodigoAsync`, que compara `p.Codigo == codigo` sin normalizar (`Services/ProductoService.cs:285, 383, 665`), y persisten `producto.Codigo` tal cual (`:418`). Ya existe índice único filtrado sobre `Codigo` (`AppDbContext.cs:378`), pero compara el valor crudo, así que no protege.
- **Matiz SQL Server (no probado aquí, solo semántica):** `=` e índices ignoran espacios **finales**; los espacios **iniciales**, tabs e internos sí distinguen. Los tests con SQLite cubren ambos bordes porque la regla deseada no debe depender del motor.
- **Riesgo de negocio:** dos productos con el mismo código lógico → búsqueda/escaneo ambiguo, stock y precios partidos entre fichas. Medio.
- **Reproducción:** `B3_Create_CodigoConEspaciosDeBordeColisionaConElExistente` (4 variantes), `B3_Create_CodigoNuevoConEspaciosSePersisteSinEllos`, `B3_ExistsCodigo_IgnoraEspaciosDeBorde`, `B3_Update_NoPermiteRenombrarAUnCodigoOcupadoAgregandoEspacios`.
- **Corrección propuesta:** un único punto de normalización (`Codigo = Codigo.Trim()`) al inicio de `CreateAsync`/`UpdateAsync` y dentro de `ExistsCodigoAsync`. Con eso el índice existente vuelve a ser efectivo; no hace falta índice nuevo.
- **Datos existentes:** **no se borra ni se fusiona nada automáticamente.** Query de solo lectura para detectar grupos: `SELECT LTRIM(RTRIM(Codigo)), COUNT(*) FROM Productos WHERE IsDeleted=0 GROUP BY LTRIM(RTRIM(Codigo)) HAVING COUNT(*)>1`. Cada grupo lo resuelve una persona (cuál conserva, qué pasa con su stock/ventas). Ojo: un `UpdateAsync` sobre uno de los productos ya duplicados ahora fallaría por colisión → informar el motivo en el mensaje.
- **Regresión:** los tests anteriores + guarda `B3_Guarda_CodigoExactoYDistintoCasoYaSeRechazaOSeAcepta_SinCambios` (el rechazo del código exacto no debe romperse).
- **Decisión de negocio:** ¿colapsar también espacios internos (`"AB  12"` → `"AB 12"`)? ¿Código se guarda en mayúsculas o se respeta el caso? Propuesta mínima: solo `Trim()`.

## B4 — Movimientos duplicados por solicitudes simultáneas idénticas
- **Causa raíz:** `MovimientoStockService.RegistrarAjusteAsync` (`:234`) valida, abre transacción y escribe, pero no hay identificador de intención: tres POST idénticos son tres movimientos válidos para el servidor. La cadena de saldos queda consistente (la concurrencia del *saldo* sí está resuelta, ver S21c).
- **Riesgo de negocio:** doble clic / reintento de red / refresco tras POST duplica entradas o salidas de stock. Medio.
- **Reproducción:** `B4_MismoRequestIdReenviado_RegistraUnSoloMovimiento` (3 POST con el mismo `RequestId` → hoy 3 movimientos) y `B4_MismoRequestIdEnParalelo_...` (hoy 2). *Nota:* el de paralelo depende de scheduling; el secuencial es el determinista.
- **Distinción reintento vs. legítimo (lo que fijan las guardas):** misma `RequestId` = reintento (se devuelve el movimiento ya creado); `RequestId` distinta con igual contenido = movimiento legítimo (`B4_Guarda_RequestIdDistintoConMismoContenido_...`); sin `RequestId` (flujos internos: venta, OC, scripts) = comportamiento actual intacto (`B4_Guarda_SinRequestIdDosEntradas...`). **No** usar ventana de tiempo por contenido.
- **Corrección propuesta:** `Guid` oculto por render del formulario/modal (`RequestId`), columna `MovimientoStock.RequestId` nullable con índice único filtrado `WHERE RequestId IS NOT NULL`; el servicio busca primero por esa clave y, ante violación del índice en carrera, relee y devuelve el existente. Complemento UX: deshabilitar el submit al enviar. **Requiere migración** (no incluida).
- **Decisión de negocio:** ninguna sobre reglas; solo confirmar que la columna nueva es aceptable. Los nombres `RequestId` son la propuesta que los tests fijan.

## B5 — Ajuste masivo con filtros vacíos modifica todo el catálogo
- **Causa raíz:** `PrecioService.AplicarCambioPrecioDirectoAsync` (`:178`) con `alcance="filtrados"` solo valida que `FiltrosJson` no sea vacío; `{}` pasa y cada filtro sin valor se omite (~`:261-285`) → la consulta queda `WHERE !IsDeleted` = todos. Verificado además que `{"busqueda":"  "}`, `{"categoriaId":0,"marcaId":0}` y `{"soloActivos":false,"stockBajo":false}` se comportan igual.
- **Alcance real del riesgo:** la UI actual (`precio-aumento-modal.js:565`) manda `alcance:"seleccionados"` con los ids de la vista previa; "Todo el catálogo" en la UI pasa por `SimularCambioPrecios` con preview (muestra la cantidad). El defecto es alcanzable **por API directa** o por cualquier cliente futuro que use `filtrados` — de ahí la severidad media.
- **Riesgo de negocio:** repricing accidental de todo el catálogo (97/97 en QA) sin confirmación. Reversible solo vía historial.
- **Reproducción:** `B5_Directo_FiltradosSinNingunFiltroEfectivo_NoModificaNada` (4 JSON); asserta `Exitoso=false`, precios intactos y 0 `CambioPrecioEvento`.
- **Corrección propuesta:** (1) calcular "filtros efectivos" tras normalizar; si ninguno aplica, rechazar con mensaje "elegí al menos un filtro o confirmá el alcance 'todo el catálogo'". (2) El alcance total solo se acepta con campo explícito `ConfirmarTodoElCatalogo=true` **y** `CantidadEsperada` igual al conteo real calculado en el servidor (la simulación ya la devuelve). Mantener `precios.apply`.
- **Regresión:** guardas `B5_Guarda_FiltradoConCategoria_...` y `B5_Guarda_SeleccionadosSigueFuncionando` (el ajuste filtrado/seleccionado legítimo no cambia).
- **Decisión de negocio:** ¿se permite el alcance "todo el catálogo" por esta vía o solo por el flujo simulación→aprobación? ¿Umbral de cantidad que exija aprobación (ya existe `RequiereAutorizacionAsync` en el batch)?

## B6 — Revertir un cambio antiguo sobrescribe un precio posterior
- **Causa raíz:** `PrecioService.RevertirCambioPrecioEventoAsync` (`:574`) asigna `producto.PrecioVenta = detalle.PrecioAnterior` sin comparar `producto.PrecioVenta` con `detalle.PrecioNuevo`; tampoco usa `RowVersion`.
- **Riesgo de negocio:** pérdida silenciosa de un precio vigente (E1 +10 %, E2 +10 %, revertir E1 → 100 en vez de 121). Alto: afecta ventas/cotizaciones al instante.
- **Reproducción:** `B6_RevertirEventoViejo_NoPisaElPrecioDeUnCambioPosterior` (hoy deja 100, esperado 121) y `B6_RevertirEventoViejo_NoMarcaElEventoComoRevertidoSiNoRevirtioNada`.
- **Control de versión propuesto:** por detalle, comparar el precio actual con `PrecioNuevo` del evento (precondición "nadie tocó el producto desde entonces"). Si difiere → ese producto se **omite** y se registra el motivo. Refuerzo contra carrera: adjuntar el `RowVersion` leído y dejar que EF falle ante cambio concurrente. El evento queda `RevertidoEn` solo si se revirtió al menos un producto; el resultado devuelve `omitidos[{productoId, precioActual, precioEsperado}]`.
- **Regresión:** guardas `B6_Guarda_RevertirElUltimoEvento...` y `B6_Guarda_NoSePuedeRevertirDosVeces...`.
- **Decisión de negocio (bloquea el diseño final):** ante conflicto en algún producto del evento, ¿**bloquear todo** el evento o **revertir parcialmente** los no conflictivos? ¿Existe un "revertir igualmente" con permiso especial? Los tests fijan solo el invariante común (el precio posterior no se pisa) y no la política parcial/total.

## B7 — Cambio de precio desde el modal Editar sin historial reversible
- **Causa raíz:** hay dos historiales. `ProductoService.UpdateAsync` (~`:392-405`) escribe `PreciosHistoricos`; el "Historial" del catálogo lee `CambioPrecioDetalles`. Un cambio hecho por `Producto/EditAjax` no crea evento/detalle → no se ve ni se revierte desde el catálogo.
- **Otros caminos de cambio de precio (inventario):**
  | Camino | PreciosHistoricos | CambioPrecioEvento/Detalle |
  |---|---|---|
  | `Catalogo/AplicarCambioPrecioDirecto` | sí (en `PrecioService`) | sí |
  | Reversión de evento | sí | sí (evento `reversion`) |
  | Batch `AplicarCambioPrecios` | a confirmar | no se verificó en esta entrega |
  | **`Producto/EditAjax` / `Producto/Edit`** | sí | **no (B7)** |
  | Creación (`CreateAsync`) | no aplica (precio inicial) | no |
  Pendiente de confirmar con lectura/ejecución: batch por lista (`ProductoPrecioLista`) y OC que actualiza costo.
- **Riesgo de negocio:** el camino más usado para tocar precios es el que no deja traza reversible; auditoría incompleta. Medio.
- **Reproducción:** `B7_EditAjax_CambioDePrecio_QuedaEnElHistorialDelCatalogoYEsReversible` (hoy: `CambioPrecioDetalles` vacío).
- **Corrección propuesta:** que `UpdateAsync`, cuando cambian importes, cree un `CambioPrecioEvento` (`Alcance="individual"`) con su detalle **en la misma transacción**, reutilizando `IPrecioService` (hoy `ProductoService` no lo recibe: cambio de constructor/DI) y conservando `PreciosHistoricos`. Alternativa: que el historial lea ambas fuentes (más barato, pero dos modelos seguirían divergiendo).
- **Regresión:** guarda `B7_Guarda_EditarSinCambiarImportesNoCreaEventosDePrecio`.
- **Decisión de negocio:** ¿unificar a un solo historial (migrar `PreciosHistoricos` al modelo de eventos) o mantener dos?

## B8 — Venta por debajo del costo sin advertencia
- **Regla documentada:** no hay una regla escrita que **prohíba** vender bajo costo. Sí existe una regla de **advertencia** en otros caminos: `PrecioHistoricoService.SimularCambioAsync` marca "margen NEGATIVO. Venderás con pérdida" y `EsRecomendable=false` (`:360`), y `PrecioService` (batch) emite `Margen negativo: x %` (`:1118`). Esos dos caminos advierten sin bloquear; `EditAjax`/`CreateAjax` y el ajuste directo no.
- **Causa raíz:** el alta/edición del producto y `AplicarCambioPrecioDirectoAsync` no evalúan margen.
- **Riesgo de negocio:** error de tipeo (900 en vez de 1900) pasa sin aviso. Bajo/medio.
- **Reproducción:** `B8_EditAjax_PrecioBajoElCosto_...` (guarda el precio y espera `advertencias[]`) y `B8_Directo_BajarPorDebajoDelCosto_...` (mensaje menciona el costo).
- **Corrección propuesta (sin cambiar política):** advertencia **no bloqueante** coherente con las otras dos: `advertencias[]` en la respuesta de `EditAjax`/`CreateAjax` y mención en el mensaje del ajuste directo; reutilizar el cálculo de margen existente en vez de duplicarlo.
- **Estado:** FAIL (advertencia ausente). **BLOCKED** para cualquier bloqueo/confirmación obligatoria o permiso especial: es política comercial no definida; **no implementar**.
- **Regresión:** guarda `B8_Guarda_PrecioPorEncimaDelCosto_NoDevuelveAdvertencias`.
- **Decisión de negocio:** ¿bloquear, exigir confirmación o permiso (`precios.sellbelowcost`)? ¿Respecto de qué costo (neto, con IVA, con percepciones/envío)? El nombre `advertencias` es la propuesta que fija el test.

## B9 — Controles dinámicos y permisos
- **Causa raíz (3 hallazgos):**
  1. `wwwroot/js/producto-crear-modal.js` arma la fila del producto recién creado con **Editar, Eliminar y Comisión incondicionales** (~`:582-596`); el servidor sí gatea Editar con `puedeVerProducto && puedeEditarProducto` (`Index_tw.cshtml:694`). Un usuario con `productos.create` sin `update/delete` ve botones que el servidor rechaza.
  2. `Index_tw.cshtml:866` renderiza "Eliminar producto" en el menú de acciones **sin permiso**, aunque `Producto/Delete` exige `productos.delete`.
  3. `Index_tw.cshtml:670` renderiza el botón de **Comisión** sin permiso; `ActualizarComisionVendedor` exige `productos.edit`.
- **Fuera de alcance, anotado:** `Index_tw.cshtml:1202` (Eliminar **marca**) sin gate de permiso — el mismo patrón en Marca; sin auditar.
- **Riesgo de negocio:** el servidor rechaza (no hay escalada de privilegios), pero la UI ofrece acciones inválidas; fricción y falsa sensación de capacidad. Medio (UX/permisos).
- **Reproducción:** `B9_Catalogo_SoloLectura_NoOfreceEliminarNiEditarComision`, `B9_Catalogo_SoloCrear_OfreceCrearPeroNoEditarEliminarNiComision`, `B9_Catalogo_SoloCrear_ExponeAlScriptLosPermisosEfectivosDeLaFila`, `B9_ElScriptQueArmaLaFilaNueva_...` (contrato estático sobre el JS).
- **Corrección propuesta:** (a) `@if` por permiso en "Eliminar producto" y en el botón de Comisión (si no edita, mostrar el valor como texto); (b) publicar los permisos en el contenedor (`data-can-edit`, `data-can-delete`, `data-can-comision`) y que el constructor de filas los respete. Extender luego a Unidades/UnidadesGlobal/Alertas (sin barrer en esta entrega).
- **Limitación:** la prueba de la fila dinámica es de **contrato estático** (el JS consulta un permiso), no de ejecución en navegador. Una verificación con Playwright contra un usuario "solo crear" queda pendiente de turno/entorno autorizado (no se ejecutó contra PRD).
- **Decisión de negocio:** ninguna; los nombres `data-can-*` son la propuesta que fijan los tests.

---

## Resumen de decisiones de negocio pendientes
1. **B6:** bloqueo total vs. reversión parcial del evento; existencia de "revertir igualmente".
2. **B8:** política (advertir / confirmar / bloquear / permiso) y qué costo se compara.
3. **B5:** si el alcance "todo el catálogo" existe por la vía directa y con qué confirmación/umbral.
4. **B3:** alcance de la normalización (solo `Trim()` vs. colapsar internos / mayúsculas) y resolución manual de los grupos ya duplicados.
5. **B7:** un solo historial de precios o dos.

## No hecho / límites
- Sin cambios de producción, sin migraciones, sin datos tocados, sin ejecución contra PRD ni la base compartida.
- Batch por lista y OC no se auditaron para B7.
- B9 en navegador real y las vistas Unidades/Alertas quedan pendientes.
- Los tests nuevos están **rojos a propósito**: no deben entrar en una PR/CI hasta que se implementen las correcciones (o se marquen con `Skip` según el flujo de fix).
