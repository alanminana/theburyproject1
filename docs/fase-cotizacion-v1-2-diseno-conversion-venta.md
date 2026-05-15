# Fase Cotización V1.2 — Diseño conversión controlada Cotización → Venta

**Agente:** Carlos — Cotización  
**Rama:** `carlos/cotizacion-v1-contratos`  
**Estado:** Diseño cerrado. V1.3 implementa la conversión controlada sin UI completa.  
**Fecha:** 2026-05-15  

---

## A. Diagnóstico del flujo Venta actual

### A.1 Cómo se crea una Venta hoy

El flujo canónico pasa por `VentaService.CreateAsync(VentaViewModel)`. El ViewModel lo arma el front-end desde `venta-create.js` y lo envía por `POST /api/venta`. `VentaController` también ofrece la vista `Create_tw.cshtml`.

**Secuencia en `CreateAsync`:**
1. Valida que TipoPago no sea tarjeta en venta nueva (`ValidarTipoPagoTarjetaNoPermitidoEnVentaNueva`).
2. Exige **caja abierta** (`AsegurarCajaAbiertaParaUsuarioActualAsync`) — hard stop.
3. Mapea el ViewModel a la entidad `Venta`.
4. Asigna `AperturaCajaId`.
5. Resuelve vendedor.
6. Genera número de venta.
7. Agrega detalles (`AgregarDetalles`).
8. **Recalcula precio vigente** en backend (`AplicarPrecioVigenteADetallesAsync`) — no confía en precios del front.
9. Calcula totales y comisiones.
10. Si crédito personal: valida condiciones pago, captura snapshot límite, ejecuta `ValidarVentaCreditoPersonalAsync`. Si `NoViable` sin excepción documental: **rechaza**.
11. Aplica resultado de validación (puede derivar a `PendienteRequisitos` o `PendienteFinanciacion`).
12. Guarda la venta en DB con retry por número duplicado.
13. Si crédito personal y estado `PendienteFinanciacion`: **crea crédito en estado `PendienteConfiguracion`**.
14. Guarda datos adicionales (tarjeta, cheque, datos crédito personal JSON).
15. Commit.

### A.2 Datos que exige `CreateAsync`

Mínimos por modelo/validación:
- `ClienteId` — `[Required]` en la entidad `Venta` (no nullable en DB).
- `TipoPago` — `[Required]`.
- Al menos un detalle con `ProductoId` y `Cantidad`.
- Para crédito personal: cliente debe existir y pasar `ValidarVentaCreditoPersonalAsync`.
- Caja abierta para el usuario autenticado.

Opcionales pero relevantes:
- `Observaciones`, `VendedorUserId`, `Estado` destino inicial.

### A.3 Efectos secundarios de `CreateAsync`

| Efecto | Se dispara en CreateAsync |
|--------|--------------------------|
| Captura snapshot límite crédito | Sí (solo crédito personal) |
| Crea crédito `PendienteConfiguracion` | Sí (si TipoPago = CreditoPersonal y estado = PendienteFinanciacion) |
| Guarda precio vigente en detalle | Sí (siempre, sobreescribe precio enviado) |
| Registra caja | **No** |
| Descuenta stock | **No** |
| Marca ProductoUnidad | **No** |
| Genera factura | **No** |
| Crea crédito definitivo/cuotas | **No** |

### A.4 Efectos secundarios de `ConfirmarVentaAsync`

