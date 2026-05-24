# KIRA-VENTAS-MODAL-REWORK-0 — Auditoría comparativa: Modal Nueva Venta

**Tipo:** Auditoría comparativa / planificación técnica / doc-only  
**Fase:** KIRA-VENTAS-MODAL-REWORK-0  
**Fecha:** 2026-05-24  
**Estado:** CERRADA — base de planificación para fases 1A–1G + QA

---

## A. Objetivo

Comparar el modal actual de Nueva Venta (`_VentaCrearModal.cshtml`) con el HTML de diseño nuevo provisto por el usuario, para definir cómo adaptarlo a Razor separando:

- Estructura Razor
- CSS separado
- JS separado
- Contratos existentes que no se pueden romper
- Funciones JS actuales vs. nuevas propuestas
- Riesgos de integración
- Roadmap de fases

El objetivo **no** es crear una vista nueva. Es reemplazar/retrabajar el modal actual manteniéndolo como modal.

---

## B. Base y contexto

- **Rama base:** `main` — commit `99dfd87`
- **HEAD al iniciar:** `99dfd87 Rehacer tabs reales de unidades de producto (MISA-INVENTARIO-FISICO-UX-2N)`
- **Modal actual canónico:** `Views/Venta/_VentaCrearModal.cshtml`
- **Vista Create canónica:** `Views/Venta/Create_tw.cshtml`
- **JS productivo:** `wwwroot/js/venta-create.js` (2343 líneas)
- **CSS productivo:** `wwwroot/css/venta-module.css`

> **NOTA IMPORTANTE:** El HTML de diseño nuevo no fue incluido en esta sesión.  
> Las secciones D y F describen la estructura esperada según el brief del prompt.  
> Cuando el usuario provea el HTML, actualizar la sección F con el mapa real.

---

## C. Archivos auditados

| Archivo | Rol |
|---|---|
| `Views/Venta/_VentaCrearModal.cshtml` | Modal fullscreen canónico — 929 líneas |
| `Views/Venta/Create_tw.cshtml` | Vista standalone — 972 líneas, comparte IDs con el modal |
| `wwwroot/js/venta-create.js` | JS productivo — 2343 líneas, carga en ambas vistas |
| `wwwroot/css/venta-module.css` | CSS canónico — clases `vm-*` y `venta-*` |
| `TheBuryProyect.Tests/Unit/VentaCreateUiContractTests.cs` | Tests de contrato UI — 60+ asserts sobre IDs, nombres y contenido |
| `e2e/ui-4e-layout-visual.spec.js` | E2E Playwright visual |
| `docs/ventas-ux-*.md` | 12 documentos de fases UX anteriores |

---

## D. Resumen del HTML nuevo (según brief)

El diseño nuevo propuesto incluye:

| Elemento | Descripción |
|---|---|
| `#modal-crear-venta` | Modal fullscreen — coincide con el actual |
| Pasos internos | `Cliente` / `Productos` / `Pago` / `Crédito` / `Revisión` — estructura de wizard |
| Resumen sticky lateral | Panel derecho sticky con totales |
| Sticky bar mobile | Barra inferior fija |
| `#modal-pago-item` | Sub-modal de pago por producto — existe en el actual |
| `#modal-documentacion` | Sub-modal documentación — existe en el actual |
| `#modal-confirmar-operacion` | **Nuevo** — no existe en el sistema actual |
| CSS inline `<style>` | Requiere extracción a archivo separado |
| JS inline `<script>` | Requiere extracción a archivo separado |
| Tailwind CDN | Ya disponible globalmente — no copiar el `<link>` |
| Google Fonts externos | Usar fuentes ya configuradas globalmente |
| Material Symbols | Ya disponible globalmente |
| Valores mock / hardcoded | No copiar — sustituir con Razor / ViewBag / asp-for |
| Opciones de pago hardcoded | No copiar — sustituir con `@foreach (tiposPago)` |
| Comentarios Razor simulados | Reescribir como `@*...*@` reales |

---

## E. Modal actual: mapa funcional completo

### E.1 Estructura raíz

```
#modal-crear-venta              div.fixed.inset-0.z-50 (overlay fullscreen, aria-modal)
  #modal-crear-venta-backdrop   backdrop semitransparente (click cierra)
  .vm-modal-panel               panel central max-w-7xl
    header.sticky               header sticky con btn-cerrar
    #venta-ajax-validation-summary  resumen de errores AJAX
    .vm-mobile-summary-bar      barra sticky mobile con total y btn confirmar
    .p-6                        contenedor del formulario
      #venta-form               form POST /Venta/CreateAjax (action real)
        @Html.AntiForgeryToken()
        input hidden Estado (EstadoVenta enum)
        grid 1/3 + 2/3
          columna izq (2/3)
            § 1 Datos generales
            § 2 Selección de productos
            § 3 Detalle de cobro
          columna der (1/3)
            § 4 Verificación crediticia (hidden por defecto)
            § 5 Vendedor y observaciones
            § 6 Totales y confirmación (.vm-totals)
        #detalles-hidden-inputs (hidden)
#modal-pago-item               sub-modal tipo de pago por producto (z-70)
#modal-documentacion           sub-modal documentación crediticia (z-60)
```

### E.2 Sección 1 — Datos generales

| Elemento | ID / name | Tipo |
|---|---|---|
| Búsqueda cliente | `#input-buscar-cliente` | input text autocomplete |
| Dropdown clientes | `#dropdown-clientes` | div dropdown |
| Hidden ClienteId | `name="ClienteId"` `id="hdn-cliente-id"` | input hidden |
| Panel cliente info | `#info-cliente` | div (hidden→show) |
| Nombre cliente | `#info-cliente-nombre` | p |
| Doc cliente | `#info-cliente-doc` | p |
| Btn limpiar cliente | `#btn-limpiar-cliente` | button |
| Fecha operación | `name="FechaVenta"` `id="FechaVenta"` | input date |
| Selector tipo pago | `name="TipoPago"` `id="select-tipo-pago"` | select, cargado por Razor desde ViewBag.TiposPago |
| Panel aviso crédito | `#panel-aviso-credito` | div (hidden) |
| Crédito disponible | `#credito-disponible` | p |
| Crédito solicitado | `#credito-solicitado` | p |
| Crédito margen | `#credito-margen` | p |

### E.3 Sección 2 — Productos

