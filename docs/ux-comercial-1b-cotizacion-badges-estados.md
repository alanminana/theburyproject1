# UX-COMERCIAL-1B — Badges de estado en Cotización

## A. Objetivo

Aplicar los tokens CSS `.quote-state-badge` de UX-COMERCIAL-1A en las vistas de Cotización
para mejorar legibilidad de estados, coherencia visual y accesibilidad,
sin tocar lógica funcional, services, controllers, JS ni endpoints.

## B. Relación con fases anteriores

- **UX-COMERCIAL-0**: definió que Cotización es presupuesto/simulación, distinta de Venta.
  La integración debe ser visual y de lenguaje, nunca funcional.
- **UX-COMERCIAL-1A**: agregó tokens CSS comerciales en `shared-components.css` (líneas 1204–1253).
  Esta fase consume esos tokens sin crearlos.

## C. Vistas auditadas

| Vista | Muestra estado | Acción |
|---|---|---|
| `Views/Cotizacion/Index_tw.cshtml` | No (simulador, sin estado guardado) | Sin cambios |
| `Views/Cotizacion/Listado_tw.cshtml` | Sí — columna "Estado" | Modificada |
| `Views/Cotizacion/Detalles_tw.cshtml` | Sí — cabecera de cotización | Modificada |
| `Views/Cotizacion/Imprimir_tw.cshtml` | No auditada (fuera de alcance) | Sin cambios |

## D. Archivos modificados

- `Views/Cotizacion/Listado_tw.cshtml`
- `Views/Cotizacion/Detalles_tw.cshtml`
- `docs/ux-comercial-1b-cotizacion-badges-estados.md` (este archivo)

## E. Mapeo de estados

Enum `EstadoCotizacion` (`Models/Enums/EstadoCotizacion.cs`):

| Valor enum | Valor int | Clase CSS | Ícono | Label visible |
|---|---|---|---|---|
| `Emitida` | 1 | `quote-state-badge--emitida` | `pending` | Emitida |
| `ConvertidaAVenta` | 3 | `quote-state-badge--convertida` | `check_circle` | Convertida |
| `Cancelada` | 4 | `quote-state-badge--cancelada` | `cancel` | Cancelada |
| `Vencida` | 2 | `quote-state-badge--vencida` | `schedule` | Vencida |
| `Borrador` | 0 | `quote-state-badge` (base) | `fiber_manual_record` | Borrador |

> **Deuda**: `Borrador` no tiene variante CSS específica en UX-COMERCIAL-1A.
> Se usa la clase base sin modificador cromático. Pendiente para UX-COMERCIAL-2 si se necesita.

## F. Cambios visuales aplicados

### Listado_tw.cshtml

- Se agregaron tres funciones locales Razor (`QuoteBadgeClass`, `QuoteBadgeLabel`, `QuoteBadgeIcon`)
  en el bloque `@{...}` inicial.
- La celda de la columna "Estado" pasó de texto plano `@item.Estado` a un `<span>` con
  `.quote-state-badge` + variante cromática + ícono Material Symbols.

### Detalles_tw.cshtml

- Se agregaron las mismas tres funciones locales Razor en el bloque `@{...}` inicial.
- El párrafo de cabecera (`@cliente · fecha · @Model.Estado`) se refactorizó a
  `<p class="flex flex-wrap items-center gap-x-2 ...">` con spans individuales,
  reemplazando `@Model.Estado` por el badge con ícono y variante.
- El separador `·` lleva `aria-hidden="true"` para no ser anunciado por lectores de pantalla.

## G. Contratos preservados

- No se modificó ningún controller, service, endpoint ni ViewModel.
- No se modificó ningún archivo `.js`.
- La lógica `puedeConvertir` en Detalles sigue intacta.
- El botón "Convertir a Venta" y el panel de conversión no fueron tocados.
- El link "Ver venta" para `ConvertidaAVenta` no fue tocado.
- El panel de cotización cancelada no fue tocado.
- Las rutas de acción no fueron modificadas.
- La tabla de opciones de pago (`OpcionesPago`) en Detalles no fue tocada —
  su columna "Estado" corresponde al estado de la opción de pago, no al `EstadoCotizacion`.

## H. Accesibilidad

- Los badges tienen texto visible además del color (no dependen solo del color).
- El ícono Material Symbols es decorativo; el texto del badge es el comunicador principal.
- Los separadores `·` tienen `aria-hidden="true"`.
- La estructura `flex-wrap` en el párrafo de cabecera permite salto de línea en mobile.

## I. Mobile / Responsive

- El párrafo de cabecera en Detalles usa `flex-wrap` para que el badge salte a nueva línea
  en pantallas pequeñas sin truncarse.
- El badge en Listado es `white-space: nowrap` por la clase base; la tabla ya tiene
  `overflow-x-auto` en el contenedor padre.

## J. Validaciones ejecutadas

- `dotnet build --configuration Release -o tmpbuild_ux_comercial_1b`: **0 errores C#, 0 errores Razor**.
  El build estándar falló por file-lock del PID 4072 (proceso preexistente de sesión anterior,
  no iniciado por esta tarea). El error es solo de copia de `.exe`, no de compilación.
- `git diff --check`: **OK** (sin trailing whitespace).
- `git status --short`: solo archivos esperados modificados.

## K. Tests

Tests `dotnet test` bloqueados por file-lock del mismo PID 4072.
Causa: `dotnet test` recompila el proyecto principal y no puede copiar el `.exe` bloqueado.
No hay error C# real — confirmado por el build alternativo a `tmpbuild_ux_comercial_1b`.
Los cambios son exclusivamente de plantillas Razor `.cshtml`; no modifican ningún tipo,
servicio ni contrato verificable por los tests existentes (LayoutUiContractTests valida
layout global, no badges de módulos).

## L. Playwright

No ejecutado. Causa: file-lock del proceso preexistente PID 4072 impide recompilar
el proyecto, lo que haría que los tests E2E apunten a la versión sin los cambios.
Los cambios no afectan rutas, endpoints, selectores de layout ni elementos testados
en la suite `ui-4e-layout-visual.spec.js` (169 tests de layout global).

## M. Procesos al cierre

- PID 4072 (`TheBuryProyect.exe`): preexistente de sesión anterior. **No cerrado** —
  no fue iniciado por esta tarea.
- PID 20380 (`powershell.exe dotnet run`): preexistente de sesión anterior. **No cerrado**.
- No se iniciaron procesos nuevos de compilación ni servidor por esta tarea.

## N. Riesgos y deudas

| Ítem | Tipo | Detalle |
|---|---|---|
| `Borrador` sin variante | Deuda visual | No hay `--borrador` en UX-COMERCIAL-1A. Usar base hasta que se defina. |
| Tests y Playwright no ejecutados | Bloqueo externo | PID 4072 preexistente. Se recomienda ejecutar en sesión sin proceso activo. |
| `Imprimir_tw.cshtml` no auditada | Fuera de alcance | Puede mostrar estado en texto plano. Pendiente para UX-COMERCIAL-1C. |
| Opciones de pago (`item.Estado`) en Detalles | Fuera de alcance | El estado de opciones de pago es texto libre generado por el simulador, no `EstadoCotizacion`. No requiere badge. |

## O. Próximo paso recomendado

**UX-COMERCIAL-1C** — Auditar `Views/Cotizacion/Imprimir_tw.cshtml` y revisar si
`payment-status-chip` aplica a la columna "Estado" de opciones de pago en el simulador
(columna renderizada por JS en `cotizacion-simulador.js` — fuera del alcance de esta fase).
