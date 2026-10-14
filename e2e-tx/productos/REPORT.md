# QA-TX Productos / Precios / Stock — informe

Entorno: stack aislado `bury-qa-prd` (http://127.0.0.1:18092, base `BuryQA_PRD`), imagen `qa-9504428`. Nunca se tocó la base de desarrollo.
Oráculo de persistencia: `sqlcmd` dentro de `bury-qa-prd-db` (`lib/db.js`, verifica `DB_NAME()` en cada consulta).
Ejecución: `& "$env:USERPROFILE\.bury-qa\qa-run.ps1" prd -- npx playwright test -c e2e-tx/productos/playwright.config.js`
Corrida final: 41 casos — 32 PASS, 9 FAIL (todos hallazgos reales de la app, ninguno es fallo de la prueba), 0 BLOCKED.
Los casos FAIL aseveran el comportamiento deseado; el fallo ES el bug. Evidencia por caso: `evidence/cases/*.json`, capturas `evidence/*.png`, integridad global `evidence/integrity.json`.

## Bugs confirmados
| ID | Sev. | Caso | Descripción |
|---|---|---|---|
| B1 | Alta | S22a | `MovimientoStock/Create` (POST, escritura) exige solo `movimientos.view`; un rol con únicamente "Ver" registró una salida (stock 10→7). Existe la acción `movimientos.create` y no se usa. |
| B2 | Alta | PR13c | `Producto/EditAjax` solo exige `productos.update`: un usuario sin `precios.apply` cambió costo y precio (1000/1500 → 10/11) sin pasar por el control de precios. No hay permiso de costos/precios en la edición. |
| B3 | Media | P04 | El código no se normaliza: `"  CODIGO  "` (con espacios) se acepta como producto nuevo aunque `CODIGO` existe → 2 filas con el mismo código lógico (6 grupos al final de la corrida en `integrity.json`). Mayúsculas/minúsculas y exacto sí se rechazan. |
| B4 | Media | S21b | Sin protección contra doble envío: 3 POST idénticos simultáneos de una entrada generaron 2–3 movimientos (stock +10/+15 en vez de +5). La cadena de saldos queda consistente. |
| B5 | Media | PR15 | `AplicarCambioPrecioDirecto` con `alcance=filtrados` y filtros vacíos aplicó +1 % a 97/97 productos sin pedir confirmación. (Se revirtió; catálogo restaurado exacto.) |
| B6 | Media | PR11b | Revertir un evento viejo (E1) con un cambio posterior (E2) deja el precio en el valor previo a E1 y pisa E2 en silencio (2420→2000), sin advertencia. |
| B7 | Media | PR10b | Un cambio de precio hecho desde el modal Editar solo escribe `PreciosHistoricos`; el "Historial" del catálogo lee `CambioPrecioDetalles`, por lo que ese cambio no aparece ni se puede revertir. |
| B8 | Baja | PR12 | Precio de venta menor al costo (900 < 1000, margen −10 %) se acepta sin bloqueo ni confirmación (solo log). |
| B9 | Media (UX/permisos) | U04 | Rol solo-lectura ve los botones Crear producto, Ajuste Masivo y 110 botones Editar; el servidor los rechaza (403/302), pero la UI los ofrece. |

Observaciones (no fallan): `S19c` acepta cantidades fraccionarias (2,5) en unidad UN (stock 7,5); el modal de alta envía el costo "con IVA" (`PrecioCompraTotalConIva`) y el servidor guarda el neto — el margen mostrado coincide con el del servidor (markup sobre costo: 1000→1600 = 60 %); ajuste de stock de tipo "Ajuste" registra el delta con saldo absoluto correcto.

## Matriz
| # | Escenario | Caso | Resultado | Evidencia/oráculo |
|---|---|---|---|---|
| 1 | Crear producto código único | P01, U01 (UI modal) | PASS | DB fila única, stock/mov inicial |
| 2 | Persistencia | P02, U01/U02 | PASS | GetJson + fila del catálogo |
| 3 | Editar nombre/costo/precio/atributos | P03, U02 (UI) | PASS | DB + PreciosHistoricos 1000→1200 / 1500→1800 |
| 3b | Edición no altera stock; RowVersion obsoleto rechazado | P03b, P03c | PASS | DB sin cambio |
| 4 | Códigos duplicados | P04 | **FAIL (B3)** | exacto/case rechazados; con espacios aceptado |
| 5 | Obligatorios / inválidos (18 variantes) | P05 | PASS | ninguna variante creó fila |
| 6 | Permisos lectura/edición | P06a/b/c, U04 | PASS (servidor) / **FAIL UI (B9)** | lector 403 sin cambios; oper crea/edita, no elimina; anónimo redirigido |
| 7 | Cambiar precio de venta | PR07 | PASS | 2000→2200, costo intacto, evento+detalle |
| 8 | Precio en catálogo | PR08, U02, U03 | PASS | `data-sort-precio` de la fila = DB |
| 9 | Precio en nueva cotización/venta | PR09 | PASS | `/Cotizacion/BuscarProductos` y `/api/ventas/BuscarProductos` = 2200 |
| 10 | Historial | PR10 PASS; PR10b | PASS / **FAIL (B7)** | |
| 11 | Revertir | PR11 PASS (y rechaza doble reversión); PR11b | PASS / **FAIL (B6)** | |
| 12 | Margen/rentabilidad | PR12 | **FAIL (B8)** margen OK, bajo costo sin bloqueo | server 60 %, UI 50 % en alta (1000→1500) = markup |
| 13 | Sin permiso no altera precios/costos | PR13a, PR13b PASS; PR13c | PASS / **FAIL (B2)** | |
| 14 | Stock inicial controlado | S14, U01 | PASS | mov "Stock inicial" 0→N |
| 15 | Entrada | S15 | PASS | 10→15, ref/motivo/usuario |
| 16 | Salida | S16 | PASS | 15→11 |
| 17 | Movimientos y saldo | S17 | PASS | cadena íntegra; ajuste absoluto registra delta; Kardex y ListJson coinciden |
| 18 | Retirar más de lo disponible | S18, S21c | PASS | "Stock insuficiente", sin movimiento; 4 salidas concurrentes: 1 éxito, stock 2 |
| 19 | Stock cero | S19, S19b | PASS | salida sobre 0 rechazada; Venta oculta sin stock, Cotización/Ajuste lo muestran; 9 entradas inválidas sin efectos |
| 20 | Catálogo vs búsqueda Venta | S20, U03 | PASS | stock y precio idénticos DB/GetJson/Info/Venta; inactivo excluido |
| 21 | Operaciones repetidas | S21a PASS (5 movs legítimos); S21b | PASS / **FAIL (B4)** | |
| – | Permiso de escritura de stock | S22a / S22b | **FAIL (B1)** / PASS | |

Integridad global (`integrity.json`): 0 stocks negativos, 0 precios negativos, 0 desvíos stock vs último movimiento, 0 cadenas rotas. Las 6 filas "con stock y sin movimiento" son el seed `E2EGENERIC-*` (carga directa), no un defecto. Único problema de integridad: códigos duplicados normalizados (B3).

## Datos creados
Productos con prefijo `QAPRD<run>-…` (≈104), roles `QA_PRD_LECTOR`/`QA_PRD_OPER` y usuarios `qaprd_lector`/`qaprd_oper` (clave de QA en `global-setup.js`), solo en `BuryQA_PRD`.
