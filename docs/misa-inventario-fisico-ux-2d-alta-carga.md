# MISA-INVENTARIO-FISICO-UX-2D - Alta y carga dedicada

## A. Objetivo

Ordenar el modo Carga de `Views/Producto/Unidades.cshtml` para separar visualmente alta individual y carga masiva, sin cambiar backend, endpoints, payloads, reglas de negocio ni JavaScript productivo.

## B. Base y contexto

- Base indicada: `main` en `2d6bcab` - MISA-INVENTARIO-FISICO-UX-2C integrada.
- Rama de trabajo: `misa/inventario-fisico-ux-2d-alta-carga`.
- Tipo de fase: Razor-only / UX visual / reorganizacion de carga.
- 2B ya separo la pantalla por modos internos.
- 2C movio acciones sensibles por unidad a `<dialog>` nativo.

## C. Deuda tomada desde 2A/2B

2A identifico que alta individual y carga masiva debian vivir como flujo dedicado, preservando preview y confirmacion. 2B creo el modo Carga, pero dentro de ese modo todavia convivian alta individual, carga masiva, previsualizacion y confirmacion con jerarquia similar.

La deuda tomada para 2D fue aclarar esos subflujos sin crear tabs nuevos, vistas nuevas, endpoints nuevos ni JS adicional.

## D. Secciones de carga detectadas

- `#modo-carga`.
- Encabezado del modo Carga.
- Alta individual de unidad.
- Carga masiva de unidades.
- Paso de previsualizacion de carga masiva.
- Confirmacion de carga masiva.
- Preview de carga masiva con tabla scrolleable.
- Link de retorno a `#listado-unidades`.

## E. Formularios detectados

- `POST Producto/CrearUnidad`
  - `CrearUnidad.ProductoId`
  - `CrearUnidad.NumeroSerie`
  - `CrearUnidad.UbicacionActual`
  - `CrearUnidad.Observaciones`
  - `asp-validation-summary="ModelOnly"`
  - `asp-validation-for` de campos de alta individual
  - `@Html.AntiForgeryToken()`

- `POST Producto/CrearUnidadesMasivas`
  - `id="form-carga-masiva-unidades"`
  - `CargaMasiva.ProductoId`
  - `CargaMasiva.CantidadSinSerie`
  - `CargaMasiva.NumerosSerieTexto`
  - `CargaMasiva.UbicacionActual`
  - `CargaMasiva.Observaciones`
  - `name="CargaMasiva.Confirmar"` con `value="false"`
  - `name="CargaMasiva.Confirmar"` con `value="true"`
  - `asp-validation-for` de campos de carga masiva
  - `@Html.AntiForgeryToken()`

## F. Reorganizacion elegida

Se eligieron dos paneles dentro del mismo `#modo-carga`:

- Alta individual: panel para crear una unidad puntual.
- Carga masiva: panel para crear varias unidades, con pasos visibles de preparacion, preview y confirmacion.

No se usaron tabs internos adicionales ni `<details>`, porque el objetivo era aclarar la diferencia sin esconder informacion critica ni agregar estado de interfaz.

## G. Cambios aplicados

- Se agrego copy breve en el encabezado del modo Carga para diferenciar alta individual y carga masiva.
- Se envolvieron alta individual y carga masiva en un grid responsive de dos paneles.
- Se renombro visualmente el panel de alta como "Alta individual" / "Crear una unidad puntual".
- Se preservo el texto contractual "El numero de serie es opcional".
- Se mantuvo el encabezado contractual "Carga masiva de unidades".
- En carga masiva se agregaron bloques visuales "Paso 1" y "Paso 2".
- El preview queda debajo de los botones de previsualizacion/confirmacion, dentro del formulario masivo.
- Se reforzo visualmente el estado "Preview listo".

## H. Contratos preservados

- `#modo-carga`.
- `#form-carga-masiva-unidades`.
- `POST Producto/CrearUnidad`.
- `POST Producto/CrearUnidadesMasivas`.
- Todos los `asp-controller`, `asp-action`, `asp-route-*`.
- Todos los `asp-for`.
- Todos los `name`.
- `name="CargaMasiva.Confirmar"` con valores `false` y `true`.
- `@Html.AntiForgeryToken()` en ambos POST.
- `asp-validation-summary`.
- `asp-validation-for`.
- Preview de carga masiva.
- Boton de confirmacion condicional de carga masiva.
- No se anidaron formularios.
- No se movieron inputs fuera de su formulario.

## I. Que no se toco

- Backend.
- Controllers.
- Services.
- Models.
- ViewModels.
- Migraciones.
- Endpoints.
- Payloads.
- Permisos.
- Reglas de stock.
- Calculos.
- JavaScript de logica.
- CSS global.
- Playwright specs.
- Ventas/Kira.
- Catalogo.
- Movimientos.
- AlertaStock.

## J. Riesgo funcional

Riesgo bajo. El cambio es Razor/HTML visual y preserva formularios POST reales, antiforgery, nombres de campos, acciones y condiciones de preview/confirmacion.

Riesgo residual: al usar dos columnas en desktop ancho, el panel de alta individual queda mas compacto que antes. En mobile ambos paneles siguen apilados.

## K. Validaciones

- `dotnet build --configuration Release`: compilacion correcta, 0 advertencias, 0 errores, tiempo 00:01:06.50.
- `dotnet test --configuration Release --filter "FullyQualifiedName~ProductoControllerPrecioTests&FullyQualifiedName~Unidades"`: 21/21 correctas, 0 errores, 0 omitidas.
- `git diff --check -- Views/Producto/Unidades.cshtml docs/misa-inventario-fisico-ux-2d-alta-carga.md`: OK.
- `git diff --check`: falla por trailing whitespace en `AGENTS.md` y `CLAUDE.md`, cambios locales preexistentes fuera de alcance.

## L. Tests/Playwright omitidos o ejecutados con motivo

Se ejecuto el filtro acotado de `ProductoControllerPrecioTests` con `Unidades` porque la fase toco estructura Razor de formularios y existen tests especificos de contrato para esta vista.

No se ejecuto suite general, tests de Ventas, tests de Cotizacion ni Playwright completo porque el alcance es Razor-only de Producto/Unidades y el prompt pidio no ejecutar esos frentes.

## M. Deudas restantes

- No hay Playwright especifico para Producto/Unidades que valide visualmente el modo Carga.
- El modo Carga sigue siendo una seccion larga cuando hay preview; ahora esta mas guiado, pero no se convirtio en wizard.
- Conciliacion queda para una fase dedicada.
- Mobile sigue usando tabla ancha en el modo Unidades; fuera de alcance de 2D.

## N. Proximo paso recomendado

`MISA-INVENTARIO-FISICO-UX-2E - Conciliacion enfocada`.
