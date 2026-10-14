# QA-PRD — Diagnóstico sin corrección (B3–B9)

Solo propuesta; no se implementó nada de esto. Referencias sobre `fix/qa-prd-p0-permisos`.

## B3 — Códigos duplicados con espacios
**Causa:** `ProductoService.ExistsCodigoAsync` compara `p.Codigo == codigo` sin normalizar (`Services/ProductoService.cs:665`); `"  CODIGO  "` ≠ `"CODIGO"`. Mayúsculas ya se rechazan por la collation de SQL Server.
**Propuesta:** normalizar (`Trim()`, y colapsar espacios internos si el negocio lo quiere) en un único punto —`CreateAsync`/`UpdateAsync` antes de validar y persistir— y también en `ExistsCodigoAsync`. Migración de datos: detectar grupos duplicados con `LTRIM(RTRIM(Codigo))` (6 en `integrity.json`), decidir a mano cuál conservar y recién después agregar un índice único filtrado (`IsDeleted = 0`) sobre el código normalizado. Test: crear con espacios → se guarda trimmeado o se rechaza.
**Riesgo:** el índice único falla si ya hay duplicados reales en producción; limpiar primero.

## B4 — Movimientos simultáneos duplicados
**Causa:** `MovimientoStockService.RegistrarAjusteAsync` lee el producto, calcula y escribe en una transacción, sin token de idempotencia: tres POST idénticos son tres movimientos legítimos para el servidor. La cadena de saldos queda consistente (la concurrencia del stock sí está protegida: S21c, 4 salidas → 1 éxito).
**Propuesta:** idempotencia de formulario, no del saldo: token por render (`Guid` oculto en el form de `Create`/modal/ficha) guardado con índice único (`MovimientoStock.RequestId` nullable, único filtrado); un reenvío con el mismo token devuelve el movimiento ya creado. Complemento de UX: deshabilitar el submit al enviar. No usar ventana de tiempo por contenido (dos entradas iguales seguidas pueden ser reales: S21a).
**Riesgo:** migración con columna nueva; los flujos internos (venta, OC) no envían token y quedan igual.

## B5 — Ajuste masivo con filtros vacíos
**Causa:** `AplicarCambioPrecioDirecto` con `alcance=filtrados` y filtros vacíos resuelve "todos" (97/97) sin confirmación; la UI ya ofrece "todo el catálogo" como alcance explícito.
**Propuesta:** en el servicio, `filtrados` con todos los filtros vacíos se rechaza ("indicá al menos un filtro o elegí Todo el catálogo"); `todos` exige un campo de confirmación (cantidad afectada devuelta por la simulación, verificada en servidor contra el conteo real). Mantener `precios.apply`. Test: filtros vacíos → 400/mensaje, 0 cambios, 0 eventos.

## B6 — Reversión que pisa cambios posteriores
**Causa:** `PrecioService.RevertirCambioPrecioEventoAsync` asigna `producto.PrecioVenta = detalle.PrecioAnterior` (`Services/PrecioService.cs:~612`) sin comparar el precio actual con `detalle.PrecioNuevo`.
**Propuesta:** por producto, si `PrecioVenta != detalle.PrecioNuevo` hay un cambio posterior → no revertir ese producto (resultado parcial con lista de omitidos y motivo) o exigir confirmación explícita "revertir igualmente"; por defecto omitir. Registrar en el evento de reversión qué productos se omitieron. Test: E1, E2 sobre el mismo producto, revertir E1 → producto omitido, precio de E2 intacto.
**Decisión de negocio:** ¿bloquear todo el evento o revertir parcialmente?

## B7 — Cambios sin historial
**Causa:** dos historiales distintos: `ProductoService.UpdateAsync` escribe `PreciosHistoricos` y el "Historial" del catálogo lee `CambioPrecioDetalles` (`CatalogoService.GetHistorialCambiosPrecioProductoAsync`), así que un cambio desde el modal Editar no se ve ni se puede revertir.
**Propuesta:** que `UpdateAsync` (cuando cambian importes y el usuario tiene `precios.apply`, ya garantizado por B2) cree también un `CambioPrecioEvento` de alcance `individual`/`producto` con su detalle, en la misma transacción, reutilizando el servicio de precios en lugar de duplicar la lógica; `PreciosHistoricos` se conserva. Alternativa: que el historial lea ambas fuentes. Test: editar precio por el modal → aparece en historial y es revertible.
**Nota:** B2 ya garantiza que solo quien puede aplicar precios llega a este camino.

## B8 — Venta bajo costo sin advertencia
**Causa:** no hay validación margen < 0 (solo log).
**Propuesta (sin cambiar política):** advertencia no bloqueante en el modal de alta/edición y en el ajuste masivo ("margen −10 %: precio menor al costo") y en la respuesta de la simulación; sin impedir guardar. Bloqueo o permiso especial solo si el negocio confirma una regla. Test: se acepta y devuelve `advertencias[]` con el margen.

## B9 — Acciones visibles sin autorización
**Estado:** resuelto en la parte de catálogo/stock por QA-PRD-P0 (ver reporte). **Pendiente** de barrer: la fila que el JS arma tras crear un producto (`producto-crear-modal.js`) y otras pantallas de productos/stock no cubiertas (Unidades, UnidadesGlobal, Alertas). Propuesta: exponer los permisos efectivos a los scripts con un `data-can-*` en el contenedor y que el constructor de filas los respete; test de render por rol sobre esas vistas.
