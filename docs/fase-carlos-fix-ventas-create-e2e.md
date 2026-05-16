# Fase: Carlos — Fix Ventas/Create flujo E2E

**Fecha:** 2026-05-16  
**Rama:** `carlos/fix-ventas-create-e2e`  
**Responsable:** Carlos (agente QA E2E)

---

## Síntomas reportados

1. La búsqueda de productos devuelve resultados pero no se pueden agregar al carrito.
2. La tabla de productos (detalles) permanece vacía.
3. Los campos de "Detalle de cobro" no aparecen o están vacíos.
4. Los totales permanecen en $0.00.
5. El botón "Confirmar transacción" aparece pero el flujo está roto.

Todos los síntomas ocurren **exclusivamente en el flujo modal** (`Index_tw.cshtml` → `_VentaCrearModal.cshtml`).  
El flujo de página completa `/Venta/Create` funcionaba correctamente.

---

## Causa raíz

**Archivo afectado:** `Views/Venta/_VentaCrearModal.cshtml`  
**Causa:** Faltaba `<input type="hidden" id="hdn-producto-requiere-numero-serie" />` en el panel `panel-agregar-produto` del modal.

### Cadena de eventos

1. `venta-create.js` (líneas 79–99) captura referencias DOM al inicializarse con `document.querySelector()`.
2. En el contexto del modal (`Index_tw`), `$('#hdn-produto-requiere-numero-serie')` retorna `null` porque ese input no existía en `_VentaCrearModal.cshtml`.
3. En el handler de click del dropdown de productos (línea 1024), el código hace:
   ```js
   hdnProductoRequiereNumeroSerie.value = requiereNumeroSerie ? 'true' : 'false';
   ```
   Sin optional chaining (`?.`). Cuando `hdnProductoRequiereNumeroSerie` es `null`, lanza:
   ```
   TypeError: Cannot set properties of null (setting 'value')
   ```
4. El error aborta el handler completo → `show(panelAgregarProducto)` (línea 1033) nunca se ejecuta.
5. Sin panel de agregar producto visible, el usuario no puede confirmar la selección.
6. Sin productos en `detalles[]`, `renderDetalles()` y `recalcularTotales()` nunca se invocan → tabla vacía, totales en $0.00.

**Por qué `Create_tw.cshtml` funcionaba:** tiene `id="hdn-produto-requiere-numero-serie"` en línea 315, por lo que la referencia no es `null`.

---

## Archivos modificados

### `Views/Venta/_VentaCrearModal.cshtml`

Agregado el input faltante después de `hdn-produto-stock`:

```html
<input type="hidden" id="hdn-produto-id" />
<input type="hidden" id="hdn-produto-codigo" />
<input type="hidden" id="hdn-produto-precio" />
<input type="hidden" id="hdn-produto-stock" />
<input type="hidden" id="hdn-produto-requiere-numero-serie" />  ← AGREGADO
```

### `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs`

Agregados 4 tests de contrato al final de la clase (antes de helpers privados):

| Test | Propósito |
|------|-----------|
| `VentaCrearModal_TieneHiddenInputRequiereNumeroSerie` | Regresión: verifica que el input faltante existe en el modal |
| `VentaCreate_View_TieneContenedorDetalleCobro` | Verifica que `Create_tw` tiene sección "Detalle de cobro" con `panel-tarjeta` y `panel-cheque` |
| `VentaCreate_View_TieneBotonOAccionAgregarProducto` | Verifica que `Create_tw` tiene botón de agregar producto (`btn-agregar-produto` o `btn-agregar-producto`) |
| `VentaCreateJs_ContieneHandlerAgregarProducto` | Verifica que el JS contiene el handler de agregar producto, `renderDetalles()` y `recalcularTotales()` |

---

## Validaciones ejecutadas

| Comando | Resultado |
|---------|-----------|
| `dotnet build --configuration Release` | ✅ 0 errores, 0 advertencias |
| `dotnet test --filter "VentaCreateUiContractTests"` | ✅ 40/40 |
| `dotnet test --filter "Venta|VentaApiController|..."` | ✅ 982/982 |

---

## Qué NO se tocó

- `venta-create.js` — el bug estaba en el HTML del modal, no en el JS. No se agregó optional chaining ni se modificó el handler.
- `venta-criar-modal.js` — sin cambios.
- `Create_tw.cshtml` — sin cambios; el flujo de página completa ya funcionaba.
- `Index_tw.cshtml` — sin cambios.
- Backend: `VentaController`, `VentaApiController`, servicios, migraciones — sin cambios.
- Módulos: Cotización, Caja, Factura, Stock, ProductoUnidad — sin tocar.
- Fixes previos de Kira: `configuracionPagosGlobal`, fallback Razor de medios de pago, TestHost — intactos.

---

## Riesgos y deuda remanente

1. **Inconsistencia de nomenclatura de IDs:** El JS y los inputs del modal usan `produto` (sin 'c') mientras que `Create_tw` usa `producto` (con 'c'). El JS captura por el ID completo, por lo que funciona, pero es una trampa de mantenimiento futura.

2. **Crash silencioso en línea 1024:** La causa raíz del error era la falta del input, pero el acceso sin optional chaining en línea 1024 de `venta-create.js` sigue siendo frágil. Si otro contexto carga el JS sin ese elemento, el crash silencioso se repetirá. Queda como deuda defensiva futura (baja prioridad ahora que el contrato de tests lo protege).

3. **Desajuste columnas tabla modal:** La tabla del modal tiene 8 columnas en el `<thead>` pero `renderDetalles()` renderiza 7. Es un desajuste cosmético sin impacto funcional.

---

## Tests de regresión agregados

- `VentaCrearModal_TieneHiddenInputRequiereNumeroSerie` → protege contra la misma omisión futura.
- Los demás tests refuerzan el contrato UI ya existente del flujo de creación.
