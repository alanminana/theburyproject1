# RESCATE-PRODUCTO-P0: edición de Subcategoría y Submarca

Fecha: 2026-10-19 · Base: `main` @ `0d3930a1988868cbc7109d215d7b8e36ffae10b7`

## Defecto previo

`ProductoService.UpdateAsync` cargaba el producto existente, validaba y copiaba casi todos los campos,
pero **no asignaba `SubcategoriaId` ni `SubmarcaId`** (sí `CategoriaId` y `MarcaId`). El modal Editar del
catálogo (`Producto/EditAjax`) enviaba ambos campos y respondía `success`, pero la base conservaba los valores
anteriores. `ValidarProductoAsync` validaba la pertenencia de la subcategoría a la categoría, pero no existía la
validación equivalente para la submarca (se podía persistir una submarca ajena a la marca elegida, p. ej. vía `CreateAsync`).

Los únicos llamadores de `UpdateAsync` son `ProductoController.EditAjax` (la página `Edit` está huérfana).

## Corrección

- `Services/ProductoService.cs`: `UpdateAsync` asigna `SubcategoriaId` y `SubmarcaId`.
  `ValidarProductoAsync` (compartido por Create y Update) valida la submarca cuando viene informada:
  existe, no está eliminada, `ParentId == MarcaId` y está activa.
  Vigencia: el selector (`MarcaService.GetChildrenAsync`) solo ofrece submarcas activas, así que una submarca
  inactiva *distinta de la ya persistida* se rechaza; la ya asignada se tolera para no bloquear la edición de
  otros campos de un producto cuya submarca se dio de baja después. No se inventa ninguna política comercial nueva.
  La validación de subcategoría quedó como estaba.
- `Controllers/ProductoController.cs` (`EditAjax`): ver criterio de nulos.

## Criterio para valores nulos

| Caso | Resultado |
|---|---|
| Campo enviado con valor | se valida y persiste |
| Campo enviado vacío (`<select>` en "Sin subcategoría/submarca" → `""`) | `null` persistido (quitar la selección es intencional) |
| Campo **no enviado** en el formulario | se conserva el valor persistido |

El binding de MVC no distingue "vacío" de "ausente" (ambos dan `null`), por eso `EditAjax` mira
`ModelState.ContainsKey(...)` (solo registra los campos enviados; el contrato de código prohíbe `Request.Form` en el controller) y rellena desde la base solo cuando la clave falta. `UpdateAsync` trata
`null` como "quitar", sin ambigüedad: el contrato de servicio es explícito.

## Pruebas

`ProductoServiceTests` (PROD01–PROD08, 12 casos) y `ProductoSubcategoriaSubmarcaHttpTests` (PROD02, PROD04, PROD09, PROD10).

- Antes de la corrección (código de `main`): 12 de 17 rojos, entre ellos PROD01 (esperado submarca/subcategoría B, quedaba A: 5 en vez de 6).
  PROD09 pasa antes y después: es una guarda para que la corrección no borre relaciones.
- Después: 17/17 verdes. Cada prueba de persistencia relee con un `DbContext` nuevo.
- PROD06 (RowVersion obsoleto): SQLite no rota `RowVersion` en UPDATE, por lo que la prueba emula el comportamiento de
  SQL Server con `UPDATE ... SET RowVersion = randomblob(8)`.
- PROD07/PROD08: stock protegido y historial de precios conservados al cambiar relaciones; los permisos `precios.apply`
  siguen cubiertos por `QaPrdP1HttpTests`/`ProductoControllerPrecioTests` (sin cambios).

## Endurecimiento del modal Editar (carga asíncrona)

`producto-editar-modal.js` cargaba las opciones de Subcategoría/Submarca por `fetch` *después* de abrir el modal, con el
select ya vaciado y el botón «Guardar cambios» habilitado. Guardar antes de la respuesta (o con la petición fallida,
cuyo `.catch` estaba vacío) enviaba `SubcategoriaId=""` y `SubmarcaId=""`, que `EditAjax` interpreta correctamente
como «quitar la selección»: se borraban relaciones guardadas sin que el usuario lo eligiera.

Contrato actual del cliente:

- Carga pendiente: el select queda deshabilitado (un campo deshabilitado no viaja, y el servidor conserva lo guardado) y el
  botón Guardar queda inactivo; un submit forzado se bloquea con un aviso.
- Carga fallida: el select sigue deshabilitado y muestra la relación guardada en solo lectura; guardar no la toca. Si la falla
  ocurre tras un cambio de categoría/marca hecho por el usuario, el guardado se bloquea (no hay opciones válidas que enviar).
- Solo un select habilitado y cargado puede enviar `""`, y eso significa una elección deliberada («Sin subcategoría/submarca»).
- Cada carga lleva un token por select y la apertura una secuencia: las respuestas tardías o fuera de orden (otra marca,
  otro producto, `GetJson` de una apertura anterior) se descartan.

Regresión: `e2e-js/producto-editar-modal-dependientes.spec.js` (12 casos). Carga el JS real en una página sintética y
controla cada respuesta HTTP con compuertas de `page.route`; no requiere app, base ni Docker:
`npx playwright test -c playwright.js-harness.config.js`. Contra el JS anterior fallan 9 de 12 (los 3 verdes son los caminos
felices); con el actual pasan los 12.

## Limitaciones conocidas

- La concurrencia optimista real (rowversion de SQL Server) solo se ejercita en el CI con SQL Server; localmente se emula.
- El modal Editar se validó en navegador solo con respuestas HTTP controladas (`e2e-js/`), no contra una base real; esa regresión no está cableada al CI.
- No hay UI que advierta si, al cambiar la marca, la submarca previa deja de corresponder: el JS ya recarga y vacía
  el selector de submarcas, y el servidor ahora lo rechaza si llegara una combinación inválida.
