# MISA-INVENTARIO-FISICO-UX-2N - Tabs reales Producto/Unidades

## A. Objetivo

Rehacer la navegacion interna de `Producto/Unidades` como tabs reales para que los modos cambien de panel sin desplazar la ventana como acordeon/anclas.

Tabs esperados:
- Unidades
- Carga
- Conciliacion
- Configuracion

## B. Problema detectado por el usuario

El usuario volvio a probar visualmente y reporto que las solapas seguian funcionando mal: al hacer click, la pantalla se desplazaba como si fueran anchors sobre secciones apiladas. El efecto era especialmente confuso en Configuracion, porque el modo parecia mezclarse con otras secciones.

## C. Por que anchors no servian

La arquitectura anterior usaba links como:

```html
<a href="#modo-carga">Carga</a>
```

Eso delegaba el cambio de modo al scroll nativo del navegador. Aunque el JS de 2K pintaba el tab activo por hash, no ocultaba paneles. Resultado: los cuatro modos seguian apilados en el DOM visible y el click producia scroll, no un cambio real de panel.

## D. Estado anterior

- Nav principal con `<a href="#modo-*">`.
- Estado activo aplicado por `className` en JS segun hash.
- Paneles `#modo-unidades`, `#modo-carga`, `#modo-conciliacion`, `#modo-configuracion` visibles y apilados.
- `#modo-configuracion` ya estaba al final desde 2M, pero seguia siendo una seccion de scroll.

## E. Arquitectura nueva de tabs

- Nav principal convertido a `role="tablist"`.
- Cada tab principal es `button type="button"` con `role="tab"`, `data-tab-target`, `aria-controls` y `aria-selected`.
- Cada modo conserva su ID historico y ahora es `role="tabpanel"` con `data-tab-panel` y `aria-labelledby`.
- Paneles inactivos se ocultan con `hidden`.
- IDs preservados: `modo-unidades`, `modo-carga`, `modo-conciliacion`, `modo-configuracion`, `listado-unidades`, `form-carga-masiva-unidades`, `ajuste-asistido`.

## F. Cambios aplicados

- Reemplazados los anchors del nav principal por botones.
- Convertidos los wrappers de modo a paneles ARIA.
- `modo-carga`, `modo-conciliacion` y `modo-configuracion` renderizan inicialmente con `hidden`.
- Agregado mapeo de hashes secundarios a su panel propietario:
  - `#listado-unidades` -> `modo-unidades`
  - `#form-carga-masiva-unidades` -> `modo-carga`
  - `#ajuste-asistido` -> `modo-conciliacion`
- Agregado contrato de test para evitar regresar a anchors en el nav principal.
- Ajustado el dialog de acciones con `style="margin: auto;"` para mantener centrado real sin tocar CSS global.

## G. JS minimo agregado

El JS inline en `@section Scripts`:

- Se ejecuta al cargar.
- Lee el hash inicial.
- Sin hash activa `modo-unidades`.
- Con `#modo-carga`, `#modo-conciliacion` o `#modo-configuracion` muestra solo ese panel.
- Al hacer click en un tab, cambia panel, actualiza `aria-selected`, clases activo/inactivo y URL con `history.replaceState`.
- Escucha `hashchange`.
- Intercepta anchors internos conocidos para abrir el panel correcto sin scroll brusco.
- Agrega navegacion simple con ArrowLeft / ArrowRight.
- No crea variables globales.
- No interfiere con formularios POST ni dialogs.

## H. Accesibilidad aplicada

- `role="tablist"` en la navegacion principal.
- `role="tab"` en cada boton.
- `role="tabpanel"` en cada panel.
- `aria-controls`, `aria-selected`, `aria-labelledby`.
- `hidden` en paneles inactivos.
- `type="button"` en tabs para evitar submit accidental.
- `tabindex` dinamico para tab activo/inactivo.

## I. Resultado /Producto/Unidades/21

Playwright desktop y mobile:

```text
/Producto/Unidades/21: selected=modo-unidades visible=modo-unidades scrollY=0
```

Resultado: abre Unidades, solo hay un panel visible.

## J. Resultado /Producto/Unidades/21#modo-configuracion

Playwright desktop y mobile:

```text
/Producto/Unidades/21#modo-configuracion: selected=modo-configuracion visible=modo-configuracion scrollY=0
```

Resultado: abre Configuracion limpia, sin Conciliacion debajo y sin secciones mezcladas.

## K. Resultado desktop

Viewport: `1440x900`.

