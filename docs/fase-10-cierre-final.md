# Fase 10 - Cierre final

Fecha: 2026-05-07

## Diagnostico

Fase 10 queda cerrada desde el frente Producto. El diff revisado mantiene ProductoController como orquestador HTTP y delega validaciones/reglas en services. No se detectaron cambios incompatibles en los contratos HTTP/JSON existentes de create/edit/get; los errores de validacion duplicada ahora salen desde ProductoService como errores globales controlados, sin romper la forma general `{ success, errors }`.

## Cambios propios de Fase 10

- ProductoController elimina validaciones duplicadas de `ExistsCodigoAsync`.
- ProductoController elimina validacion manual duplicada de `RowVersion`.
- Edit POST tradicional queda cubierto por tests para RowVersion nula/vacia y caso valido.
- IVA de formularios queda delegado a `IProductoService.PrepararPrecioVentaConIvaAsync`.
- Conversion de precio con IVA a precio sin IVA queda delegada a `IProductoService.ObtenerPrecioVentaSinIva`.
- Fallback manual de comision desde `Request.Form` queda eliminado; la comision usa `DecimalModelBinder`.
- Caracteristicas de producto quedan normalizadas/sincronizadas en ProductoService.
- ProductoController queda sin helpers duplicados `NormalizarComisionPorcentaje`, `NormalizarCaracteristicas` y `ResolverPorcentajeIVAAsync`.
- Tests ampliados en ProductoControllerPrecioTests, ProductoServiceTests y DecimalModelBinderTests.

## Cambios heredados detectados en working tree

Estos cambios existen en el working tree pero no se consideran propios del cierre documental de Fase 10:

- Cambios de credito/venta/caja/reportes/comprobantes y sus tests asociados.
- Nuevas migraciones y snapshot EF para condiciones de pago y restricciones de cuotas.
- Entidades y DbContext para `ProductoCondicionPago` y `ProductoCondicionPagoTarjeta`.
- Resolver de condiciones de pago del carrito y DTOs asociados.
- UI/JS de condiciones de pago en catalogo.
- Evidencia QA y documentos de fases 8.x/9.x.
- Archivos de contrato PDF bajo `App_Data/contratos-venta-credito/1027`.

## ProductoController

Estado final confirmado:

- Create/CreateAjax delegan mapeo persistible y calculo de IVA.
- Edit/EditAjax delegan duplicados, RowVersion, caracteristicas e IVA al service.
- GetJson conserva forma JSON y expone precio sin IVA usando el service.
- ActualizarComisionVendedor conserva contrato JSON `{ success, message, comisionPorcentaje }`.
- Endpoints de condiciones de pago devuelven `{ success, data }`, BadRequest/NotFound controlados y guardado transaccional via service.

## Validacion ejecutada

- `dotnet build --no-incremental`: OK, 0 warnings, 0 errores.
- `dotnet test --filter "Producto|Decimal|Comision|Caracteristica|IVA|Alicuota|ProductoCondicionPago"`: OK, 452/452.
- `dotnet test --no-build`: OK, 2229/2229.
- `dotnet ef migrations has-pending-model-changes`: OK, sin cambios pendientes de modelo.
- `git diff --check`: OK, sin errores; solo avisos LF/CRLF del working tree.

## Riesgos y deuda pendiente

- El working tree mezcla Fase 10 con cambios heredados grandes de credito/venta/UI/DB. Antes de release conviene separar commit o PR por frente.
- Hay advertencias LF/CRLF al revisar diff; no bloquean, pero conviene normalizar politica de line endings fuera de este cierre.
- Las migraciones existentes son heredadas y fueron validadas con EF, pero no fueron modificadas en esta fase.

## Checklist release/refactor

- [x] Build no incremental sin warnings.
- [x] Tests filtrados de Producto/Decimal/Comision/Caracteristica/IVA/Alicuota/ProductoCondicionPago verdes.
- [x] Suite completa `--no-build` verde.
- [x] EF sin cambios pendientes de modelo.
- [x] `git diff --check` sin errores.
- [x] ProductoController sin duplicacion detectada en 10.1.
- [x] Contratos HTTP/JSON existentes sin cambio incompatible detectado.
- [ ] Separar cambios heredados de credito/venta/UI/DB en commit o PR propio.
- [ ] Decidir politica de line endings antes de merge final.

## Siguiente frente recomendado

Ordenar el working tree por frentes antes de release: primero aislar Fase 10 de Producto, luego revisar/commitear condiciones de pago DB/UI como frente heredado, y por ultimo credito/venta/caja por su mayor blast radius.