Ejecutados **en este orden** en `ConfirmarVentaAsync` (para ventas no-crédito):
1. Exige caja abierta.
2. `ValidarEstadoParaConfirmacion` — solo acepta `Presupuesto` o `PendienteRequisitos`.
3. `ValidarStock` — verifica stock actual contra cantidades en detalle.
4. `ValidarUnidadesTrazablesAsync` — para productos con `RequiereNumeroSerie`, exige `ProductoUnidadId` en `EnStock`.
5. **`DescontarStockYRegistrarMovimientos`** — descuento real de stock, movimiento registrado.
6. **`MarcarUnidadesVendidasAsync`** — marca `ProductoUnidad` como `Vendida`.
7. Si crédito personal: `CrearCreditoDefinitivoDesdeJsonAsync` — crea cuotas, descuenta saldo crédito.
8. Genera alertas de stock bajo.
9. Cambia estado a `Confirmada`.
10. Después del SaveChanges: **`RegistrarMovimientoVentaAsync` en caja** (solo Efectivo/Tarjeta/Cheque/Transferencia/MercadoPago — NO crédito personal, NO cuenta corriente).

`ConfirmarVentaCreditoAsync` tiene el mismo flujo para crédito ya configurado.

### A.5 Cuándo se descuenta stock
**Solo en `ConfirmarVentaAsync`** (y `ConfirmarVentaCreditoAsync`), mediante `DescontarStockYRegistrarMovimientos`. No se toca en Create, Update ni en ninguna conversión previa.

### A.6 Cuándo se marca ProductoUnidad
**Solo en `ConfirmarVentaAsync`** (y `ConfirmarVentaCreditoAsync`), mediante `MarcarUnidadesVendidasAsync`. La validación `ValidarUnidadesTrazablesAsync` exige que la unidad ya esté asignada y en `EnStock` antes del descuento.

### A.7 Cuándo se registra Caja
**Solo post-commit en `ConfirmarVentaAsync`** / `ConfirmarVentaCreditoAsync`. En `FacturarVentaAsync` hay un fallback para ventas confirmadas antes del fix, pero el flujo normal es al confirmar. **Nunca en Create.**

### A.8 Cuándo se crea crédito definitivo
- `CreateAsync`: crea crédito en estado `PendienteConfiguracion` (solo si TipoPago = CreditoPersonal y deriva a `PendienteFinanciacion`).
- `ConfirmarVentaAsync`: ejecuta `CrearCreditoDefinitivoDesdeJsonAsync` (cuotas reales, descuenta saldo).
- `ConfirmarVentaCreditoAsync`: ejecuta `GenerarCuotasCreditoAsync` y marca crédito como `Generado`.

### A.9 Cuándo se factura
Solo en `FacturarVentaAsync`, que exige estado `Confirmada`. Nunca antes.

### A.10 Estado de Venta para conversión segura

`EstadoVenta` real del sistema:

| Valor | Nombre | Uso |
|-------|--------|-----|
| 0 | `Cotizacion` | Cotización sin compromiso — editable, eliminable |
| 1 | `Presupuesto` | Presupuesto formal — editable, eliminable, **confirmable** |
| 2 | `Confirmada` | Venta confirmada — stock descontado |
| 3 | `Facturada` | Facturada |
| 4 | `Entregada` | Entregada |
| 5 | `Cancelada` | Cancelada |
| 6 | `PendienteRequisitos` | Esperando documentación/autorización — **confirmable** |
| 7 | `PendienteFinanciacion` | Crédito personal pendiente de configurar — editable |

**`ValidarEstadoParaConfirmacion` solo acepta `Presupuesto` o `PendienteRequisitos`.**

**Estado destino recomendado para conversión: `EstadoVenta.Cotizacion`.**

Justificación:
- Es el estado de "cotización sin compromiso" dentro del modelo Venta.
- Es editable y eliminable por diseño.
- NO es confirmable por `ValidarEstadoParaConfirmacion` — el usuario deberá pasar a `Presupuesto` manualmente antes de confirmar.
- Cero efectos secundarios irreversibles.
- Conceptualmente correcto: la Cotización convertida se convierte en una Venta-Cotización editable, no en un compromiso.

> **Nota:** Esto implica que el estado `EstadoVenta.Cotizacion` tiene doble uso en el sistema actual: ventas creadas directamente en ese estado y ventas convertidas desde `Cotizacion` (la entidad propia). Esto es aceptable para V1.2. Si en el futuro se necesita distinguir origen, se puede usar el campo `CotizacionOrigenId` descrito en la sección I.