| Elemento | ID | Tipo |
|---|---|---|
| Filtro categoría | `#filtro-categoria` | select, ViewBag.CategoriasFiltro |
| Filtro marca | `#filtro-marca` | select, ViewBag.MarcasFiltro |
| Filtro precio min | `#filtro-precio-min` | input number |
| Filtro precio max | `#filtro-precio-max` | input number |
| Filtro solo stock | `#filtro-solo-stock` | checkbox |
| Búsqueda producto | `#input-buscar-producto` | input text autocomplete |
| Dropdown productos | `#dropdown-productos` | div dropdown |
| Panel agregar | `#panel-agregar-producto` | div (hidden→show) |
| Texto producto seleccionado | `#txt-producto-seleccionado` | input readonly |
| Hidden producto id | `#hdn-producto-id` | input hidden |
| Hidden producto código | `#hdn-producto-codigo` | input hidden |
| Hidden producto precio | `#hdn-producto-precio` | input hidden |
| Hidden producto stock | `#hdn-producto-stock` | input hidden |
| Hidden requiere N/S | `#hdn-producto-requiere-numero-serie` | input hidden |
| Cantidad | `#txt-cantidad` | input number |
| Error stock | `#stock-error` | p (hidden) |
| Advertencia stock sin identificar | `#advertencia-stock-sin-identificar` | div (hidden) |
| Descuento item | `#txt-descuento-item` | input number |
| Btn agregar producto | `#btn-agregar-producto` | button type=button |
| Panel selector unidad (trazabilidad) | `#panel-selector-unidad` | div (hidden) |
| Select unidad | `#select-producto-unidad` | select |
| Error unidad | `#producto-unidad-error` | p (hidden) |
| Aviso sin unidades | `#aviso-sin-unidades` | div (hidden) |
| Link gestionar unidades | `#link-gestionar-unidades` | a |
| Tabla detalles | `#tbody-detalles` | tbody |
| Estado vacío | `#detalles-vacio` | div |
| Badge cantidad productos | `#detalle-items-badge` | span |
| Scroll horizontal detalles | `#venta-detalles-scroll` `data-oc-scroll` | div |

### E.4 Sección 3 — Detalle de cobro

| Panel | ID | Activo cuando |
|---|---|---|
| Tarjeta | `#panel-tarjeta` | TipoPago = TarjetaCredito o TarjetaDebito |
| Cheque | `#panel-cheque` | TipoPago = Cheque |
| Crédito personal | `#panel-credito-personal` | TipoPago = CreditoPersonal |
| Planes de pago | `#panel-planes-pago` | Tarjeta, MercadoPago |
| Diagnóstico condiciones | `#panel-diagnostico-condiciones-pago` | hidden (stub en Create) |

Dentro del panel tarjeta:

| Elemento | name / id |
|---|---|
| Select tarjeta | `name="DatosTarjeta.ConfiguracionTarjetaId"` `id="select-tarjeta"` |
| Hidden nombre tarjeta | `name="DatosTarjeta.NombreTarjeta"` `id="hdn-tarjeta-nombre"` |
| Hidden tipo tarjeta | `name="DatosTarjeta.TipoTarjeta"` `id="hdn-tarjeta-tipo"` |
| Select cuotas | `name="DatosTarjeta.CantidadCuotas"` `id="select-cuotas-tarjeta"` |
| Hidden plan id | `name="DatosTarjeta.ConfiguracionPagoPlanId"` `id="hdn-configuracion-pago-plan-id"` |
| N° autorización | `name="DatosTarjeta.NumeroAutorizacion"` `id="txt-num-autorizacion-tarjeta"` |
| Panel resumen tarjeta | `#panel-tarjeta-resumen` | |
| Monto por cuota | `#tarjeta-monto-cuota` | |
| Total con interés | `#tarjeta-total-interes` | |
| Recargo | `#tarjeta-recargo` | |
| Aviso cuotas sin interés | `#panel-aviso-cuotas-sin-interes` | |

Dentro del panel cheque:

| Elemento | name / id |
|---|---|
| N° cheque | `name="DatosCheque.NumeroCheque"` `id="txt-num-cheque"` |
| Banco | `name="DatosCheque.Banco"` `id="txt-banco-cheque"` |
| Titular | `name="DatosCheque.Titular"` `id="txt-titular-cheque"` |
| CUIT | `name="DatosCheque.CUIT"` `id="txt-cuit-cheque"` |
| Fecha emisión | `name="DatosCheque.FechaEmision"` `id="txt-fecha-emision-cheque"` |
| Fecha vencimiento | `name="DatosCheque.FechaVencimiento"` `id="txt-fecha-vencimiento-cheque"` |
| Monto | `name="DatosCheque.Monto"` `id="txt-monto-cheque"` |

### E.5 Sección 4 — Verificación crediticia

| Elemento | ID | Notas |
|---|---|---|
| Panel contenedor | `#panel-verificacion-crediticia` | hidden, show solo con CreditoPersonal |
| Btn verificar | `#btn-verificar-elegibilidad` | type=button |
| Feedback slot | `#venta-create-feedback-slot` | aria-live=polite |
| Panel resultado | `#panel-resultado-verificacion` | hidden→show |
| Badge verificación | `#verificacion-badge` | |
| Estado SCORE | `#verificacion-estado` | span |
| Límite crédito | `#verificacion-limite` | |
| Crédito utilizado | `#verificacion-utilizado` | |
| Cupo disponible | `#verificacion-saldo` | |
| Barra progreso | `#verificacion-barra` | |
| Panel cupo suficiente | `#panel-cupo-suficiente` | |
| Panel cupo insuficiente | `#panel-cupo-insuficiente` | role=alert |
| Detalle cupo | `#cupo-insuficiente-detalle` | |
| Panel motivos | `#panel-motivos` | |
| Lista motivos | `#lista-motivos` | |
| Panel mora | `#panel-alerta-mora` | role=alert |
| Texto mora | `#alerta-mora-texto` | |
| Panel documentación faltante | `#panel-documentacion-faltante` | role=alert |
| Lista docs faltantes | `#lista-docs-faltantes` | |
| Btn cargar documentación | `#btn-cargar-documentacion` | data-venta-modal-action/target |
| Panel excepción crediticia | `#panel-excepcion-crediticia` | condicional @if (puedeExcepcion) |
| Hidden excepción | `name="AplicarExcepcionDocumental"` `id="hdn-aplicar-excepcion"` | |
| Panel excepción inactiva | `#panel-excepcion-inactiva` | |
| Btn aplicar excepción | `#btn-aplicar-excepcion` | |
| Panel excepción activa | `#panel-excepcion-activa` | hidden |
| Textarea motivo excepción | `name="MotivoExcepcionDocumentalCreate"` `id="txt-excepcion-documental"` | |
| Btn confirmar excepción | `#btn-confirmar-excepcion` | |
| Btn cancelar excepción | `#btn-cancelar-excepcion` | |

> En el modal `_VentaCrearModal.cshtml`, el panel de excepción existe dentro del panel de verificación crediticia.  
> En `Create_tw.cshtml` existe el panel adicional `#panel-contrato-venta` (Documentación contractual obligatoria).

### E.6 Sección 5 — Vendedor y observaciones