```text
desktop /Producto/Unidades/21: selected=modo-unidades visible=modo-unidades scrollY=0
desktop /Producto/Unidades/21#modo-configuracion: selected=modo-configuracion visible=modo-configuracion scrollY=0
desktop /Producto/Unidades/21#modo-carga: selected=modo-carga visible=modo-carga scrollY=0
desktop /Producto/Unidades/21#modo-conciliacion: selected=modo-conciliacion visible=modo-conciliacion scrollY=0
desktop tab clicks: no accordion scroll, final=modo-configuracion, scrollY=0
desktop listado responsive: cards=none table=block
desktop dialog: open=true insideTable=false centerDelta=0/0
```

## L. Resultado mobile

Viewport: `390x844`.

```text
mobile /Producto/Unidades/21: selected=modo-unidades visible=modo-unidades scrollY=0
mobile /Producto/Unidades/21#modo-configuracion: selected=modo-configuracion visible=modo-configuracion scrollY=0
mobile /Producto/Unidades/21#modo-carga: selected=modo-carga visible=modo-carga scrollY=0
mobile /Producto/Unidades/21#modo-conciliacion: selected=modo-conciliacion visible=modo-conciliacion scrollY=0
mobile tab clicks: no accordion scroll, final=modo-configuracion, scrollY=0
mobile listado responsive: cards=block table=none
mobile dialog: open=true insideTable=false centerDelta=0/0
```

## M. Contratos preservados

- Formularios POST existentes.
- `asp-action`, `asp-controller`, `asp-route`.
- `name`, `id`, `required`.
- `@Html.AntiForgeryToken()`.
- `ProductoUnidadId`, `Motivo`, `EstadoDestino`, `ProductoId`.
- `CargaMasiva.Confirmar=false`.
- `CargaMasiva.Confirmar=true`.
- Partial `_EstadoUnidadBadge`.
- Dialogs fuera de tabla/overflow.
- Cards mobile de 2J.
- Tabla desktop.
- Carga masiva.
- Conciliacion.
- Configuracion.
- Historial y endpoints.

## N. Que no se toco

- Backend.
- Controllers.
- Services.
- Models.
- ViewModels.
- Migrations.
- Endpoints.
- Payloads.
- Permisos.
- Reglas de stock.
- Calculos.
- CSS global.
- JS global.
- Playwright specs.
- Ventas/Kira, Catalogo, Movimientos, AlertaStock, Cotizacion.

## O. Riesgo funcional

Medio controlado. El cambio afecta la arquitectura visible de navegacion, pero no modifica reglas de negocio ni formularios. El riesgo principal era dejar anchors internos apuntando a paneles ocultos; se mitigo con `panelByHash` para hashes secundarios conocidos.

## P. Validaciones ejecutadas

```powershell
dotnet build --configuration Release
dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests"
node inline con Playwright sobre http://localhost:5187/Producto/Unidades/21
git diff --check
```

## Q. Resultado build

```text
Primer build Release: Compilacion correcta, 0 advertencias, 0 errores.
Revalidacion final: el build normal encontro file-lock en obj/Release/net8.0/rpswa.dswa.cache.json.
Reintento permitido:
dotnet build --configuration Release -o tmpbuild_misa_inventario_fisico_ux_2n
Compilacion correcta.
1 Advertencia(s): NETSDK1194 por usar --output a nivel solucion.
0 Errores
Tiempo transcurrido 00:02:29.47
```

## R. Resultado tests

```text
Correctas! - Con error: 0, Superado: 80, Omitido: 0, Total: 80, Duracion: 14 s
```

## S. Resultado Playwright/manual

Playwright inline ejecutado contra la app disponible en `localhost:5187`.

Resultado:
- Desktop 1440x900 OK.
- Mobile 390x844 OK.
- Las URLs con hash abren el panel correcto.
- Clicks en tabs no generan scroll tipo acordeon.
- Paneles no se mezclan.
- Tabla desktop visible solo en desktop.
- Cards mobile visibles solo en mobile.
- Dialog abre fuera de tabla y centrado.

## T. Deudas restantes

- No se creo spec Playwright permanente para Producto/Unidades; la validacion fue inline para no modificar specs fuera de alcance.
- Algunos enlaces internos siguen usando hash por contrato historico, pero ahora el JS los mapea al panel correcto.
- Si en el futuro se agregan nuevos anchors internos dentro de paneles ocultos, deberan sumarse a `panelByHash`.

## U. Proximo frente recomendado

Crear una spec Playwright acotada de contrato visual para `Producto/Unidades` que valide tabs reales, panel unico visible, hash inicial y responsive tabla/cards, sin ejecutar la suite completa.