---

## B. Brechas para la conversión

| Brecha | Descripción | Impacto |
|--------|-------------|---------|
| B1 | `Venta.ClienteId` no nullable en entidad/ViewModel | Si cotización no tiene cliente, la conversión es bloqueante |
| B2 | `CreateAsync` exige caja abierta | La conversión disparada por cualquier usuario bloquea si no hay caja abierta para ese usuario |
| B3 | `CreateAsync` recalcula precio vigente siempre | El snapshot cotizado se pierde — hay que decidir política de precios |
| B4 | `ValidarEstadoParaConfirmacion` no acepta `Cotizacion` | La venta convertida necesita un paso manual a `Presupuesto` para poder confirmarse |
| B5 | Crédito personal: `CreateAsync` puede crear crédito `PendienteConfiguracion` inmediatamente | Si se llama directamente a `CreateAsync` con TipoPago CreditoPersonal, se crea un crédito prematuro |
| B6 | Productos trazables sin unidad asignada | La cotización no reserva unidades — la venta convertida no tendrá `ProductoUnidadId` |
| B7 | TipoPago en cotización usa `CotizacionMedioPagoTipo` — mapeo a `TipoPago` necesario | No hay mapeo automático definido aún |
| B8 | Sin trazabilidad de origen | No hay campo `CotizacionOrigenId` en Venta ni tabla de conversión |
| B9 | Cotización puede haber vencido | Sin validación de vigencia antes de convertir |
| B10 | Cotización ya convertida | Sin guard para evitar doble conversión |

---

## C. Contrato funcional de conversión

### C.1 Definición

Convertir una Cotización genera una **Venta editable en estado `EstadoVenta.Cotizacion`**, sin disparar ningún efecto secundario irreversible.

La venta convertida es una venta incompleta que el operador debe completar y confirmar mediante el flujo normal.

### C.2 Qué copia la conversión

| Campo origen (Cotizacion) | Campo destino (Venta) | Notas |
|---------------------------|----------------------|-------|
| `ClienteId` | `ClienteId` | Solo si está presente en cotización |
| `Detalles[].ProductoId` | `Detalles[].ProductoId` | |
| `Detalles[].Cantidad` | `Detalles[].Cantidad` | |
| `Detalles[].PrecioUnitarioSnapshot` | Precio inicial del detalle | Sujeto a política de precios (ver sección D) |
| `MedioPagoSeleccionado` | `TipoPago` | Mediante mapeo `CotizacionMedioPagoTipo → TipoPago` |
| `CantidadCuotasSeleccionada` | `DatosCreditoPersonall.CantidadCuotas` | Solo si aplica |
| `Observaciones` | `Observaciones` | |
| `Id` (de la cotización) | Campo futuro `CotizacionOrigenId` | Solo si se agrega el campo |

### C.3 Qué NO dispara la conversión

- Descuento de stock.
- Marcado de `ProductoUnidad`.
- Registro de movimiento en caja.
- Generación de factura.
- Creación de crédito definitivo/cuotas.
- Captura de snapshot de límite de crédito.
- Validación `ValidarVentaCreditoPersonalAsync` (se ejecutará al confirmar).

### C.4 Qué valida la conversión (previamente, en preview)

- Cotización existe y no está eliminada.
- Estado de cotización es `Emitida` (no `Vencida`, `ConvertidaAVenta`, `Cancelada`, `Borrador`).
- Cotización no ha sido ya convertida (`Estado != ConvertidaAVenta`).
- Cotización no vencida (`FechaVencimiento` nula o futura).
- Cliente presente si el medio de pago es crédito personal.
- Productos activos (no eliminados).
- Advertencia si algún precio cambió desde el snapshot.
- Advertencia si algún plan/medio de pago ya no está activo.
- Advertencia si hay productos trazables que requerirán selección de unidad.

---

## D. Regla de precios

### D.1 Opciones evaluadas