| Elemento | ID / name | Condicional |
|---|---|---|
| Select vendedor | `name="VendedorUserId"` `id="VendedorUserId"` | Solo si puedeDelegarVendedor && vendedores != null |
| Display vendedor auto | display name del usuario logueado | else branch |
| Textarea observaciones | `name="Observaciones"` `id="Observaciones"` | siempre |

### E.7 Sección 6 — Totales y confirmación

| Elemento | ID / name | Tipo |
|---|---|---|
| Subtotal display | `#total-subtotal` | span |
| Descuento label | `#total-descuento-label` | span |
| Descuento display | `#total-descuento` | span |
| IVA display | `#total-iva` | span |
| Total final | `#total-final` | span |
| Hidden subtotal | `name="Subtotal"` `id="hdn-subtotal"` | input hidden |
| Hidden descuento | `name="Descuento"` `id="hdn-descuento"` | input hidden |
| Hidden IVA | `name="IVA"` `id="hdn-iva"` | input hidden |
| Hidden total | `name="Total"` `id="hdn-total"` | input hidden |
| Btn confirmar | `#btn-confirmar` | type=button, onclick=VentaCrearModal.submit() |
| Sticky total mobile | `#vm-modal-sticky-total` | span — espejo de #total-final vía MutationObserver |

### E.8 Sub-modales

**#modal-pago-item** (z-70):

| Elemento | ID |
|---|---|
| Título producto | `#modal-pago-item-titulo` |
| Select tipo pago item | `#select-tipo-pago-item` (opciones hardcoded — ver riesgo F.4) |
| Panel planes item | `#modal-pago-item-planes` |
| Panel resumen item | `#modal-pago-item-resumen` |
| Celdas resumen | `#modal-plan-producto` `#modal-plan-precio-base` `#modal-plan-cuotas-label` `#modal-plan-ajuste` `#modal-plan-total` `#modal-plan-cuota` |
| Btn guardar pago item | `#btn-guardar-pago-item` |
| Btn cerrar (×2) | `.btn-cerrar-pago-item` |

**#modal-documentacion** (z-60):

| Elemento | ID |
|---|---|
| Overlay (cierra) | `#modal-documentacion-overlay` data-venta-modal-action=close |
| Btn cerrar doc | `#btn-cerrar-modal-doc` |
| Lista docs | `#modal-lista-docs` |
| Input archivo | `#input-doc-archivo` |
| Nombre archivo | `#doc-archivo-nombre` |
| Select tipo documento | `#select-tipo-documento` |
| Feedback upload | `#doc-upload-feedback` |
| Btn subir documento | `#btn-subir-documento` |
| Btn ir documentación | `#btn-ir-documentacion` |

---

## F. HTML nuevo: mapa estructural (desde brief — actualizar con HTML real)

> Nota: sección basada en la descripción del prompt. Completar cuando se provea el HTML.

| Estructura propuesta | Estado vs. actual |
|---|---|
| `#modal-crear-venta` como raíz | Compatible — mismo ID |
| Header con título y btn cerrar | Compatible — reemplaza el header actual |
| Wizard de pasos (tabs/steps) | **Nuevo** — el actual usa layout columnar sin steps |
| Resumen sticky lateral | Compatible — el actual ya tiene columna derecha sticky |
| Sticky bar mobile | Compatible — el actual tiene `.vm-mobile-summary-bar` |
| `#modal-pago-item` sub-modal | Compatible — mismo ID |
| `#modal-documentacion` sub-modal | Compatible — mismo ID |
| `#modal-confirmar-operacion` sub-modal | **Nuevo** — no existe en el sistema |
| `#venta-form` como form raíz | Debe verificar action y method |
| Antiforgery token | Debe existir en el form |
| CSS `<style>` inline | Extraer a `wwwroot/css/venta-modal-rework.css` |
| JS `<script>` inline | Extraer a `wwwroot/js/venta-modal-rework.js` |

---

## G. Comparación campo por campo: actual vs. nuevo

| Campo / Elemento | Actual | HTML nuevo | Estado |
|---|---|---|---|
| Modal raíz ID | `#modal-crear-venta` z-50 | `#modal-crear-venta` | ✅ Compatible |
| Header sticky | Sí, `sticky top-0 z-10` | Sí | ✅ Compatible |
| Btn cerrar modal | `#btn-cerrar-modal-crear-venta` type=button | Debe tener mismo ID | ⚠️ Verificar |
| Validación AJAX summary | `#venta-ajax-validation-summary` `#venta-ajax-error-list` | ¿Presente? | ⚠️ Verificar |
| Sticky mobile | `.vm-mobile-summary-bar` `#vm-modal-sticky-total` | sticky bar | ✅ Mejorar |
| Form | `#venta-form` action=/Venta/CreateAjax method=POST | Verificar action | ⚠️ Riesgo alto |
| Antiforgery | `@Html.AntiForgeryToken()` | ¿Presente? | ⚠️ Obligatorio |
| Hidden Estado | `name="Estado"` value=EstadoVenta.Presupuesto | ¿Presente? | ⚠️ Obligatorio |
| Búsqueda cliente | `#input-buscar-cliente` `#dropdown-clientes` `#hdn-cliente-id` | ¿Compatibles? | ⚠️ Verificar IDs |
| Panel cliente info | `#info-cliente` `#info-cliente-nombre` `#info-cliente-doc` `#btn-limpiar-cliente` | Equivalente | ⚠️ Verificar IDs |
| FechaVenta | `name="FechaVenta"` `id="FechaVenta"` | ¿Presente? | ⚠️ Verificar |
| Tipo pago principal | `name="TipoPago"` `id="select-tipo-pago"` — ViewBag | ¿Options hardcoded? | 🔴 Riesgo: no copiar hardcoded |
| Panel aviso crédito | `#panel-aviso-credito` y 3 IDs de valores | ¿Presente? | ⚠️ Verificar |
| Búsqueda producto | `#input-buscar-producto` `#dropdown-productos` | ¿Compatibles? | ⚠️ Verificar IDs |
| Panel agregar producto | `#panel-agregar-producto` + 7 hidden inputs | ¿Presente? | 🔴 Crítico |
| Hidden producto IDs | 5 hidden inputs de producto | ¿Presente? | 🔴 Crítico |
| Trazabilidad / selector unidad | `#panel-selector-unidad` `#select-producto-unidad` etc. | ¿Presente? | 🔴 Puede faltar |
| Tabla detalles | `#tbody-detalles` `#detalles-vacio` | ¿Equivalente? | ⚠️ Verificar |
| Badge count | `#detalle-items-badge` | ¿Presente? | ⚠️ Verificar |
| Scroll affordance | `data-oc-scroll` attrs | ¿Presente? | ⚠️ Verificar |
| Panel tarjeta | `#panel-tarjeta` + IDs tarjeta | ¿Con IDs exactos? | 🔴 Crítico |
| Hidden tarjeta nombre/tipo | `id="hdn-tarjeta-nombre"` `id="hdn-tarjeta-tipo"` | ¿Presente? | 🔴 Test lo verifica |
| Panel cheque | `#panel-cheque` + names DatosCheque.* | ¿Presente? | 🔴 Crítico |
| Panel crédito | `#panel-credito-personal` | ¿Presente? | ⚠️ Verificar |
| Panel planes pago | `#panel-planes-pago` `#lista-planes-pago` | ¿Presente? | ⚠️ Verificar |
| Panel diagnóstico | `#panel-diagnostico-condiciones-pago` + 4 IDs | ¿Presente? | ⚠️ Verificar — test lo verifica |
| Verificación crediticia | `#panel-verificacion-crediticia` + 15+ IDs | ¿Presente? | 🔴 Crítico |
| Excepción crediticia | `#panel-excepcion-crediticia` condicional @if | ¿Presente? | ⚠️ Requiere Razor |
| VendedorUserId | `name="VendedorUserId"` condicional | ¿Presente? | ⚠️ Requiere Razor |
| Observaciones | `name="Observaciones"` `id="Observaciones"` | ¿Presente? | ⚠️ Verificar |
| Total final | `#total-final` | ¿Presente? | 🔴 MutationObserver depende de esto |
| 4 hidden totales | hdn-subtotal, hdn-descuento, hdn-iva, hdn-total | ¿Presente? | 🔴 Crítico |
| Btn confirmar | `#btn-confirmar` type=button onclick=VentaCrearModal.submit() | type=button ✅ | ✅ Compatible |
| Modal sticky total | `#vm-modal-sticky-total` | ¿Presente? | ⚠️ Requerido por inline script |
| Modal pago item | `#modal-pago-item` z-70 + IDs internos | ¿Con IDs exactos? | ⚠️ Verificar |
| Modal documentación | `#modal-documentacion` data-venta-modal attr + IDs | ¿Con attrs exactos? | ⚠️ Verificar |
| Modal confirmar operación | No existe | **Nuevo** | 🆕 Nueva integración |
| `#detalles-hidden-inputs` | div hidden para POST | ¿Presente? | 🔴 Crítico |

