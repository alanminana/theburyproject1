# Fase Kira — Caja 2: UX Detalle Ventas Apertura

**Rama:** `kira/caja-detalle-ventas-ux`  
**Base:** `kira/caja-totales-ventas-efectivas` (commit `ea4da0b`)  
**Fecha:** 2026-05-18

---

## A. Diagnóstico UX

La vista `DetallesApertura_tw.cshtml` mostraba `VentasDelTurno` en una tabla única sin distinción visual por impacto en caja. Estados como `Presupuesto`, `Cotizacion` y `Cancelada` aparecían junto a ventas confirmadas con su monto completo, generando confusión operativa para el encargado de caja.

Problemas concretos detectados:
- Sin separación visual entre ventas que generan ingreso y las que no.
- Presupuestos y cotizaciones mostraban total en verde junto a ventas reales.
- Canceladas aparecían con monto, sin indicación de que están anuladas.
- `PendienteRequisitos` y `PendienteFinanciacion` confundibles con ventas efectivas.
- El badge de estado usaba ternarios inline incompletos (solo Facturada/Entregada/Cancelada distinguidas; Confirmada quedaba en `slate` igual que Presupuesto).
- No había helpers reutilizables para badge de estado.

---

## B. Decisión de layout

**Elegida: Separación por bloques** (3 secciones independientes con header propio).

Alternativa descartada: tabla única con columna "Impacto caja". Razón: el operador necesita separación visual clara, no solo una columna adicional. Los presupuestos/canceladas distraen cuando están intercalados con ventas reales.

---

## C. Estados por bloque

| Bloque | Estados | Visual | Comportamiento |
|--------|---------|--------|----------------|
| **Ventas efectivas** | `Confirmada`, `Facturada`, `Entregada` | Emerald/Blue badges, total en color | Siempre visible si hay datos |
| **Operaciones pendientes** | `PendienteRequisitos`, `PendienteFinanciacion` | Amber badges, columna "Sin ingreso inmediato" | Visible solo si existen |
| **Registros de auditoría** | `Presupuesto`, `Cotizacion`, `Cancelada` | Slate apagado, opacidad 75%, total slate/tachado en Cancelada | **Colapsado por defecto** con `<details>/<summary>` |

---

## D. Cambios visuales

### Contador en header
Antes: `N ventas vinculadas`  
Después: `N ventas efectivas · M sin impacto inmediato`

### Bloque 1 — Ventas efectivas
- Badge estado: `BadgeClaseEstado()` — `Confirmada` → azul, `Facturada/Entregada` → emerald.
- Total: emerald (o amber si es crédito/cuenta corriente).
- Banner amber inferior si hay ventas a crédito personal/cuenta corriente.

### Bloque 2 — Operaciones pendientes
- Columna "Impacto caja" con badge amber "Sin ingreso inmediato".
- Tipo pago en slate neutro (no emerald).
- Total en amber.

### Bloque 3 — Registros de auditoría
- Elemento `<details>/<summary>` nativo — colapsado por defecto, sin JS.
- Tabla con `opacity-75`.
- Canceladas: total tachado + leyenda "Anulada" en rose.
- Presupuestos/Cotizaciones: total slate.
- Columna "Impacto caja" con badge "Sin impacto en caja" + ícono `block`.
- Footer: texto aclaratorio sobre finalidad de auditoría.

### Helpers Razor agregados a `@functions {}`
- `BadgeClaseEstado(EstadoVenta)` — devuelve clases Tailwind por estado.
- `LabelEstado(EstadoVenta)` — devuelve label legible por estado.
- Reemplaza los ternarios inline anteriores en la columna Estado.

---

## E. Tests agregados/ajustados

Nuevo archivo: `TheBuryProyect.Tests/Unit/CajaDetallesAperturaContractTests.cs`

Tests de contrato (leen el .cshtml directamente, sin infraestructura de renderizado Razor):

| Test | Verifica |
|------|----------|
| `DetallesApertura_ContieneTextoVentasEfectivas` | Bloque 1 presente |
| `DetallesApertura_ContieneTextoSinImpactoEnCaja` | Bloque 3 con texto correcto |
| `DetallesApertura_ContieneRegistrosDeAuditoria` | Header bloque 3 presente |
| `DetallesApertura_ContieneOperacionesPendientes` | Bloque 2 presente |
| `DetallesApertura_UsaAgrupacionPorEstadoVenta` | Variables `ventasEfectivas/Pendientes/Auditoria` presentes |
| `DetallesApertura_ContadorDistingueEfectivasYSinImpacto` | Contador en header correcto |
| `DetallesApertura_NoUsaHtmlRawEnSeccionVentas` | Seguridad XSS verificada |
| `DetallesApertura_BloquePendientesUsaBadgeAmber` | "Sin ingreso inmediato" presente |
| `DetallesApertura_BloqueAuditoriaEsColapsable` | `<details>/<summary>` presentes |

---

## F. Validaciones ejecutadas

```
dotnet build --configuration Release  →  0 errores, 0 advertencias
dotnet test --filter "Caja"           →  170/170 (9 nuevos)
dotnet test --filter "Caja|Venta|MovimientoCaja"  →  904/904
git diff --check                      →  limpio
```

---

## G. Qué NO se tocó

- `Services/CajaService.cs` — sin cambios.
- `ViewModels/CajaViewModel.cs` — sin cambios. `VentasDelTurno` sigue siendo la fuente completa; la separación ocurre en la vista.
- `MovimientoCaja` — sin cambios.
- `VentaService` — sin cambios.
- Reglas de cancelación — sin cambios.
- Caja cierre/apertura — sin cambios.
- Cotización, Stock, Factura, ProductoUnidad, migraciones — sin cambios.
- Tests de CajaService preexistentes — ninguno modificado.

---

## H. Riesgos y deuda remanente

- **`<details>` nativo:** el bloque de auditoría usa collapse nativo del browser. No tiene animación de apertura. Si se quiere animación se puede agregar CSS en `_CajaModuleStyles.cshtml` sin tocar lógica.
- **tipoPagoLabel repetido:** cada bloque repite el switch de `tipoPagoLabel`. Es posible extraerlo a un helper en `@functions {}` en una iteración posterior.
- **Scroll affordance del bloque 3:** el `data-oc-scroll` en el bloque colapsado puede no inicializarse si el JS de `horizontal-scroll-affordance.js` corre antes de abrir el `<details>`. Bajo impacto (el bloque es solo auditoría).
- **VentasDelTurno sin estado "incierto":** si en el futuro se agregan nuevos `EstadoVenta`, quedarían sin grupo y no aparecerían en ningún bloque. Considerar agregar un bloque fallback o un Assert en tests.

---

## Checklist actualizado

```
✅ Kira Caja 1 — backend totales efectivos cerrado (ea4da0b)
✅ Kira Caja 2 — UX detalle apertura cerrado
⬜ Integrar kira/caja-totales-ventas-efectivas a main
⬜ Integrar kira/caja-detalle-ventas-ux a main
```