**Opción A — Usar snapshot cotizado siempre**
- Ventaja: respeta el presupuesto dado al cliente.
- Riesgo: puede vender a precio desactualizado si el operador no revisa.

**Opción B — Recalcular precio actual al convertir**
- Ventaja: precio siempre vigente.
- Riesgo: el presupuesto pierde valor; el cliente puede reclamar.

**Opción C — Comparar y pedir confirmación (recomendada)**
- La conversión detecta diferencias entre snapshot y precio actual.
- Si hay diferencia: muestra advertencia y permite elegir.
- Si la cotización está vigente (`FechaVencimiento` futura o nula) y sin diferencia de precios: usar snapshot directamente.
- Si la cotización venció: recalcular obligatoriamente (precio actual).

### D.2 Decisión recomendada

**Política híbrida:**

1. Si cotización está **vigente** y precios **no cambiaron**: usar snapshot (respeta presupuesto).
2. Si cotización está **vigente** pero algún precio **cambió**: mostrar advertencia; operador elige snapshot o precio actual.
3. Si cotización **venció**: precio actual obligatorio (el presupuesto ya no es válido).

Esta política debe documentarse en UI con texto claro al operador.

> **Nota técnica:** `CreateAsync` llama a `AplicarPrecioVigenteADetallesAsync` que sobreescribe el precio. El servicio de conversión **no debe llamar a `CreateAsync` directamente**. Debe armar la entidad `Venta` de forma controlada para poder preservar el snapshot si corresponde, o bien llamar a un método específico que permita decidir la fuente del precio.

---

## E. Estado de venta destino

**Estado destino: `EstadoVenta.Cotizacion` (valor 0)**

Justificación:
- Diseñado exactamente para "cotización sin compromiso" dentro del flujo Venta.
- Editable y eliminable sin restricciones adicionales.
- **No confirmable directamente** — el operador debe promoverla a `Presupuesto` (o el estado que corresponda según la validación) antes de confirmar.
- No requiere caja abierta para existir en ese estado.
- El flujo de autorización, requisitos y financiación se activa solo al avanzar el estado.

**El operador, después de completar los datos faltantes, cambia el estado a `Presupuesto` y sigue el flujo normal.**

---

## F. Cliente obligatorio/opcional

### F.1 Regla por medio de pago

| Medio de pago cotizado | Regla de cliente |
|------------------------|-----------------|
| Efectivo / Transferencia / Cheque | Cliente puede seguir siendo `null` en la cotización; la venta se crea sin cliente solo si `Venta.ClienteId` lo soporta |
| Crédito personal | **Cliente obligatorio antes de convertir** — `CreateAsync` exige `ClienteId` y `ValidarVentaCreditoPersonalAsync` lo valida |
| Tarjeta / MercadoPago | Cliente obligatorio en el flujo actual de Venta |

### F.2 Problema real

La entidad `Venta` tiene `ClienteId` como `int` (no nullable) y la validación del ViewModel tiene `[Required]`. La conversión **no puede bypassear este constraint** sin un cambio de modelo.

### F.3 Decisión recomendada

- Si la cotización tiene `ClienteId`: copiarlo a la venta sin cambios.
- Si la cotización **no tiene** `ClienteId`:
  - Para crédito personal: **bloquear conversión** — pedir cliente antes.
  - Para otros medios: **mostrar advertencia**, permitir al operador completar el cliente en la UI de conversión antes de confirmar la creación de la Venta.
- No forzar cliente "genérico" ni crear bypass del modelo.

---

## G. ProductoUnidad / Stock

### G.1 Regla de conversión

La cotización no reserva `ProductoUnidad` ni stock. Al convertir:
- No asignar `ProductoUnidadId` en ningún `VentaDetalle`.
- No verificar stock al momento de la conversión.
- No registrar movimiento de stock.

### G.2 Qué ocurre al confirmar la venta convertida