---

## H. Contratos críticos a preservar

### H.1 Contratos HTML — IDs que JS usa directamente

Todo ID en esta lista es referenciado por `venta-create.js` mediante `$('#id')`. Si cambia o desaparece, el JS falla silenciosamente o con error.

```
#input-buscar-cliente
#dropdown-clientes
#hdn-cliente-id
#info-cliente
#info-cliente-nombre
#info-cliente-doc
#btn-limpiar-cliente
#input-buscar-producto
#dropdown-productos
#panel-agregar-producto
#txt-producto-seleccionado
#hdn-producto-id
#hdn-producto-codigo
#hdn-producto-precio
#hdn-producto-stock
#hdn-producto-requiere-numero-serie
#txt-cantidad
#stock-error
#advertencia-stock-sin-identificar
#txt-descuento-item
#btn-agregar-producto
#panel-selector-unidad
#select-producto-unidad
#producto-unidad-error
#aviso-sin-unidades
#link-gestionar-unidades
#tbody-detalles
#detalles-vacio
#detalle-items-badge
#detalles-hidden-inputs
#select-tipo-pago
#panel-tarjeta
#panel-cheque
#panel-credito-personal
#panel-verificacion-crediticia
#select-tarjeta
#select-cuotas-tarjeta
#panel-tarjeta-resumen
#panel-aviso-cuotas-sin-interes
#hdn-tarjeta-nombre
#hdn-tarjeta-tipo
#hdn-configuracion-pago-plan-id
#configuracion-pagos-global-estado
#panel-planes-pago
#lista-planes-pago
#panel-aviso-credito
#credito-disponible
#credito-solicitado
#credito-margen
#panel-diagnostico-condiciones-pago
#diagnostico-condiciones-pago-icon
#diagnostico-condiciones-pago-estado
#diagnostico-condiciones-pago-resumen
#diagnostico-condiciones-pago-bloqueo
#diagnostico-condiciones-pago-detalle
#btn-confirmar
#filtro-categoria
#filtro-marca
#filtro-precio-min
#filtro-precio-max
#filtro-solo-stock
#venta-create-feedback-slot
#venta-ajax-validation-summary
#venta-ajax-error-list
#btn-verificar-elegibilidad
#panel-resultado-verificacion
#verificacion-badge
#verificacion-estado
#verificacion-limite
#verificacion-utilizado
#verificacion-saldo
#verificacion-barra
#panel-cupo-suficiente
#panel-cupo-insuficiente
#cupo-insuficiente-detalle
#panel-motivos
#lista-motivos
#panel-alerta-mora
#alerta-mora-texto
#panel-documentacion-faltante
#lista-docs-faltantes
#btn-cargar-documentacion
#hdn-aplicar-excepcion
#panel-excepcion-inactiva
#btn-aplicar-excepcion
#panel-excepcion-activa
#btn-confirmar-excepcion
#btn-cancelar-excepcion
#txt-excepcion-documental
#total-subtotal
#total-descuento-label
#total-descuento
#total-iva
#total-final
#hdn-subtotal
#hdn-descuento
#hdn-iva
#hdn-total
#vm-modal-sticky-total
```

### H.2 Contratos name POST — campos del formulario

Estos `name` definen el payload que llega al backend. **No cambiar sin coordinación con el controller.**

```
ClienteId
TipoPago
FechaVenta
Estado
Subtotal
Descuento
IVA
Total
VendedorUserId
Observaciones
MotivoExcepcionDocumentalCreate
AplicarExcepcionDocumental
DatosTarjeta.ConfiguracionTarjetaId
DatosTarjeta.NombreTarjeta
DatosTarjeta.TipoTarjeta
DatosTarjeta.CantidadCuotas
DatosTarjeta.NumeroAutorizacion
DatosTarjeta.ConfiguracionPagoPlanId
DatosCheque.NumeroCheque
DatosCheque.Banco
DatosCheque.Titular
DatosCheque.CUIT
DatosCheque.FechaEmision
DatosCheque.FechaVencimiento
DatosCheque.Monto
Detalles[i].ProductoId
Detalles[i].Cantidad
Detalles[i].PrecioUnitario
Detalles[i].Descuento
Detalles[i].Subtotal
Detalles[i].ProductoUnidadId
```

### H.3 Contratos de data-attributes

