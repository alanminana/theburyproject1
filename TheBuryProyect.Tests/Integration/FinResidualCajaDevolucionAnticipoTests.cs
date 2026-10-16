using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-FIN-RESIDUAL — pruebas AISLADAS (SQLite, secuenciales, servicios reales vía DI) de las reglas
/// financieras residuales que NO necesitan SQL Server: cierre de caja, devolución, anticipo, reintentos.
///
/// Son pruebas de CARACTERIZACIÓN: las marcadas "Hallazgo_" fijan el comportamiento ACTUAL, que es defectuoso
/// respecto del invariante indicado en cada una. Pasan hoy (= el defecto está CONFIRMADO por ejecución) y
/// DEBEN fallar cuando se corrija el servicio; en ese momento se invierte la aserción (cada una trae el
/// invariante deseado en su comentario). Las marcadas "Contrato_" fijan comportamiento correcto que debe preservarse.
/// No se modificó ningún servicio de producción. Ver docs/qa-fin-residual-auditoria.md.
///
/// La concurrencia real (interleavings) vive en Concurrency/FinResidualSqlServerTests (SQL Server, BLOCKED
/// hasta que exista un turno exclusivo del stack FIN).
/// </summary>
[Collection("HttpIntegration")]
public sealed class FinResidualCajaDevolucionAnticipoTests : IClassFixture<CustomWebApplicationFactory>
{
    // Sin HttpContext el usuario actual es "Sistema" (CurrentUserService).
    private const string Usuario = "Sistema";
    private readonly CustomWebApplicationFactory _factory;

    public FinResidualCajaDevolucionAnticipoTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ------------------------------------------------------------------ siembra

    private sealed record VentaContado(int VentaId, string Numero, int ClienteId, int ProductoId, int AperturaId);

    /// <summary>Venta de contado Confirmada: 2 × $50 = $100, ingreso de caja VentaEfectivo $100 en la apertura dada.</summary>
    private async Task<VentaContado> SembrarVentaContadoAsync(int? aperturaId = null, decimal stockInicial = 10m)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = aperturaId ?? await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
        var sufijo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var cliente = new Cliente
        {
            Nombre = "Fin", Apellido = "Residual" + sufijo, TipoDocumento = "DNI", NumeroDocumento = sufijo,
            Email = $"{sufijo}@test.com", Activo = true, Telefono = "0000000000", Domicilio = "QA 1"
        };
        var cat = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(cliente, cat, marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 50m, PorcentajeIVA = 21m, StockActual = stockInicial, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = "VFR" + sufijo,
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            AperturaCajaId = apertura,
            Subtotal = 100m,
            Total = 100m,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 100m,
                    CostoUnitarioAlMomento = 17.5m, CostoTotalAlMomento = 35m
                }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();