`ConfirmarVentaAsync` ejecuta `ValidarUnidadesTrazablesAsync` antes de descontar. Para productos con `RequiereNumeroSerie = true`, exige `ProductoUnidadId` presente y en estado `EnStock`. Si la venta convertida no tiene unidades asignadas, la confirmación fallará con mensaje claro.

**El operador debe seleccionar unidades trazables en el flujo de edición de la Venta, antes de intentar confirmar.**

### G.3 Advertencia en preview de conversión

El preview debe listar productos trazables y advertir que requerirán selección de unidad antes de confirmar.

---

## H. Crédito personal

### H.1 Regla de conversión

La cotización puede haber simulado crédito personal via `ICotizacionPagoCalculator`. Al convertir:
- **No crear crédito definitivo**.
- **No crear crédito `PendienteConfiguracion`**.
- **No registrar cuotas**.
- Copiar la opción seleccionada (`CantidadCuotasSeleccionada`, `ValorCuotaSeleccionada`) como datos iniciales del ViewModel si aplica.

### H.2 Por qué no llamar a `CreateAsync` directamente

`CreateAsync` con `TipoPago = CreditoPersonal` puede crear un crédito `PendienteConfiguracion` inmediatamente (líneas 267-272 de VentaService). El servicio de conversión debe evitar esto construyendo la entidad de forma que derive a `EstadoVenta.Cotizacion` y no a `PendienteFinanciacion`.

### H.3 Cuándo se evalúa el crédito real

Al avanzar la venta convertida por el flujo normal (editar datos, cambiar a `Presupuesto`, confirmar), `ValidarVentaCreditoPersonalAsync` y `ValidarConfirmacionVentaAsync` se ejecutarán con los datos actuales del cliente y los límites vigentes.

---

## I. Modelo de relación Cotización → Venta

### I.1 Opción A — Campo nullable en Venta

```csharp
// Venta.cs
public int? CotizacionOrigenId { get; set; }
public virtual Cotizacion? CotizacionOrigen { get; set; }
```

Ventaja: simple, consulta directa.  
Riesgo: migración en tabla grande, acoplamiento entre módulos.

### I.2 Opción B — Tabla de conversión

```csharp
public class CotizacionConversion : AuditableEntity
{
    public int CotizacionId { get; set; }
    public int VentaId { get; set; }
    public DateTime FechaConversion { get; set; }
    public string UsuarioConversion { get; set; } = string.Empty;
    // ...
}
```

Ventaja: no modifica Venta, permite múltiples conversiones (revertida y reconvertida).  
Riesgo: más complejo para consultas.

### I.3 Decisión recomendada para V1.2

**No implementar todavía.** Razones:
- Requiere migración y revisión de reportes.
- Para V1.2 alcanza con marcar la cotización como `ConvertidaAVenta` en `EstadoCotizacion`.
- El campo `CotizacionOrigenId` o la tabla `CotizacionConversion` se evalúan en una fase futura cuando exista necesidad real de trazabilidad bidireccional.

**Para V1.2:** al convertir, marcar `Cotizacion.Estado = EstadoCotizacion.ConvertidaAVenta`. El sistema ya tiene este enum definido. Esto evita doble conversión y da trazabilidad unidireccional suficiente.

---

## J. Servicio futuro recomendado

### J.1 Interfaz

```csharp
public interface ICotizacionConversionService
{
    Task<CotizacionConversionPreviewResultado> PreviewConversionAsync(
        int cotizacionId, 
        CancellationToken ct = default);

    Task<CotizacionConversionResultado> ConvertirAVentaAsync(
        int cotizacionId,
        CotizacionConversionRequest request,
        string usuario,
        CancellationToken ct = default);
}
```

### J.2 Modelos de resultado