```
data-venta-modal-action="open"     — btn-cargar-documentacion abre sub-modal
data-venta-modal-action="close"    — overlay y btn-cerrar cierran sub-modal
data-venta-modal-target="documentacion"
data-venta-modal="documentacion"   — en #modal-documentacion
data-oc-scroll                     — en #venta-detalles-scroll
data-oc-scroll-shell
data-oc-scroll-fade="left"/"right"
data-oc-scroll-region
data-oc-scroll-table
data-oc-scroll-hint
data-index                         — en .btn-eliminar-detalle (generado por JS)
data-plan-id                       — en .plan-pago-btn (generado por JS)
data-configuracion-pago-id         — en options de select-tipo-pago
```

### H.4 Contratos de clases CSS funcionales

Estas clases son hooks JS o CSS que tienen lógica:

```
.btn-eliminar-detalle       — listener delegado en #tbody-detalles
.btn-cerrar-pago-item       — cierra #modal-pago-item
.plan-pago-btn              — selector de plan, gestionado por renderSelectorPlanesPago()
.toast-msg                  — auto-dismiss por VentaModule.initSharedUi()
.alert-erp                  — clase base del sistema de alertas
.alert-erp-info .alert-erp-warning .alert-erp-error
.vm-mobile-summary-bar      — barra sticky mobile (CSS en venta-module.css)
.vm-btn-confirm-sm          — botón confirmar en la barra mobile
.vm-modal-panel             — borde del panel modal (CSS custom, no Tailwind)
.vm-modal-header-sep        — borde inferior del header
.vm-section                 — card de sección
.venta-create-doc-footer    — responsive footer del sub-modal doc
.venta-create-doc-action    — acción en el footer doc
```

### H.5 Contratos JS: API pública de VentaCrearModal

Funciones que otros scripts o botones inline pueden llamar:

```javascript
VentaCrearModal.submit()   // llamado desde #btn-confirmar y .vm-btn-confirm-sm
VentaCrearModal.open()     // abre el modal (si existe como API pública)
VentaCrearModal.close()    // cierra el modal (si existe como API pública)
```

> **Nota:** La función `submit()` es la más crítica. Está hardcoded como `onclick="VentaCrearModal.submit()"` tanto en `#btn-confirmar` como en la sticky bar mobile. Cualquier renombramiento rompe la confirmación.

### H.6 Contratos de endpoints

```
POST /Venta/CreateAjax       — action del #venta-form
GET  /api/ventas/BuscarClientes?term=...
GET  /api/ventas/BuscarProductos?...
GET  /api/ventas/GetTarjetasActivas
GET  /api/ventas/configuracion-pagos-global
GET  /api/productos/{id}/unidades-disponibles
POST /api/ventas/CalcularTotalesVenta
GET  /api/ventas/VerificarElegibilidadCredito (llamado por btn-verificar-elegibilidad)
POST /api/clientes/{id}/documentos (llamado por btn-subir-documento)
```

### H.7 Contratos verificados por tests (VentaCreateUiContractTests.cs)

Los siguientes asserts deben seguir pasando tras el rework:

| Test | Assert clave |
|---|---|
| `CreateView_PosteaCamposSnapshotDeTarjeta` | `name="DatosTarjeta.ConfiguracionTarjetaId"`, `id="hdn-tarjeta-nombre"`, `id="hdn-tarjeta-tipo"` en Create_tw |
| `CreateView_TieneSelectorTipoPagoGeneralVisible` | `id="select-tipo-pago"`, label `Tipo de pago principal`, no oculto |
| `VentaCrearModal_MuestraTipoPagoPrincipalVisible` | `id="select-tipo-pago"` en modal, no hidden |
| `VentaCrearModal_AclaraPagoPrincipalYAjustePorProducto` | Texto exacto sobre tipo pago principal |
| `CreateView_NoMuestraAccionPagoPorItemEnTabla` | DoesNotContain: `btn-configurar-pago-item`, `modal-pago-item` en **Create_tw** |
| `CreateView_MantienePanelesDePagoGeneral` | `panel-tarjeta`, `panel-cheque`, `panel-credito-personal`, `panel-planes-pago`, `configuracion-pagos-global-estado` |
| `CreateView_TieneSoporteSelectorProductoUnidad` | `hdn-producto-requiere-numero-serie`, `panel-selector-unidad`, `select-producto-unidad` |
| `CreateView_PanelDiagnosticoCondicionesPagoExisteOcultoEnNuevaVenta` | `panel-diagnostico-condiciones-pago`, `diagnostico-condiciones-pago-bloqueo` |
| `VentaCreateJs_PueblaSnapshotDeTarjetaDesdeTarjetaActiva` | `ventaForm.requestSubmit()` existe en JS |
| `VentaCreateJs_MantienePreviewRecargoDebitoComoMontoYPorcentaje` | Funciones de recargo en JS |

> **Importante:** Los tests de `_VentaCrearModal` (`VentaCrearModal_*`) auditan el modal — el rework debe mantener todos sus asserts.  
> Los tests de `CreateView_*` auditan `Create_tw.cshtml` — el rework del modal no los rompe directamente, pero hay IDs compartidos que ambos necesitan.

---

## I. Funciones JS actuales (venta-create.js)

### I.1 Módulo global: estado

```javascript
const detalles = []                     // array de líneas del detalle
let clienteSeleccionado = null
let tarjetaInfoCache = []
let creditoCupoDisponible = null
let configuracionPagosGlobal = null
let configuracionPagosGlobalDisponible = false
const condicionesProductoCache = new Map()
```

### I.2 Funciones críticas (no duplicar en el nuevo JS)

| Función | Rol |
|---|---|
| `cargarConfiguracionPagosGlobal()` | Carga medios de pago desde API, popula select-tipo-pago |
| `onTipoPagoChange()` | Muestra/oculta paneles según tipo de pago |
| `cargarTarjetasActivas()` | Carga tarjetas disponibles |
| `recalcularTotales()` | POST a /api/ventas/CalcularTotalesVenta |
| `actualizarTotalesUI()` | Actualiza todos los spans y hidden inputs de totales |
| `renderDetalles()` | Renderiza #tbody-detalles y #detalles-hidden-inputs |
| `renderSelectorPlanesPago()` | Renderiza botones de plan en #lista-planes-pago |
| `invalidarVerificacionCrediticia()` | Resetea estado de verificación |
| `actualizarBloqueoContinuidadCondicionesPago()` | Habilita/deshabilita #btn-confirmar |
| `actualizarResumenOperacion()` | Actualiza hero metrics (Create) o simplemente el total |
| `VentaCrearModal.submit()` | Submit principal — llama ventaForm.requestSubmit() |

### I.3 Enums JS (deben coincidir con Models/Enums/)

```javascript
const TIPO_PAGO = {
    Efectivo: '0', Transferencia: '1', TarjetaDebito: '2', TarjetaCredito: '3',
    Cheque: '4', CreditoPersonal: '5', MercadoPago: '6', CuentaCorriente: '7', Tarjeta: '8'
}
```

### I.4 Patrones de seguridad existentes