        ctx.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = apertura,
            Tipo = TipoMovimientoCaja.Ingreso,
            Concepto = ConceptoMovimientoCaja.VentaEfectivo,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado,
            TipoPago = TipoPago.Efectivo,
            Monto = 100m,
            Descripcion = "Venta " + venta.Numero,
            Referencia = venta.Numero,
            ReferenciaId = venta.Id,
            VentaId = venta.Id,
            Usuario = Usuario,
            FechaMovimiento = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return new VentaContado(venta.Id, venta.Numero, cliente.Id, producto.Id, apertura);
    }

    private async Task CerrarAperturaAsync(int aperturaId, decimal efectivoContado)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICajaService>().CerrarCajaAsync(
            new CerrarCajaViewModel { AperturaCajaId = aperturaId, EfectivoContado = efectivoContado }, Usuario);
    }

    private async Task<(decimal ingresos, decimal egresos, int movimientos)> LibroAsync(int aperturaId)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movs = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted).ToListAsync();
        return (movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto),
                movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto),
                movs.Count);
    }

    private async Task<decimal> StockAsync(int productoId)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.Productos.AsNoTracking().Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync();
    }

    // ================================================================== 1. Cierre de caja y movimientos posteriores

    /// <summary>
    /// H-CAJA-2 (CONFIRMADO). Invariante deseado: una apertura CERRADA es inmutable (su libro coincide con el
    /// snapshot de CierreCaja). Hoy el contramovimiento de una cancelación se asienta en la apertura del
    /// ingreso original aunque ya esté cerrada: el libro de la caja cerrada pasa a divergir de su cierre.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-CAJA-2   Causa raíz: CR-1   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): Caja: el libro de una apertura cerrada deja de coincidir con su CierreCaja (egresos 100 vs 0 congelado). Dinero: difícil de detectar.
    // Comportamiento defectuoso que reproduce: CancelarVentaAsync asienta la reversión en la apertura del ingreso original aunque esté Cerrada.
    // Comportamiento esperado que exigirá la futura regresión: Una apertura cerrada es inmutable: la reversión se asienta en la apertura activa de quien cancela (con referencia a la original) o la cancelación se rechaza. REGLA COMERCIAL A APROBAR.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-CAJA-2]: contramovimiento de cancelacion cae en una caja ya cerrada")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-CAJA-2")]
    [Trait("CausaRaiz", "CR-1")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CancelarVenta_ContramovimientoCaeEnAperturaYaCerrada_Y_DivergeDelCierre()
    {
        var v = await SembrarVentaContadoAsync();
        await CerrarAperturaAsync(v.AperturaId, efectivoContado: 100m); // arqueo exacto: sin diferencia

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "post-cierre"));

        using var check = _factory.Services.CreateScope();
        var ctx = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ctx.AperturasCaja.AsNoTracking().Include(a => a.Cierre).FirstAsync(a => a.Id == v.AperturaId);
        var (ingresos, egresos, _) = await LibroAsync(v.AperturaId);

        Assert.True(apertura.Cerrada);
        // Defecto: el egreso de reversión (100) quedó DENTRO de la apertura ya cerrada...
        Assert.Equal(100m, egresos);
        // ...y el cierre congelado dice que no hubo egresos: libro ≠ cierre.
        Assert.Equal(0m, apertura.Cierre!.TotalEgresosSistema);
        Assert.Equal(100m, apertura.Cierre.TotalIngresosSistema);
        Assert.NotEqual(apertura.Cierre.TotalEgresosSistema, egresos);
        _ = ingresos;
    }

    /// <summary>
    /// H-CAJA-1 (CONFIRMADO, parte secuencial). RegistrarMovimientoVentaAsync resuelve la apertura por
    /// Venta.AperturaCajaId y NO verifica Cerrada/vencida: asienta un ingreso en una apertura cerrada.
    /// Es la misma consecuencia que "cobro que entra mientras se cierra la caja", sin necesitar carrera.
    /// Invariante deseado: rechazar (o redirigir a la apertura activa) si la apertura está cerrada.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-CAJA-1   Causa raíz: CR-1   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): Caja: ingreso posterior al cierre; el arqueo ya no refleja el libro. La carrera real con el cierre (R-01) sigue SIN ejecutar.
    // Comportamiento defectuoso que reproduce: RegistrarMovimientoVentaAsync resuelve la apertura por Venta.AperturaCajaId y no valida Cerrada ni vencida.
    // Comportamiento esperado que exigirá la futura regresión: Todo asiento exige apertura abierta y vigente, verificada bajo el mismo lock que el cierre; si está cerrada se rechaza o se redirige.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-CAJA-1]: ingreso de venta asentado en una caja cerrada")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-CAJA-1")]
    [Trait("CausaRaiz", "CR-1")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_RegistrarMovimientoVenta_AsientaIngresoEnAperturaCerrada()
    {
        var v = await SembrarVentaContadoAsync();
        await CerrarAperturaAsync(v.AperturaId, efectivoContado: 100m);
        var antes = await LibroAsync(v.AperturaId);

        MovimientoCaja? mov;
        using (var scope = _factory.Services.CreateScope())
            mov = await scope.ServiceProvider.GetRequiredService<ICajaService>()
                .RegistrarMovimientoVentaAsync(v.VentaId, v.Numero, 40m, TipoPago.Efectivo, Usuario);

        Assert.NotNull(mov); // aceptado: no hay guardia de apertura cerrada
        var despues = await LibroAsync(v.AperturaId);
        Assert.Equal(antes.ingresos + 40m, despues.ingresos);
    }

    /// <summary>
    /// Contraste (PASS): el alta MANUAL sí rechaza la apertura cerrada; la guardia existe pero solo en ese camino.
    /// </summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_RegistrarMovimientoManual_RechazaAperturaCerrada]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_RegistrarMovimientoManual_RechazaAperturaCerrada()
    {
        var v = await SembrarVentaContadoAsync();
        await CerrarAperturaAsync(v.AperturaId, efectivoContado: 100m);

        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICajaService>().RegistrarMovimientoAsync(
                new MovimientoCajaViewModel
                {
                    AperturaCajaId = v.AperturaId, Tipo = TipoMovimientoCaja.Ingreso,
                    Concepto = ConceptoMovimientoCaja.AjusteCaja, Descripcion = "x", Monto = 5m
                }, Usuario));
        Assert.Contains("cerrada", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Contrato (PASS): el segundo cierre secuencial de la misma apertura se rechaza con mensaje de negocio.</summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_CerrarCaja_SegundoCierreSecuencial_SeRechaza]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_CerrarCaja_SegundoCierreSecuencial_SeRechaza()
    {
        var v = await SembrarVentaContadoAsync();
        await CerrarAperturaAsync(v.AperturaId, efectivoContado: 100m);

        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ICajaService>().CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = v.AperturaId, EfectivoContado = 100m }, Usuario));
        Assert.Contains("ya está cerrada", ex.Message);
    }

    /// <summary>
    /// H-CAJA-3 (CONFIRMADO, decisión de negocio). El cierre no mira movimientos con acreditación Pendiente
    /// (transferencia/tarjeta/MP): se pueden acreditar DESPUÉS del cierre y el saldo real de la apertura
    /// cerrada cambia. Invariante a decidir: ¿cierre bloqueado/advertido con pendientes, o acreditar a posteriori es legítimo?
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-CAJA-3   Causa raíz: CR-2   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): Caja: el saldo real de una apertura cerrada cambia tras el arqueo (+70).
    // Comportamiento defectuoso que reproduce: CerrarCajaAsync ignora movimientos con acreditación Pendiente y AcreditarMovimientoAsync los acepta con la apertura cerrada.
    // Comportamiento esperado que exigirá la futura regresión: A DEFINIR POR NEGOCIO: o el cierre advierte/bloquea con pendientes, o acreditar a posteriori es legítimo y debe quedar auditado.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-CAJA-3]: acreditacion de un pendiente despues del cierre")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-CAJA-3")]
    [Trait("CausaRaiz", "CR-2")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CierreConMovimientoPendienteDeAcreditar_YAcreditacionPosteriorCambiaElSaldoRealDeLaApertura()
    {
        var v = await SembrarVentaContadoAsync();
        int pendienteId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = new MovimientoCaja
            {
                AperturaCajaId = v.AperturaId, Tipo = TipoMovimientoCaja.Ingreso,
                Concepto = ConceptoMovimientoCaja.VentaTransferencia,
                EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, TipoPago = TipoPago.Transferencia,
                Monto = 70m, Descripcion = "Transferencia pendiente", Usuario = Usuario, FechaMovimiento = DateTime.UtcNow
            };
            ctx.MovimientosCaja.Add(m);
            await ctx.SaveChangesAsync();
            pendienteId = m.Id;
        }

        await CerrarAperturaAsync(v.AperturaId, efectivoContado: 100m); // la transferencia no es efectivo: el cierre no la considera

        decimal antes, despues;
        using (var scope = _factory.Services.CreateScope())
        {
            var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
            antes = await caja.CalcularSaldoRealAsync(v.AperturaId);
            await caja.AcreditarMovimientoAsync(pendienteId, Usuario); // aceptado con la apertura ya cerrada
            despues = await caja.CalcularSaldoRealAsync(v.AperturaId);
        }

        Assert.Equal(antes + 70m, despues);
    }

    // ================================================================== 2. Devolución vs cancelación

    private async Task<(int devolucionId, byte[] rowVersion)> CrearYAprobarDevolucionAsync(VentaContado v, int cantidad, bool reembolsoEnCaja)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
        var dev = await svc.CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA",
                TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero, RegistrarEgresoCaja = reembolsoEnCaja
            },
            new List<DevolucionDetalle>
            {
                new() { ProductoId = v.ProductoId, Cantidad = cantidad, AccionRecomendada = AccionProducto.ReintegrarStock }
            });
        var aprobada = await svc.AprobarDevolucionAsync(dev.Id, "qa", dev.RowVersion);
        return (aprobada.Id, aprobada.RowVersion);
    }

    /// <summary>
    /// H-DEV-1 (CONFIRMADO). Invariante deseado: no se puede devolver una venta Cancelada (ya devolvió stock y
    /// revirtió el ingreso). CrearDevolucionAsync solo verifica que la venta exista y no esté borrada.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-DEV-1   Causa raíz: CR-3   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): Stock/caja: abre la puerta a H-DEV-3 (reembolso de una venta ya revertida).
    // Comportamiento defectuoso que reproduce: CrearDevolucionAsync solo verifica que la venta exista y no esté borrada; no mira Venta.Estado.
    // Comportamiento esperado que exigirá la futura regresión: No se crea ni se completa una devolución sobre una venta Cancelada (invariante I-D2).
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-DEV-1]: devolucion creada sobre una venta cancelada")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-DEV-1")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CrearDevolucion_SobreVentaYaCancelada_SeAcepta()
    {
        var v = await SembrarVentaContadoAsync();
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "cancelada"));

        using var scope2 = _factory.Services.CreateScope();
        var dev = await scope2.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
            new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA" },
            new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = 1 } });

        Assert.Equal(EstadoDevolucion.Pendiente, dev.Estado);
    }

    /// <summary>
    /// H-DEV-2 (CONFIRMADO, orden Devolución → Cancelación). Invariante deseado: el dinero que sale de caja por
    /// una venta nunca supera lo que ingresó por ella (Σ egresos(venta) ≤ Σ ingresos(venta)) y el stock devuelto
    /// nunca supera lo vendido. Hoy CancelarVentaAsync no consulta devoluciones: contra-asienta el 100 % del
    /// ingreso encima del reembolso ya pagado (doble egreso) y vuelve a reintegrar las 2 unidades (doble stock).
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-DEV-2   Causa raíz: CR-3   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): DINERO Y STOCK: egreso 150 sobre 100 ingresados; stock 13 contra 12 (se inventa 1 unidad).
    // Comportamiento defectuoso que reproduce: CancelarVentaAsync no consulta devoluciones: contra-asienta el 100% del ingreso y reintegra todo el stock otra vez.
    // Comportamiento esperado que exigirá la futura regresión: Se rechaza cancelar con devoluciones vivas, o la reversión descuenta lo ya reembolsado. REGLA COMERCIAL A APROBAR.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-DEV-2]: cancelar tras una devolucion reembolsada duplica caja y stock")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-DEV-2")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CancelarVenta_TrasDevolucionReembolsada_DuplicaEgresoDeCajaYStock()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);

        // El reembolso necesita caja abierta del usuario actual: es la misma apertura sembrada.
        var (devId, rv) = await CrearYAprobarDevolucionAsync(v, cantidad: 1, reembolsoEnCaja: true);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv);

        var tras = await LibroAsync(v.AperturaId);
        Assert.Equal(50m, tras.egresos);                       // reembolso de 1 unidad
        Assert.Equal(11m, await StockAsync(v.ProductoId));     // 10 + 1 devuelta

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "tras devolución"));

        var fin = await LibroAsync(v.AperturaId);
        // Defecto: egresos = 50 (reembolso) + 100 (reversión total) = 150 > 100 ingresados por la venta.
        Assert.Equal(150m, fin.egresos);
        Assert.True(fin.egresos > fin.ingresos, "El egreso total superó al ingreso de la venta: caja neta negativa.");
        // Defecto: stock = 10 + 1 (devolución) + 2 (cancelación) = 13 > 12 (10 + 2 vendidas): se inventaron 1 unidad.
        Assert.Equal(13m, await StockAsync(v.ProductoId));
    }

    /// <summary>
    /// H-DEV-3 (CONFIRMADO, orden Cancelación → Completar devolución previa). Una devolución aprobada ANTES de la
    /// cancelación se puede completar DESPUÉS: reembolsa en efectivo una venta cuyo ingreso ya fue revertido.
    /// Invariante deseado: CompletarDevolucionAsync rechaza si la venta está Cancelada.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-DEV-3   Causa raíz: CR-3   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): DINERO Y STOCK: egreso 200 sobre 100 ingresados; stock 14 contra 12.
    // Comportamiento defectuoso que reproduce: CompletarDevolucionAsync solo exige Estado = Aprobada; no mira Venta.Estado.
    // Comportamiento esperado que exigirá la futura regresión: CompletarDevolucionAsync rechaza si la venta está Cancelada (invariante I-D2).
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-DEV-3]: completar una devolucion aprobada sobre una venta cancelada")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-DEV-3")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CompletarDevolucionAprobada_SobreVentaCancelada_ReembolsaDeNuevo()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var (devId, rv) = await CrearYAprobarDevolucionAsync(v, cantidad: 2, reembolsoEnCaja: true);

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "antes de completar"));
        var trasCancelar = await LibroAsync(v.AperturaId);
        Assert.Equal(100m, trasCancelar.egresos); // reversión de la venta

        using (var scope = _factory.Services.CreateScope())
        {
            var dev = await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv);
            Assert.Equal(EstadoDevolucion.Completada, dev.Estado); // aceptado
        }

        var fin = await LibroAsync(v.AperturaId);
        Assert.Equal(200m, fin.egresos); // reversión 100 + reembolso 100 sobre 100 ingresados
        Assert.Equal(14m, await StockAsync(v.ProductoId)); // 10 + 2 (cancelación) + 2 (devolución)
    }

    /// <summary>
    /// Contrato (PASS): la cantidad devuelta acumulada no supera lo vendido (guardia secuencial). La versión
    /// concurrente (dos altas simultáneas leen el mismo "ya devuelto") está en el harness SQL Server.
    /// </summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_CrearDevolucion_SecuencialNoPuedeExcederLoVendido]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_CrearDevolucion_SecuencialNoPuedeExcederLoVendido()
    {
        var v = await SembrarVentaContadoAsync();
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
                new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "1" },
                new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = 2 } });

        using var scope2 = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope2.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
                new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "2" },
                new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = 1 } }));
        Assert.Contains("excede", ex.Message);
    }

    /// <summary>Contrato (PASS): completar dos veces la misma devolución no reembolsa dos veces (estado + RowVersion).</summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_CompletarDevolucion_Reintento_NoDuplicaReembolso]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_CompletarDevolucion_Reintento_NoDuplicaReembolso()
    {
        var v = await SembrarVentaContadoAsync();
        var (devId, rv) = await CrearYAprobarDevolucionAsync(v, cantidad: 1, reembolsoEnCaja: true);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv);

        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));

        Assert.Equal(50m, (await LibroAsync(v.AperturaId)).egresos);
    }

    // ================================================================== 3. Anticipos

    /// <summary>
    /// H-ANT-1 (CONFIRMADO). Invariante deseado: al cancelar una venta a crédito, su anticipo cobrado en caja se
    /// revierte (o la cancelación se rechaza igual que con cuotas cobradas). El anticipo se asienta como
    /// AnticipoCredito con ReferenciaId = CreditoId y SIN VentaId; RegistrarContramovimientoVentaAsync solo revierte
    /// los conceptos Venta* y AsegurarSinPagosAplicadosParaCancelarAsync solo mira CobroCuota: el anticipo queda
    /// cobrado en una venta cancelada, sin ninguna señal.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-ANT-1   Causa raíz: CR-4   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): DINERO/CAJA: el anticipo sigue en caja sin contramovimiento ni devolución al cliente, sin ninguna señal.
    // Comportamiento defectuoso que reproduce: El anticipo se asienta como AnticipoCredito con ReferenciaId = CreditoId y sin VentaId; la cancelación solo revierte conceptos Venta* y solo mira CobroCuota.
    // Comportamiento esperado que exigirá la futura regresión: El anticipo se asienta con VentaId y la cancelación lo trata como pago aplicado (rechazo por defecto) o lo revierte. REGLA COMERCIAL A APROBAR.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-ANT-1]: anticipo cobrado queda en una venta cancelada")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-ANT-1")]
    [Trait("CausaRaiz", "CR-4")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_CancelarVentaACredito_NoRevierteNiBloqueaPorElAnticipoCobrado()
    {
        EscenarioSembrado s;
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            aperturaId = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
            s = await ConcSeed.SembrarAsync(ctx, aperturaId, conIngresoDeVenta: false, usuarioMovimiento: Usuario);
        }

        MovimientoCaja? anticipo;
        using (var scope = _factory.Services.CreateScope())
            anticipo = await scope.ServiceProvider.GetRequiredService<ICajaService>()
                .RegistrarMovimientoAnticipoAsync(s.CreditoId, "CR-ANT", 150m, Usuario);
        Assert.NotNull(anticipo);
        Assert.Null(anticipo!.VentaId); // sin vínculo directo con la venta

        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(s.VentaId, "con anticipo"));

        var libro = await LibroAsync(aperturaId);
        Assert.Equal(150m, libro.ingresos); // el anticipo sigue en caja
        Assert.Equal(0m, libro.egresos);    // sin contramovimiento ni devolución al cliente
        using var check = _factory.Services.CreateScope();
        var ctx2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(EstadoVenta.Cancelada, (await ctx2.Ventas.AsNoTracking().FirstAsync(v => v.Id == s.VentaId)).Estado);
    }

    /// <summary>
    /// H-ANT-2 (CONFIRMADO). Invariante deseado: un anticipo > 0 que no pudo asentarse hace fallar la confirmación
    /// (como RegistrarMovimientoCuotaAsync, cuyo llamador convierte el null en excepción). Hoy el servicio devuelve
    /// null en silencio y ConfirmarVentaCreditoAsync ignora el resultado: la venta se confirma sin ingreso del anticipo.
    /// Aquí se prueba el servicio (usuario sin caja abierta); la ruta de carrera está en el harness SQL Server.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-ANT-2   Causa raíz: CR-4   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): DINERO/CAJA: la venta a crédito se confirma sin ingreso del anticipo.
    // Comportamiento defectuoso que reproduce: RegistrarMovimientoAnticipoAsync devuelve null sin lanzar y ConfirmarVentaCreditoAsync ignora el resultado.
    // Comportamiento esperado que exigirá la futura regresión: Con anticipo > 0 y sin caja el servicio falla (fail-closed), igual que el cobro de cuota.
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-ANT-2]: anticipo sin caja abierta devuelve null en silencio")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-ANT-2")]
    [Trait("CausaRaiz", "CR-4")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_RegistrarMovimientoAnticipo_SinCajaAbierta_DevuelveNullSinFallar()
    {
        using var scope = _factory.Services.CreateScope();
        var r = await scope.ServiceProvider.GetRequiredService<ICajaService>()
            .RegistrarMovimientoAnticipoAsync(creditoId: 1, "CR-X", 100m, "usuario-sin-caja-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Null(r);
    }

    // ================================================================== 4. Reintentos / idempotencia

    /// <summary>
    /// H-IDEM-1 (CONFIRMADO). Los movimientos manuales no tienen clave de idempotencia: un doble POST
    /// (reintento HTTP, doble clic) asienta el movimiento dos veces. Invariante deseado: mismo token ⇒ un solo asiento.
    /// </summary>
    // =========================== REGISTRO DEL DEFECTO (QA-FIN-RESIDUAL) ===========================
    // ID: H-IDEM-1   Causa raíz: CR-5   Estado: ABIERTO   PR correctiva: PENDIENTE
    // ESTE TEST PASA PORQUE REPRODUCE UN DEFECTO VIGENTE. Verde NO significa corregido.
    // Impacto (dinero/caja/stock): CAJA: un doble clic o reintento HTTP asienta dos veces (egreso 50 en vez de 25).
    // Comportamiento defectuoso que reproduce: MovimientoCaja no tiene clave de idempotencia.
    // Comportamiento esperado que exigirá la futura regresión: Mismo token de idempotencia produce un solo asiento (IdempotencyKey con índice único filtrado).
    // Al corregir: invertir las aserciones, quitar el trait DefectoConocido y renombrar a Regresion_*.
    // ===============================================================================================
    [Fact(DisplayName = "DEFECTO CONOCIDO ABIERTO [H-IDEM-1]: reintento de movimiento manual duplica el asiento")]
    [Trait("Categoria", "DefectoConocido")]
    [Trait("Hallazgo", "H-IDEM-1")]
    [Trait("CausaRaiz", "CR-5")]
    [Trait("Estado", "ABIERTO")]
    public async Task Hallazgo_RegistrarMovimientoManual_ReintentoIdenticoDuplicaElAsiento()
    {
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
            aperturaId = await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), Usuario);

        var modelo = new MovimientoCajaViewModel
        {
            AperturaCajaId = aperturaId, Tipo = TipoMovimientoCaja.Egreso, Concepto = ConceptoMovimientoCaja.GastoOperativo,
            Descripcion = "Reintento", Monto = 25m
        };
        for (var i = 0; i < 2; i++)
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICajaService>().RegistrarMovimientoAsync(modelo, Usuario);
        }

        var libro = await LibroAsync(aperturaId);
        Assert.Equal(2, libro.movimientos);
        Assert.Equal(50m, libro.egresos);
    }

    /// <summary>
    /// Contrato (PASS): el cobro de cuotas usa la RowVersion como token de idempotencia: el reintento del MISMO
    /// request (mismo RowVersion) tras confirmarse el primero se rechaza y no duplica pago ni asiento de caja.
    /// </summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_PagoMultipleDeCuotas_ReintentoConLaMismaRowVersion_SeRechazaSinDuplicar]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_PagoMultipleDeCuotas_ReintentoConLaMismaRowVersion_SeRechazaSinDuplicar()
    {
        EscenarioSembrado s;
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            aperturaId = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
            s = await ConcSeed.SembrarAsync(ctx, aperturaId, conIngresoDeVenta: false, usuarioMovimiento: Usuario);
        }

        PagoMultipleCuotasRequest Request(Cuota c) => new()
        {
            ClienteId = s.ClienteId,
            CuotaIds = [c.Id],
            RowVersionsPorCuota = new Dictionary<int, string> { [c.Id] = Convert.ToBase64String(c.RowVersion) },
            MedioPago = "Efectivo"
        };

        Cuota cuota;
        using (var scope = _factory.Services.CreateScope())
            cuota = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[0]);
        var request = Request(cuota);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotasAsync(request);

        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() =>
                scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotasAsync(request));

        using var check = _factory.Services.CreateScope();
        var ctx2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await ctx2.PagosCuota.CountAsync(p => p.Cuota.CreditoId == s.CreditoId));
        Assert.Equal(1, await ctx2.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.ReferenciaId == s.CuotaIds[0]));
    }

    /// <summary>Contrato (PASS): cancelar dos veces la misma venta (reintento) no duplica contramovimiento ni stock.</summary>
    [Fact(DisplayName = "CONTRATO VALIDO (comportamiento correcto a preservar) [Contrato_CancelarVenta_Reintento_NoDuplicaContramovimientoNiStock]")]
    [Trait("Categoria", "ContratoValido")]
    public async Task Contrato_CancelarVenta_Reintento_NoDuplicaContramovimientoNiStock()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "1"));
        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "2"));

        Assert.Equal(100m, (await LibroAsync(v.AperturaId)).egresos);
        Assert.Equal(12m, await StockAsync(v.ProductoId));
    }
}