```csharp
public class CotizacionConversionPreviewResultado
{
    public bool Convertible { get; set; }
    public List<string> Errores { get; set; } = new();       // bloquean conversión
    public List<string> Advertencias { get; set; } = new();  // informativas
    public bool ClienteFaltante { get; set; }
    public bool HayCambioPrecios { get; set; }
    public bool HayProductosTrazables { get; set; }
    public bool HayPlanesInactivos { get; set; }
    public bool CotizacionVencida { get; set; }
    public List<DiferenciaPrecioDto> DiferenciasPrecios { get; set; } = new();
}

public class CotizacionConversionRequest
{
    public int? ClienteIdOverride { get; set; }         // cliente seleccionado en UI si cotización no lo tenía
    public bool UsarPrecioActual { get; set; }           // true = precio actual; false = snapshot cotizado
    public TipoPago? TipoPagoOverride { get; set; }     // si operador cambia medio de pago al convertir
}

public class CotizacionConversionResultado
{
    public bool Exitoso { get; set; }
    public int? VentaId { get; set; }
    public string? Error { get; set; }
}
```

### J.3 Lógica de `PreviewConversionAsync`

1. Buscar cotización, verificar no eliminada.
2. Verificar estado `Emitida` — si no, error.
3. Verificar no vencida.
4. Verificar no convertida (`Estado != ConvertidaAVenta`).
5. Verificar cliente: si falta y medio es crédito personal → error bloqueante; si falta y otro medio → advertencia.
6. Para cada producto en detalles: verificar activo, comparar precio snapshot vs actual, identificar si es trazable.
7. Verificar medio de pago seleccionado sigue activo en `ConfiguracionPago`.
8. Si crédito personal: verificar plan/tasas siguen vigentes.
9. Retornar resumen con errores (bloquean) y advertencias (informan).

### J.4 Lógica de `ConvertirAVentaAsync`

1. Ejecutar preview interno.
2. Si hay errores → lanzar excepción o retornar resultado fallido.
3. Resolver cliente (cotización o override de request).
4. Resolver precios según `request.UsarPrecioActual`.
5. Mapear `CotizacionMedioPagoTipo → TipoPago`.
6. Construir entidad `Venta` directamente (sin pasar por `CreateAsync`) para evitar efectos secundarios:
   - Estado: `EstadoVenta.Cotizacion`.
   - No crear crédito.
   - No resolver caja.
   - Precio: snapshot o actual según política.
   - `AperturaCajaId`: null (se asignará cuando el operador confirme).
7. Asignar número mediante `VentaNumberGenerator.GenerarNumeroAsync(EstadoVenta.Cotizacion)`.
8. Calcular totales y IVA en backend.
9. Guardar en transacción.
10. Marcar `Cotizacion.Estado = ConvertidaAVenta`.
11. Commit.
12. Retornar `VentaId`.

> **Regla crítica:** `ConvertirAVentaAsync` **no llama a `VentaService.CreateAsync`**. Construye la entidad directamente para tener control total sobre los efectos secundarios. Reutiliza servicios de soporte (número, precio) sin pasar por la orquestación completa de ventas.

### J.5 Dependencias del servicio

- `AppDbContext` — directo.
- `VentaNumberGenerator` — para generar número.
- `IPrecioVigenteResolver` — para comparar/aplicar precio actual.
- `IConfiguracionPagoService` — para verificar planes activos.
- `ICotizacionService` — para cargar la cotización con detalles.
- No depende de: `ICajaService`, `IMovimientoStockService`, `IProductoUnidadService`, `ICreditoDisponibleService`, `IContratoVentaCreditoService`.

---

## K. UI futura recomendada

### K.1 Punto de entrada

En `Views/Cotizacion/Detalles.cshtml`:
- Botón "Convertir a Venta" visible cuando `EstadoCotizacion == Emitida`.
- Botón deshabilitado con tooltip si `Vencida`, `ConvertidaAVenta` o `Cancelada`.

### K.2 Flujo de conversión