- `esc()` — escapa HTML antes de insertar con `innerHTML` en dropdowns y tbody
- `textContent` para valores de usuario en spans y celdas
- `fetchJson()` / `postJson()` — fetch helpers con token antiforgery
- **NO usar innerHTML** sin `esc()` para datos del servidor

---

## J. Funciones JS nuevas propuestas (a extraer a venta-modal-rework.js)

> Solo se crean si el HTML nuevo las requiere. No duplicar funciones existentes en venta-create.js.

| Función propuesta | Rol | Condición |
|---|---|---|
| `initModalSteps()` | Gestión de wizard de pasos si el nuevo diseño lo usa | Solo si hay tabs/steps reales en el HTML nuevo |
| `openSubmodal(id)` | Abre sub-modales genéricamente | Si el nuevo HTML tiene `#modal-confirmar-operacion` |
| `closeSubmodal(id)` | Cierra sub-modales | Igual que openSubmodal |
| `syncStickyTotal()` | Espejo de #total-final → barra sticky | Ya existe como inline script — mover aquí |
| Controlador de wizard | Navegación entre pasos | Si se implementan pasos reales |

**Regla:** Si una función ya existe en `venta-create.js`, **no duplicarla** en el nuevo archivo. El nuevo JS debe coordinarse o extender, no reemplazar.

---

## K. CSS actual

### K.1 Clases canónicas del modal (venta-module.css)

```
.vm-label               — label de campo con ícono
.vm-required            — asterisco requerido
.vm-input / .vm-textarea — inputs del modal
.vm-select              — selects del modal
.vm-search-wrap / .vm-search-icon — búsqueda con ícono
.vm-modal-panel         — panel exterior del modal
.vm-modal-header-sep    — separador del header
.vm-section             — card de sección interior
.vm-step                — badge de paso (1, 2, 3...)
.vm-metric              — chip de métrica
.vm-dropdown            — dropdown de resultados
.vm-btn-ghost           — botón secundario
.vm-btn-add             — botón Añadir producto
.vm-btn-confirm         — botón Confirmar Transacción principal
.vm-btn-confirm-sm      — botón confirmar en sticky mobile
.vm-btn-verify          — botón Verificar Elegibilidad
.vm-totals              — contenedor de totales
.vm-sub-panel           — panel interior (tarjeta, cheque, crédito)
.vm-error-summary       — resumen de errores AJAX
.vm-mobile-summary-bar  — barra sticky mobile
.vm-mobile-summary-bar__info
.vm-preconfirm-reminder — recordatorio pre-confirmación
.vm-panel-producto      — panel de agregar producto
.vm-checkbox-label      — label con checkbox
```

### K.2 Clases de Create_tw (para referencia)

```
.venta-section / .venta-label / .venta-input / .venta-select
.venta-sub-panel / .venta-stat / .venta-stat__label / .venta-stat__value
.venta-hero-metrics / .venta-hero-metric
.venta-scroll-medium
.venta-create-doc-footer / .venta-create-doc-action
.hero-erp                — sección hero (Create_tw)
.sticky-action-footer    — footer sticky mobile (Create_tw, en shared-components.css)
```

---

## L. CSS nuevo a extraer

El CSS inline del HTML nuevo debe extraerse completamente a:

```
wwwroot/css/venta-modal-rework.css
```

**Reglas de extracción:**

1. No copiar estilos que ya existen en `venta-module.css` — reusar clases `vm-*`.
2. No copiar estilos que conflictúen con Tailwind CDN ya cargado.
3. No incluir `@tailwind` directives — el CDN ya está disponible.
4. Nombrar nuevas clases con prefijo `vmr-` (venta-modal-rework) para evitar colisiones.
5. Mantener la paleta ERP canónica: `#161c28` bg, `#1e293b` border, `#135bec` primary.
6. Si el HTML nuevo usa variables CSS, centralizar en `:root` en el nuevo CSS.

**Clases que probablemente hay que crear:**

- `.vmr-step-nav` — navegación de wizard si existe
- `.vmr-step-panel` — panel de contenido de cada paso
- `.vmr-sticky-summary` — resumen sticky lateral si difiere del actual
- `.vmr-confirm-modal` — para el nuevo #modal-confirmar-operacion si aplica

---

## M. Riesgos de integración

### M.1 CRÍTICO — form action y submit

**Riesgo:** El HTML nuevo puede traer el `<form>` con `action` incorrecto o sin él.  
**Impacto:** Los datos no llegarían al controller. La venta no se crearía.  
**Mitigación:** El form debe tener `action="/Venta/CreateAjax"` y el submit debe ser `VentaCrearModal.submit()` → `ventaForm.requestSubmit()`.

### M.2 CRÍTICO — Antiforgery

**Riesgo:** Si se copia el form sin `@Html.AntiForgeryToken()`, todas las operaciones POST fallan con 400.  
**Impacto:** El sistema rechaza toda transacción.  
**Mitigación:** `@Html.AntiForgeryToken()` debe estar dentro del `#venta-form`, siempre.

### M.3 CRÍTICO — Duplicación de funciones JS globales

**Riesgo:** El HTML nuevo trae JS inline con funciones globales que pueden colisionar con `venta-create.js`.  
**Impacto:** Comportamiento imprevisible — la función que carga último gana.  
**Mitigación:** Extraer todo JS inline al nuevo archivo. No redefinir funciones ya existentes.

### M.4 ALTO — Options hardcoded en selects

**Riesgo:** El HTML nuevo puede traer `#select-tipo-pago` con options hardcoded (Efectivo, Transferencia, etc.).  
**Impacto:** Si se usa esto en lugar del `@foreach (tiposPago)`, los medios de pago quedan estáticos y no respetan la configuración del ERP.  
**Mitigación:** Siempre usar `@if (tiposPago != null) { foreach ... }` desde ViewBag.

**Mismo riesgo en `#select-tipo-pago-item`** — el modal actual ya tiene options hardcoded. Se mantiene solo porque es el sub-modal de condiciones por producto, no el pago principal. Pero si el nuevo HTML trae un select principal hardcoded, debe reemplazarse.

### M.5 ALTO — IDs que cambian o se pierden

**Riesgo:** El HTML nuevo puede renombrar IDs o introducir nuevas estructuras que el JS actual no encuentra.  
**Impacto:** Funciones JS fallan silenciosamente — el operador no ve errores pero la lógica no ejecuta.  
**Mitigación:** Antes de integrar cada sección, verificar que todos los IDs en la lista H.1 existen con exactamente el mismo nombre.

### M.6 ALTO — Sub-modal #modal-confirmar-operacion

**Riesgo:** Es un elemento nuevo. Requiere JS para abrirlo, cerrarlo y conectarlo con VentaCrearModal.submit().  
**Impacto:** Si no se conecta correctamente, el operador no puede confirmar la venta.  
**Decisión requerida:** ¿Este modal reemplaza o complementa el flujo de confirmación actual?

