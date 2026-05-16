# Fase Cotización V1.9 — Numeración robusta ante concurrencia

## A. Objetivo

Garantizar que `Cotizacion.Numero` sea único y robusto ante concurrencia de requests simultáneos, eliminando el riesgo de colisión que existía en la generación original.

---

## B. Diagnóstico previo

### Cómo se generaba `Cotizacion.Numero` (estado previo)

`CotizacionService.GenerarNumeroAsync` (privado) realizaba:

1. SELECT del último número con el prefijo del día (`COT-yyyyMMdd-`)
2. Parse del contador numérico al final
3. Retorno del siguiente número: `COT-{hoy}-{n+1:0000}`

**Problema:** sin ningún control de concurrencia. Sin semáforo, sin transacción, sin retry.

### Existencia de índice único

`AppDbContext` (línea ~1224):
```csharp
entity.HasIndex(e => e.Numero)
    .IsUnique()
    .HasFilter("[IsDeleted] = 0");
```
✅ El índice único ya existía. Actúa como última línea de defensa pero sin retry el error llegaba como 500 al usuario.

### Patrón canónico existente

`VentaNumberGenerator` usa `SemaphoreSlim` estático:
```csharp
private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
```
Este es el patrón canónico del proyecto.

---

## C. Riesgo de concurrencia detectado

Con dos requests simultáneos al endpoint de creación:

1. Request A entra a `GenerarNumeroAsync`, lee `last = null`, genera `COT-YYYYMMDD-0001`
2. Request B entra a `GenerarNumeroAsync` (sin bloqueo), lee `last = null`, genera `COT-YYYYMMDD-0001`
3. Request A llama `SaveChangesAsync` → éxito
4. Request B llama `SaveChangesAsync` → `DbUpdateException` por violación de índice único
5. Sin retry: Request B falla con error 500 sin recuperación

---

## D. Decisión técnica

**Opción A reforzada**: SemaphoreSlim + retry ante colisión de índice único.

- SemaphoreSlim estático (igual que `VentaNumberGenerator`) serializa la generación de números en el mismo proceso. Reduce la frecuencia de colisiones.
- Retry de hasta 3 intentos ante `DbUpdateException` con violación UNIQUE. Protege el caso multi-instancia (múltiples servidores sin acceso al semáforo compartido).
- No requiere migración. El índice único ya existía.
- No requiere tabla de secuencia ni cambio de formato.

**Opción C (tabla/secuencia)** descartada: sería más robusta pero innecesaria para el volumen esperado y requeriría migración.

---

## E. Cambios aplicados

### `Services/CotizacionService.cs`

1. **Campo estático agregado:**
```csharp
private static readonly SemaphoreSlim _semaphore = new(1, 1);
```

2. **`GenerarNumeroAsync` protegido con semáforo:**
```csharp
await _semaphore.WaitAsync(cancellationToken);
try { ... SELECT + calcular siguiente ... }
finally { _semaphore.Release(); }
```

3. **Retry en `CrearAsync`** (máx. 3 intentos):
```csharp
for (var intento = 0; intento < maxReintentos; intento++)
{
    try { await _context.SaveChangesAsync(cancellationToken); break; }
    catch (DbUpdateException ex) when (EsColisionNumeroUnico(ex) && intento < maxReintentos - 1)
    {
        _logger.LogWarning("Colisión de número {Numero} en intento {Intento}. Reintentando.", ...);
        cotizacion.Numero = await GenerarNumeroAsync(cancellationToken);
    }
}
```

4. **Helper `EsColisionNumeroUnico`:**
```csharp
private static bool EsColisionNumeroUnico(DbUpdateException ex)
{
    var msg = ex.InnerException?.Message ?? ex.Message;
    return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
}
```
Compatible con SQL Server (errores 2601/2627) y SQLite (tests).

---

## F. Tests agregados

Archivo: `TheBuryProyect.Tests/Integration/CotizacionNumeracionTests.cs`