```
[Usuario presiona "Convertir a Venta"]
        ↓
GET /api/cotizacion/{id}/conversion-preview
        ↓
    ¿Hay errores bloqueantes?
    Sí → mostrar errores, deshabilitar conversión
    No → continuar
        ↓
    ¿Hay advertencias?
    Sí → mostrar advertencias con checkboxes de confirmación
    No → continuar
        ↓
    ¿Cliente faltante?
    Sí → selector de cliente obligatorio
    No → continuar
        ↓
    ¿Hay diferencias de precios y cotización vigente?
    Sí → selector: "Usar precios cotizados" vs "Usar precios actuales"
    No → usar snapshot automáticamente
        ↓
[Usuario confirma]
        ↓
POST /api/cotizacion/{id}/convertir
        ↓
    Éxito → redirect a /Venta/Edit/{ventaId} (con mensaje de éxito)
    Error → mostrar error, permanecer en cotización
```

### K.3 Mensajes clave

- `"La cotización fue convertida a venta. Completá los datos pendientes antes de confirmar."`
- `"Esta venta fue generada desde la cotización #NRO. Revisá precios y datos antes de confirmar."`
- Para productos trazables: `"Los siguientes productos requieren selección de unidad física antes de confirmar: [lista]."`

---

## L. Riesgos

| ID | Riesgo | Probabilidad | Impacto | Mitigación |
|----|--------|-------------|---------|------------|
| R1 | Duplicar reglas de VentaService en servicio de conversión | Alta | Medio | El servicio de conversión crea la entidad pero no orquesta confirmación; las reglas de negocio críticas siguen en VentaService |
| R2 | Crear Venta con datos inválidos (cliente null, precio 0) | Media | Alto | Validación previa en preview + validaciones en entidad/ViewModel |
| R3 | Saltarse stock/unidad | Baja | Alto | La conversión no llama a DescontarStock ni MarcarUnidades; `ValidarUnidadesTrazablesAsync` bloquea al confirmar si faltan |
| R4 | Usar precios vencidos sin saberlo | Media | Medio | Preview detecta diferencias; UI muestra advertencia |
| R5 | Usar planes de crédito inactivos | Media | Alto | Preview verifica actividad de planes antes de mostrar opciones |
| R6 | Crédito personal sin reevaluar | Media | Alto | Conversión no crea crédito; la evaluación real ocurre al confirmar con datos actuales |
| R7 | Cliente faltante en medio crédito personal | Alta | Alto | Preview bloquea si medio = crédito personal y no hay cliente |
| R8 | Cotización ya convertida | Media | Medio | Guard en preview verifica `Estado == Emitida`; al convertir se marca `ConvertidaAVenta` en transacción |
| R9 | Concurrencia: dos usuarios convierten la misma cotización | Baja | Alto | Transacción + verificación de estado en la misma transacción (optimistic concurrency sobre Cotizacion.RowVersion) |
| R10 | Auditoría insuficiente: no se sabe quién convirtió ni cuándo | Media | Medio | Campo `CotizacionOrigenId` futuro o tabla `CotizacionConversion`; mínimo: log de aplicación con `CreatedBy` en Venta |
| R11 | Conflicto con `EstadoVenta.Cotizacion` existente | Baja | Bajo | El estado ya existe y tiene semántica compatible; la distinción de "origen" puede hacerse por campo futuro |
| R12 | `CreateAsync` llamado directamente desde UI con datos de cotización | Media | Alto | Implementar endpoint dedicado `/api/cotizacion/{id}/convertir`; no reusar endpoint de venta |

---

## M. Clasificación de componentes