### M.7 MEDIO — Accesibilidad y aria

**Riesgo:** El HTML nuevo puede perder `role="dialog"`, `aria-modal="true"`, `aria-label` o `role="alert"` en paneles críticos.  
**Impacto:** Tests de accesibilidad fallan. Usuarios con lectores de pantalla no reciben anuncios.  
**Mitigación:** Revisar sección por sección los atributos aria antes de integrar.

### M.8 MEDIO — Panel de trazabilidad individual

**Riesgo:** Si el nuevo diseño no incluye `#panel-selector-unidad` y el flujo de selección de unidad física, los productos con `requiereNumeroSerie=true` no podrán venderse correctamente.  
**Impacto:** Venta de productos trazables queda rota.  
**Mitigación:** Verificar en el HTML nuevo o agregar explícitamente en la fase 1A.

### M.9 MEDIO — Panel diagnóstico condiciones de pago

**Riesgo:** El HTML nuevo puede no incluir `#panel-diagnostico-condiciones-pago` y sus 4 IDs internos.  
**Impacto:** El JS en `venta-create.js` intenta escribir en estos IDs. Si no existen, falla silenciosamente pero no bloquea el flujo.  
**Nota:** El test `CreateView_PanelDiagnosticoCondicionesPagoExisteOcultoEnNuevaVenta` lo verifica solo para `Create_tw.cshtml`, no para el modal.

### M.10 BAJO — Tailwind CDN y clases inline

**Riesgo:** Si el HTML nuevo usa clases Tailwind que no existen en el CDN configurado, el diseño difiere del esperado.  
**Mitigación:** Verificar la versión del CDN de Tailwind usada globalmente en el proyecto.

### M.11 BAJO — Google Fonts y Material Symbols

**Riesgo:** El HTML nuevo puede incluir `<link>` adicionales que ya están en el layout global.  
**Impacto:** Doble carga de recursos, posible FOUC.  
**Mitigación:** No copiar los `<link>` del HTML nuevo — verificar si ya están en `_Layout.cshtml`.

---

## N. Qué NO se debe copiar directo del HTML nuevo

```
<!DOCTYPE html>
<html>
<head>
<body>
<link rel="stylesheet" href="https://cdn.tailwindcss.com">
<link href="https://fonts.googleapis.com/...">
<link href="https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined...">
<style> ... </style>       ← extraer a wwwroot/css/venta-modal-rework.css
<script> ... </script>     ← extraer a wwwroot/js/venta-modal-rework.js
Opciones hardcoded de tipo de pago en <select name="TipoPago">
Datos mock de cliente o producto (nombres, precios fijos)
Comentarios como "<!-- Razor: @if ... -->" sin convertir a Razor real
```

---

## O. Qué SÍ se puede reutilizar del HTML nuevo

Una vez provisto el HTML, evaluar reutilización de:

- Estructura visual del header y layout general
- Diseño visual de los paneles de paso (si son mejores que el actual)
- Nueva barra sticky de resumen si supera la actual
- Diseño del `#modal-confirmar-operacion` — requiere nueva integración JS
- Variables CSS y tokens de color si están bien definidos
- Mejoras de accesibilidad: `aria-live`, `aria-expanded`, `aria-current`
- Iconografía de Material Symbols si agrega claridad operativa
- Composición visual mobile del sticky footer

---

## P. Plan de separación Razor / CSS / JS

### P.1 Razor (_VentaCrearModal.cshtml)

Responsabilidades:

- Estructura HTML del modal y sub-modales
- Directivas Razor: `@using`, `@{...}`, `@(expr)`, `@if`, `@foreach`
- ViewBag: `TiposPago`, `Tarjetas`, `CategoriasFiltro`, `MarcasFiltro`, `Vendedores`
- `@Html.AntiForgeryToken()`
- `asp-for`, `asp-items`, `asp-action` (si aplica)
- IDs, names, data-attrs de contratos
- Clases CSS referenciadas (vm-*, Tailwind inline)
- **No** lógica JS inline (salvo el inline script del MutationObserver del sticky total que puede moverse a JS)

### P.2 CSS (wwwroot/css/venta-modal-rework.css)

Responsabilidades:

- Todo CSS inline del HTML nuevo
- Nuevas clases `vmr-*` para elementos nuevos
- Overrides específicos del rework que no existen en venta-module.css
- Animaciones y transiciones nuevas
- No duplicar clases ya en venta-module.css

Cargado en la vista a través de:

```cshtml
@section Styles {
    <partial name="_VentaModuleStyles" />
    <link rel="stylesheet" href="~/css/venta-modal-rework.css" asp-append-version="true" />
}
```

o en el partial `_VentaModuleStyles` si aplica.

### P.3 JS (wwwroot/js/venta-modal-rework.js)

Responsabilidades:

- Todo JS inline del HTML nuevo
- Gestión de wizard de pasos (si aplica)
- Gestión del nuevo `#modal-confirmar-operacion`
- No duplicar funciones de venta-create.js
- Puede exportar funciones al namespace `VentaCrearModal` o a un objeto nuevo `VentaModalRework`
- Debe cargarse **después** de venta-create.js

Cargado en:

```cshtml
@section Scripts {
    <script src="~/js/horizontal-scroll-affordance.js" asp-append-version="true"></script>
    <script src="~/js/venta-module.js" asp-append-version="true"></script>
    <script src="~/js/venta-create.js" asp-append-version="true"></script>
    <script src="~/js/venta-modal-rework.js" asp-append-version="true"></script>
}
```

---

## Q. Roadmap por fases

### KIRA-VENTAS-MODAL-REWORK-1A — Skeleton Razor del nuevo modal

**Alcance:**
- Reemplazar la estructura visual del `_VentaCrearModal.cshtml` con el layout del HTML nuevo
- Mantener TODOS los IDs, names, data-attrs y contratos existentes
- No cambiar ninguna clase funcional
- Agregar Razor donde el HTML tiene valores mock o hardcoded
- No tocar JS ni CSS todavía

**Validar:** build + VentaCreateUiContractTests (60 tests)

### KIRA-VENTAS-MODAL-REWORK-1B — CSS separado del nuevo modal

**Alcance:**
- Crear `wwwroot/css/venta-modal-rework.css` con CSS extraído del HTML nuevo
- Eliminar `<style>` inline si lo hubiera en el skeleton
- Verificar que no colisiona con venta-module.css
- No tocar JS ni Razor productivo

**Validar:** build + inspección visual

### KIRA-VENTAS-MODAL-REWORK-1C — JS separado para pasos y sub-modales