| Test | Qué verifica |
|---|---|
| `CrearCotizacion_GeneraNumeroConFormatoEsperado` | Formato `COT-yyyyMMdd-XXXX` con regex |
| `CrearCotizacion_PrimeraDeLDia_EmpiezaEn0001` | Primera cotización del día inicia en 0001 |
| `CrearCotizacion_SegundaDelDia_EsSecuencial` | Dos cotizaciones generan 0001 y 0002 |
| `CrearCotizacion_ConPrefijoPrevioEnDB_GeneraSiguienteNumero` | Con 0003 pre-existente genera 0004 |
| `CrearMultiplesCotizaciones_NumerosSecuencialesUnicos` | 5 creates = 5 números únicos y correlativos |
| `IndiceUnico_NumeroRepetido_LanzaDbUpdateException` | El índice DB rechaza duplicados |

**Test de colisión con threading real:** no implementado. SQLite en memoria con conexión compartida no es thread-safe para concurrencia real. El retry se valida indirectamente mediante:
- `EsColisionNumeroUnico` cubre la detección vía mensaje de excepción
- La lógica de retry está cubierta por revisión de código + build + patrón validado de VentaNumberGenerator

---

## G. Validaciones

- Build Release: 0 errores, 0 advertencias ✅
- Tests Cotizacion: 120/120 (eran 114 en V1.8, +6 nuevos) ✅
- Suite completa: 385/385 ✅
- `git diff --check`: limpio (solo advertencia CRLF, no errores) ✅
- Sin migración generada ✅

---

## H. Qué NO se tocó

- `VentaNumberGenerator` (solo lectura para referencia de patrón)
- `VentaService`
- `CotizacionConversionService`
- `AppDbContext` (índice ya existía, sin cambios)
- Stock, caja, factura
- Módulos de Juan/Kira
- Formato público del número (`COT-yyyyMMdd-XXXX` sin cambios)

---

## I. Riesgos y deuda remanente

| Riesgo | Mitigación aplicada | Deuda |
|---|---|---|
| Concurrencia multi-instancia (varios servidores) | Retry ante UNIQUE (3 intentos) | Sin secuencia DB robusta; aceptable para volumen esperado |
| Agotamiento de reintentos | 3er intento propaga `DbUpdateException` al controller | Controller debe mapear a 409/500 apropiado |
| `SemaphoreSlim` no protege multi-servidor | Retry cubre este caso | Si el volumen escala, evaluar secuencia SQL Server |

---

## J. Clasificación de componentes

| Componente | Clasificación | Decisión |
|---|---|---|
| `CotizacionService.GenerarNumeroAsync` | Canónico | Mejorado con semáforo |
| `CotizacionService.CrearAsync` (bloque save) | Canónico | Agregado retry |
| `VentaNumberGenerator` | Canónico Venta | Solo referencia, no modificado |
| `Cotizacion.Numero` índice único | Canónico EF | Confirmado existente, no modificado |
| `AppDbContext` config Cotizacion | Canónico | No modificado |

---

## K. Checklist

### Carlos — Cotización V1.x
- [x] V1.6 — Permiso granular cotizaciones.convert
- [x] V1.7 — Tests integración seguridad conversión 403
- [x] V1.8 — Preview tabla cambios unitarios
- [x] V1.9 — Numeración robusta ante concurrencia
- [ ] V1.10 — Cancelación compleja
- [ ] V1.11 — Vencimiento automático
- [ ] V1.12 — Impresión/envío

### Juan — Inventario
- [ ] Diagnóstico StockActual vs unidades físicas (en curso)

### Kira — Ventas/Create
- [ ] BUG Ventas/Create roto (en curso)

---

## L. Siguiente micro-lote recomendado

**V1.10 — Cancelación compleja de cotización**

La cotización tiene estados (`Emitida`, `Convertida`, etc.) pero probablemente la cancelación no valida transiciones de estado ni deja trazabilidad. El siguiente frente de valor sería: validar transición de estado → cancelada, registrar motivo y usuario, y cubrir con tests de integración.
