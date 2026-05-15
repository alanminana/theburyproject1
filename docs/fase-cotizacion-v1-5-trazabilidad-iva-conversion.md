# Fase Cotización V1.5 — Trazabilidad bidireccional + IVA correcto en VentaDetalle convertido

## A. Objetivo

Cerrar dos deudas técnicas explícitas de V1.3/V1.4:

1. `IVAUnitario = 0` en `VentaDetalle` convertido desde cotización.
2. Sin trazabilidad bidireccional `CotizacionId ↔ VentaId`.

---

## B. Diagnóstico previo

| Pregunta | Respuesta |
|---|---|
| ¿Venta ya tiene FK a Cotización? | No. Se agrega en esta fase. |
| ¿Cotización ya guarda VentaId? | No. Se resuelve vía consulta inversa. |
| ¿Conviene `Venta.CotizacionOrigenId` o `Cotizacion.VentaId`? | `Venta.CotizacionOrigenId` — Opción A elegida. |
| ¿Una sola venta por cotización? | Sí. Estado `ConvertidaAVenta` garantiza doble conversión imposible. |
| Campos IVA en VentaDetalle | `PorcentajeIVA`, `AlicuotaIVAId`, `AlicuotaIVANombre`, `IVAUnitario`, `PrecioUnitarioNeto`, `SubtotalNeto`, `SubtotalIVA`, etc. |
| Fuente canónica de IVA | `ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto)` — prioriza `AlicuotaIVA` activa del producto, luego de Categoría, luego `PorcentajeIVA`, default 21%. |
| `IPrecioVigenteResolver` devuelve IVA | Solo `PrecioFinalConIva`. No desglosa. Hay que cargar producto separado. |
| ¿Hace falta tocar VentaService? | No. Lógica de IVA reproducida localmente en `CotizacionConversionService`. |

---

## C. Decisión de trazabilidad

**Opción A elegida: `Venta.CotizacionOrigenId` nullable FK.**

- La venta sabe de dónde vino (FK directa).
- Para ver venta desde cotización → query `Ventas WHERE CotizacionOrigenId = @id`.
- Sin acoplar entidad `Cotizacion` a `Venta`.
- Additive: columna nullable, sin romper ventas existentes.
- Sin tabla `CotizacionConversion` por ahora — una cotización genera exactamente una venta.

---

## D. Migración

Nombre: `AddCotizacionOrigenToVenta`

```sql
ALTER TABLE Ventas ADD CotizacionOrigenId int NULL;
CREATE INDEX IX_Ventas_CotizacionOrigenId ON Ventas (CotizacionOrigenId);
ALTER TABLE Ventas ADD CONSTRAINT FK_Ventas_Cotizaciones_CotizacionOrigenId
    FOREIGN KEY (CotizacionOrigenId) REFERENCES Cotizaciones(Id) ON DELETE SET NULL;
```

- Additive: columna nullable, índice, FK con `SET NULL`.
- No toca datos existentes.
- Reversible (`Down` elimina FK, índice y columna).

---

## E. IVA en VentaDetalle

**Causa del bug:** `ConstruirDetalles` seteaba `IVAUnitario = 0m` y `PrecioUnitarioNeto = precioUnitario` sin descomponer IVA.

**Fix aplicado en `ConstruirDetalles`:**
1. Acepta `IReadOnlyDictionary<int, Producto> productos` (cargado con `Include(AlicuotaIVA)` y `Include(Categoria).ThenInclude(AlicuotaIVA)`).
2. Usa `ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto)` para obtener porcentaje.
3. Descompone precio (que ya incluye IVA) en neto + IVA:
   - `divisor = 1 + pct/100`
   - `PrecioUnitarioNeto = Round(precioUnitario / divisor)`
   - `IVAUnitario = Round(precioUnitario - PrecioUnitarioNeto)`
   - `SubtotalNeto = Round(subtotal / divisor)`
   - `SubtotalIVA = Round(subtotal - SubtotalNeto)`
4. Rellena `PorcentajeIVA`, `AlicuotaIVAId`, `AlicuotaIVANombre`.
5. Mantiene `SubtotalFinalNeto = subtotalNeto`, `SubtotalFinalIVA = subtotalIva`.

También se actualiza `venta.IVA = detalles.Sum(d => d.SubtotalIVA)` al construir la venta.

**En carga de productos** (`ConvertirAVentaAsync`): ya se cargaba el diccionario de productos para validar activos; se le agregaron los includes necesarios para IVA.

---

## F. Cambios aplicados