**Alcance:**
- Crear `wwwroot/js/venta-modal-rework.js`
- Extraer JS inline del HTML nuevo
- Conectar el wizard de pasos si aplica (sin reemplazar funciones de venta-create.js)
- Gestión básica de `#modal-confirmar-operacion` si es necesaria
- Eliminar `<script>` inline del skeleton

**Validar:** build + tests JS en VentaCreateUiContractTests + Playwright visual básico

### KIRA-VENTAS-MODAL-REWORK-1D — Integración cliente / producto / totales

**Alcance:**
- Verificar que autocomplete de cliente y producto funcionan con el nuevo layout
- Verificar renderDetalles() con el nuevo tbody
- Verificar actualizarTotalesUI() con los nuevos spans de totales
- Verificar sticky total mobile

**Validar:** Playwright flujo de agregar cliente + productos + ver total

### KIRA-VENTAS-MODAL-REWORK-1E — Pago principal y pago por producto

**Alcance:**
- Verificar onTipoPagoChange() con el nuevo layout de paneles
- Verificar panel-tarjeta, panel-cheque, panel-credito-personal
- Verificar sub-modal #modal-pago-item
- Verificar selector de planes de pago

**Validar:** Playwright flujo de tipo de pago (tarjeta, crédito, efectivo)

### KIRA-VENTAS-MODAL-REWORK-1F — Crédito / documentación / excepción

**Alcance:**
- Verificar panel-verificacion-crediticia
- Verificar btn-verificar-elegibilidad
- Verificar panel-alerta-mora, panel-cupo-insuficiente, panel-documentacion-faltante
- Verificar sub-modal #modal-documentacion
- Verificar flujo de excepción crediticia (panel-excepcion-crediticia)

**Validar:** Playwright verificación crediticia + documentación

### KIRA-VENTAS-MODAL-REWORK-1G — Confirmación final / CreateAjax / submit

**Alcance:**
- Verificar VentaCrearModal.submit() → ventaForm.requestSubmit()
- Verificar action=/Venta/CreateAjax
- Verificar antiforgery
- Integrar #modal-confirmar-operacion si corresponde
- Verificar respuesta de CreateAjax (redirect o error)

**Validar:** Playwright flujo completo de venta exitosa + venta con error

### KIRA-VENTAS-MODAL-REWORK-QA — Playwright desktop / mobile / regresión

**Alcance:**
- Regresión completa de VentaCreateUiContractTests (60 tests)
- Regresión de specs E2E relevantes
- Prueba visual desktop y mobile
- Prueba de accesibilidad (tab order, aria-live, role=alert)
- Prueba de venta exitosa end-to-end

**Validar:** Suite completa de tests + Playwright + verificación manual

---

## R. Validaciones necesarias por fase

| Fase | Build | VentaCreateUiContractTests | Playwright visual | Playwright flujo |
|---|---|---|---|---|
| 1A Skeleton | ✅ obligatorio | ✅ obligatorio | — | — |
| 1B CSS | ✅ obligatorio | — | ✅ inspección | — |
| 1C JS | ✅ obligatorio | ✅ obligatorio | ✅ básico | — |
| 1D Cliente/Producto | ✅ | ✅ | ✅ | ✅ básico |
| 1E Pago | ✅ | ✅ | ✅ | ✅ tipo pago |
| 1F Crédito | ✅ | ✅ | ✅ | ✅ crédito |
| 1G Confirmación | ✅ | ✅ | ✅ | ✅ completo |
| QA final | ✅ | ✅ 60/60 | ✅ | ✅ completo |

---

## S. Playwright necesario

Specs existentes a correr en cada fase relevante:

```powershell
# Fase 1A+:
npx.cmd playwright test e2e/ui-4e-layout-visual.spec.js

# Fase 1D+:
npx.cmd playwright test e2e/venta-pago-por-item.spec.js

# Fase 1G+:
npx.cmd playwright test e2e/cotizacion-conversion.spec.js    # si el modal cubre cotización
```

Nuevos specs que se deben crear en QA:

- `e2e/venta-modal-rework-visual.spec.js` — snapshots del nuevo diseño
- `e2e/venta-modal-rework-flujo.spec.js` — flujo completo nueva venta con el modal rework

---

## T. Decisión final: implementar o ajustar diseño antes

**Recomendación:** Iniciar la fase 1A (skeleton Razor) cuando el HTML nuevo sea provisto.

**Condiciones para continuar:**

1. El HTML nuevo debe ser provisto antes de iniciar 1A.
2. Antes de 1A, verificar contra la lista de contratos H.1 que los IDs críticos están presentes en el HTML nuevo o se pueden agregar sin romper el diseño.
3. Si el HTML nuevo no incluye `#panel-selector-unidad` (trazabilidad individual), debe agregarse en 1A.
4. Si el `#modal-confirmar-operacion` es complejo, evaluar postergarlo a 1G o convertirlo en una fase separada 1H.
5. Confirmar con el usuario si el wizard de pasos es obligatorio en el diseño final o es una sugerencia visual.

**No bloquear en:** diferencias puramente visuales de clase CSS — esas se resuelven en 1B.

---

## U. Próximo prompt recomendado

Una vez que el usuario provea el HTML nuevo, iniciar con:

```
PROMPT — KIRA-VENTAS-MODAL-REWORK-1A — Skeleton Razor del nuevo modal

Usar la auditoría docs/kira-ventas-modal-rework-0-auditoria.md como referencia.

Base: main actualizado.

El HTML nuevo ya fue provisto: [pegar aquí el HTML].

Tarea:
1. Adaptar el HTML nuevo a _VentaCrearModal.cshtml preservando todos los contratos
   de la sección H de la auditoría.
2. Reemplazar valores mock por Razor (ViewBag, @foreach, @if, asp-for).
3. Mantener todos los IDs, names, data-attrs de la lista H.1.
4. No tocar JS ni CSS productivo todavía.
5. Validar con build + VentaCreateUiContractTests.
6. Documentar cambios aplicados y contratos preservados.
```

---

## Validaciones ejecutadas (esta fase)

```powershell
git diff --check    # Sin errores de whitespace
git status --short  # Solo nuevo archivo de documentación
```

**Build/tests/Playwright:** no ejecutados — fase doc-only, el diff confirma que solo se crea un archivo de documentación.

---

## Estado final del working tree

```
?? docs/kira-ventas-modal-rework-0-auditoria.md    ← nuevo, será commiteado

M  .claude/settings.local.json    ← LOCAL, no commitear
M  AGENTS.md                      ← LOCAL, no commitear
M  CLAUDE.md                      ← LOCAL, no commitear
M  Views/Producto/Unidades.cshtml ← LOCAL (modificación pre-existente), no commitear
M  docs/misa-catalogo-ux-1g-aria-live-modales.md ← LOCAL, no commitear
D  skills-lock.json               ← LOCAL, no commitear
```