| Componente | Clasificación | Evidencia | Decisión |
|------------|---------------|-----------|----------|
| `CotizacionService` | Canónico nuevo | Registrado en DI, usado por controllers V1.x, tests pasan | Extender con método de conversión o nuevo servicio |
| `ICotizacionService` | Canónico nuevo | Interfaz propia, implementación real | Agregar método de conversión en V1.3 |
| `VentaService` | Canónico Venta | Registrado en DI, flujo principal de ventas, 300+ tests | No tocar en diseño; reutilizar servicios auxiliares |
| `VentaValidator` | Canónico Venta | Usado por VentaService, tests de validación | No modificar; sus guards protegen el flujo de confirmación |
| `EstadoVenta.Cotizacion` | Canónico existente | Valor 0 del enum, usado en VentaValidator como estado editable | Reutilizar como estado destino de conversión |
| `EstadoCotizacion.ConvertidaAVenta` | Canónico nuevo | Valor 3 del enum propio de Cotización, ya definido | Usar para marcar cotizaciones convertidas |
| `ICotizacionConversionService` | Nuevo — diseñado | No existe todavía | Crear en V1.3 |
| `VentaNumberGenerator` | Canónico auxiliar | Usado en VentaService, acepta EstadoVenta | Reutilizar en servicio de conversión |
| `IPrecioVigenteResolver` | Canónico auxiliar | Inyectado en VentaService, compara precios | Reutilizar en preview de conversión |

---

## N. Checklist

### Completado en esta fase (V1.2)
- [x] Diagnóstico flujo Venta actual
- [x] Diagnóstico efectos secundarios CreateAsync / ConfirmarVentaAsync
- [x] Identificar brechas para conversión
- [x] Definir contrato funcional de conversión
- [x] Decidir política de precios (opción C híbrida)
- [x] Definir estado destino (`EstadoVenta.Cotizacion`)
- [x] Definir regla cliente obligatorio/opcional
- [x] Definir regla ProductoUnidad/stock en conversión
- [x] Definir regla crédito personal en conversión
- [x] Diseñar modelo de relación Cotización → Venta (diferido a V1.3+)
- [x] Diseñar interfaz `ICotizacionConversionService`
- [x] Diseñar flujo UI de conversión
- [x] Documentar riesgos
- [x] Clasificación de componentes
- [x] Documento de diseño creado
- [x] Build OK
- [x] Tests Cotizacion OK (57 passed)
- [x] Commit + push

### Pendiente (V1.3 — implementación)
- [ ] Implementar `ICotizacionConversionService` y `CotizacionConversionService`
- [ ] Implementar `PreviewConversionAsync`
- [ ] Implementar `ConvertirAVentaAsync`
- [ ] Agregar endpoint `GET /api/cotizacion/{id}/conversion-preview`
- [ ] Agregar endpoint `POST /api/cotizacion/{id}/convertir`
- [ ] Tests unitarios del servicio de conversión
- [ ] Tests de integración: conversión exitosa, cotización vencida, cotización ya convertida, cliente faltante
- [ ] UI: botón en `Detalles.cshtml`, pantalla de preview, confirmación
- [ ] Mapeo `CotizacionMedioPagoTipo → TipoPago`
- [ ] Validación de planes activos en preview
- [ ] Campo `CotizacionOrigenId` (evaluación — puede quedar para V1.4+)

---

## O. Prompt siguiente recomendado

```
CARLOS — FASE COTIZACIÓN V1.3 — Implementación conversión controlada Cotización → Venta

Contexto: V1.2 cerrado. Documento de diseño en docs/fase-cotizacion-v1-2-diseno-conversion-venta.md.

Implementar:
1. ICotizacionConversionService con PreviewConversionAsync y ConvertirAVentaAsync
2. CotizacionConversionService (sin llamar a VentaService.CreateAsync)
3. Endpoint GET /api/cotizacion/{id}/conversion-preview
4. Endpoint POST /api/cotizacion/{id}/convertir
5. Tests unitarios del servicio
6. Marcar cotización como ConvertidaAVenta en la misma transacción

No implementar todavía:
- UI completa de conversión
- Campo CotizacionOrigenId en Venta (evaluar en V1.4)
- Mapeo tarjeta/MercadoPago a crédito personal (ya contemplado en diseño)

Validar con: dotnet build, dotnet test --filter "Cotizacion", git diff --check.
Trabajar en E:\theburyproject-carlos-cotizacion, rama carlos/cotizacion-v1-contratos.
```