| Archivo | Cambio |
|---|---|
| `Models/Entities/Venta.cs` | Agrega `CotizacionOrigenId int?` y nav `CotizacionOrigen`. |
| `Data/AppDbContext.cs` | Configura FK con `SetNull`, índice `IX_Ventas_CotizacionOrigenId`. |
| `Migrations/20260515234236_AddCotizacionOrigenToVenta.cs` | Migración additive (columna + índice + FK). |
| `Services/CotizacionConversionService.cs` | Setea `CotizacionOrigenId`, carga productos con IVA includes, calcula IVA correctamente en `ConstruirDetalles`. |
| `Services/CotizacionService.cs` | `ObtenerAsync` consulta venta relacionada si cotización está `ConvertidaAVenta`; `MapDetalle` acepta venta info y la propaga al DTO. |
| `Services/Models/CotizacionCrearRequest.cs` | `CotizacionResultado` agrega `VentaConvertidaId` y `NumeroVentaConvertida`. |
| `Views/Cotizacion/Detalles_tw.cshtml` | Sección `ConvertidaAVenta` muestra enlace "Ver venta" si hay `VentaConvertidaId`. |
| `TheBuryProyect.Tests/Integration/CotizacionConversionServiceTests.cs` | 8 tests nuevos V1.5. |

---

## G. UI mínima

En `Views/Cotizacion/Detalles_tw.cshtml`, sección `ConvertidaAVenta`:
- Si `Model.VentaConvertidaId.HasValue` → muestra botón "Ver venta {numero}" con link a `/Venta/Edit/{id}`.
- Si no tiene venta linkada aún (cotizaciones convertidas antes de V1.5) → solo muestra el mensaje existente.
- Sin tocar `Views/Venta/*`.

---

## H. Tests

8 tests nuevos en `CotizacionConversionServiceTests`:

| Test | Qué verifica |
|---|---|
| `Convertir_SetCotizacionOrigenIdEnVenta` | FK queda seteada al convertir |
| `Convertir_PermiteEncontrarVentaPorCotizacionOrigen` | Query inversa `CotizacionOrigenId` funciona |
| `Convertir_NoPermiteDobleConversion` | Segunda conversión falla |
| `Venta_CotizacionOrigenId_EsNullable_EnVentasNormales` | Ventas sin conversión tienen FK null |
| `Convertir_IVAUnitario_NoEsCeroSiProductoTieneIVA` | IVAUnitario > 0 para IVA 21% |
| `Convertir_IVACero_SiProductoNoTieneIVA` | IVAUnitario = 0 para productos sin IVA |
| `Convertir_SubtotalCoherente_ConIVADescompuesto` | 121 con 21% → neto 100, IVA 21, subtotalNeto 200, subtotalIVA 42 |
| `Convertir_UsaAlicuotaIVA_SiProductoLaTiene` | AlicuotaIVA activa toma prioridad sobre PorcentajeIVA |

**Resultado:** 102/102 tests pasan. Total suite ampliada: 849/849.

---

## I. Qué NO se tocó

- `VentaService` — no modificado.
- `venta-create.js` — no modificado.
- `Views/Venta/*` — no modificado.
- Confirmación, stock, caja, factura, crédito definitivo — intactos.
- Módulos de Juan (Devolucion, Producto, Proveedor, etc.) — intactos.
- `Cotizacion.cs` — no se le agregó `VentaId` ni navegación hacia Venta (se resuelve por query inversa).

---

## J. Riesgos y deuda remanente

| Ítem | Tipo | Prioridad |
|---|---|---|
| Cotizaciones convertidas antes de V1.5 no tienen `CotizacionOrigenId` → el enlace "Ver venta" no aparece | Deuda conocida, aceptada | Baja — solo afecta datos históricos del worktree dev |
| Permiso granular `cotizaciones.convert` | Deuda V1.5+ | Media |
| Preview UI no muestra tabla detallada de cambios de precio unitarios | Deuda V1.5+ | Media |
| Numeración con reintento ante concurrencia | Deuda técnica | Baja |
| Múltiples ventas por cotización (hoy: una sola) | Futura extensión | No planificada |

---

## K. Checklist actualizado

### Carlos — Cotización

- [x] V1.1 — Persistencia mínima
- [x] V1.2 — Diseño conversión Cotización → Venta
- [x] V1.3 — Implementación conversión controlada
- [x] V1.4 — UI conversión Cotización → Venta
- [x] V1.5 — Trazabilidad bidireccional + IVA correcto en VentaDetalle convertido

### Pendiente después de V1.5

- [ ] Permiso granular `cotizaciones.convert`
- [ ] Preview UI: tabla detallada de cambios de precio unitarios
- [ ] Numeración con reintento ante concurrencia
- [ ] Cancelación compleja
- [ ] Vencimiento automático
- [ ] Impresión/envío de cotización
