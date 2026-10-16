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
/// FIN-DEV-P0: H-DEV-1/2/3 fueron CORREGIDOS; sus pruebas son ahora "Regresion_" (exigen el comportamiento correcto).
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

    // ============ FIN-DEV-P0: devolución vs. cancelación (H-DEV-1, H-DEV-2, H-DEV-3 CORREGIDOS) ============
    // Regla comercial aprobada por el dueño (solución CONSERVADORA, sin reembolsos parciales):
    //   1. Una venta con devoluciones Pendiente / EnRevision / Aprobada / Completada NO puede cancelarse.
    //   2. Una venta Cancelada NO admite devoluciones nuevas.
    //   3. Una devolución Aprobada NO puede completarse si su venta está Cancelada (incluye registros históricos).
    //   4. Toda operación rechazada deja la base intacta (estado, stock, unidades, caja, auditoría).
    // La concurrencia real (lock de la fila de Ventas compartido) se prueba contra SQL Server en
    // Concurrency/FinResidualSqlServerTests (R-06/R-07/R-08/R-11/R-12), no con SQLite.

    /// <summary>Foto de TODO lo que una operación rechazada no debe tocar.</summary>
    private sealed record FotoDevolucion(
        EstadoVenta EstadoVenta, string EstadosDevolucion, int MovimientosCaja, decimal Ingresos, decimal Egresos,
        decimal Stock, int MovimientosStock, string EstadosUnidades, int HistorialUnidades, int NotasCredito, int Devoluciones,
        string EstadosNotasCredito);

    private async Task<FotoDevolucion> FotoAsync(VentaContado v)
    {
        var (ing, egr, movs) = await LibroAsync(v.AperturaId);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estadoVenta = await ctx.Ventas.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == v.VentaId).Select(x => x.Estado).FirstAsync();
        var devs = await ctx.Devoluciones.AsNoTracking().Where(d => d.VentaId == v.VentaId && !d.IsDeleted)
            .OrderBy(d => d.Id).Select(d => d.Estado).ToListAsync();
        var unidades = await ctx.ProductoUnidades.AsNoTracking().Where(u => u.ProductoId == v.ProductoId)
            .OrderBy(u => u.Id).Select(u => new { u.Id, u.Estado }).ToListAsync();
        var unidadIds = unidades.Select(u => u.Id).ToList();
        return new FotoDevolucion(
            estadoVenta,
            string.Join(",", devs),
            movs, ing, egr,
            await ctx.Productos.AsNoTracking().Where(p => p.Id == v.ProductoId).Select(p => p.StockActual).FirstAsync(),
            await ctx.MovimientosStock.AsNoTracking().CountAsync(m => m.ProductoId == v.ProductoId),
            string.Join(",", unidades.Select(u => u.Estado)),
            await ctx.Set<ProductoUnidadMovimiento>().AsNoTracking().CountAsync(m => unidadIds.Contains(m.ProductoUnidadId)),
            await ctx.NotasCredito.AsNoTracking().CountAsync(n => n.Devolucion.VentaId == v.VentaId),
            devs.Count,
            string.Join(",", await ctx.NotasCredito.AsNoTracking().Where(n => n.Devolucion.VentaId == v.VentaId)
                .OrderBy(n => n.Id).Select(n => n.Estado + ":" + n.MontoTotal).ToListAsync()));
    }

    private async Task<string> RechazadaConAsync(Func<IServiceProvider, Task> operacion)
    {
        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => operacion(scope.ServiceProvider));
        return ex.Message;
    }

    private Task CancelarAsync(IServiceProvider sp, VentaContado v, string motivo = "QA FIN-DEV-P0") =>
        sp.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, motivo);

    private Task<Devolucion> CrearDevolucionPendienteAsync(IServiceProvider sp, VentaContado v, int cantidad, int? unidadId = null,
        TipoResolucionDevolucion tipo = TipoResolucionDevolucion.ReembolsoDinero) =>
        sp.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA",
                TipoResolucion = tipo, RegistrarEgresoCaja = tipo == TipoResolucionDevolucion.ReembolsoDinero
            },
            new List<DevolucionDetalle>
            {
                new() { ProductoId = v.ProductoId, Cantidad = cantidad, AccionRecomendada = AccionProducto.ReintegrarStock, ProductoUnidadId = unidadId }
            });

    /// <summary>Lleva una devolución nueva (1 unidad, reembolso en caja) al estado pedido por los medios reales del servicio.</summary>
    private async Task<int> DevolucionEnEstadoAsync(VentaContado v, EstadoDevolucion estado)
    {
        int id;
        byte[] rv;
        using (var scope = _factory.Services.CreateScope())
        {
            var dev = await CrearDevolucionPendienteAsync(scope.ServiceProvider, v, cantidad: 1);
            id = dev.Id;
            rv = dev.RowVersion;
        }

        switch (estado)
        {
            case EstadoDevolucion.Pendiente:
                break;
            case EstadoDevolucion.EnRevision:
                // No hay operación de servicio que lleve a EnRevision: se fija el estado como lo dejaría el flujo manual.
                using (var scope = _factory.Services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Devoluciones
                        .Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Estado, EstadoDevolucion.EnRevision));
                break;
            case EstadoDevolucion.Aprobada:
            case EstadoDevolucion.Completada:
                using (var scope = _factory.Services.CreateScope())
                {
                    var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
                    var aprobada = await svc.AprobarDevolucionAsync(id, "qa", rv);
                    if (estado == EstadoDevolucion.Completada)
                        await svc.CompletarDevolucionAsync(id, aprobada.RowVersion);
                }
                break;
            case EstadoDevolucion.Rechazada:
                using (var scope = _factory.Services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<IDevolucionService>().RechazarDevolucionAsync(id, "qa", rv);
                break;
            case EstadoDevolucion.Cancelada:
                using (var scope = _factory.Services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CancelarDevolucionAsync(id, "qa", rv);
                break;
        }
        return id;
    }

    /// <summary>Simula un registro histórico inconsistente: la venta quedó Cancelada con una devolución ya aprobada (antes de FIN-DEV-P0 se podía).</summary>
    private async Task ForzarVentaCanceladaHistoricaAsync(VentaContado v)
    {
        using var scope = _factory.Services.CreateScope();
        var n = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Ventas
            .Where(x => x.Id == v.VentaId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, EstadoVenta.Cancelada));
        Assert.Equal(1, n);
    }

    /// <summary>Una unidad física Vendida vinculada a la línea de la venta (para medir su estado ante rechazos).</summary>
    private async Task<int> SembrarUnidadVendidaAsync(VentaContado v)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var detalle = await ctx.VentaDetalles.FirstAsync(d => d.VentaId == v.VentaId);
        var unidad = new ProductoUnidad
        {
            ProductoId = v.ProductoId, CodigoInternoUnidad = "U-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Estado = EstadoUnidad.Vendida, VentaDetalleId = detalle.Id, ClienteId = v.ClienteId, FechaVenta = DateTime.UtcNow
        };
        ctx.ProductoUnidades.Add(unidad);
        await ctx.SaveChangesAsync();
        return unidad.Id;
    }

    /// <summary>
    /// H-DEV-1 (CORREGIDO, FIN-DEV-P0). Una venta Cancelada no admite devoluciones: se rechaza sin insertar
    /// cabecera ni detalles y sin tocar caja, stock, unidades ni auditoría.
    /// </summary>
    [Fact(DisplayName = "REGRESION [H-DEV-1]: no se puede crear una devolucion sobre una venta cancelada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-1")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "CORREGIDO")]
    public async Task Regresion_H_DEV_1_CrearDevolucion_SobreVentaCancelada_SeRechazaSinEfectos()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        using (var scope = _factory.Services.CreateScope())
            await CancelarAsync(scope.ServiceProvider, v, "cancelada");
        var antes = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Cancelada, antes.EstadoVenta);
        Assert.Equal(12m, antes.Stock);         // 10 + 2 reintegradas por la cancelación
        Assert.Equal(100m, antes.Egresos);      // reversión del ingreso

        var mensaje = await RechazadaConAsync(sp => CrearDevolucionPendienteAsync(sp, v, cantidad: 1));

        Assert.Contains("venta cancelada", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, await FotoAsync(v));
        Assert.Equal(0, (await FotoAsync(v)).Devoluciones);
        // Sin escrituras parciales: ninguna cabecera ni detalle de devolución para la venta (ni siquiera soft-deleted).
        using var check = _factory.Services.CreateScope();
        var ctx = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await ctx.Devoluciones.IgnoreQueryFilters().CountAsync(d => d.VentaId == v.VentaId));
        Assert.Equal(0, await ctx.DevolucionDetalles.IgnoreQueryFilters().CountAsync(d => d.Devolucion.VentaId == v.VentaId));
    }

    /// <summary>
    /// H-DEV-2 (CORREGIDO, FIN-DEV-P0). Cancelar una venta con una devolución ya reembolsada se rechaza: no se
    /// duplican el egreso de caja ni el stock y la venta sigue Confirmada.
    /// </summary>
    [Fact(DisplayName = "REGRESION [H-DEV-2]: no se puede cancelar una venta con una devolucion reembolsada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-2")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "CORREGIDO")]
    public async Task Regresion_H_DEV_2_CancelarVenta_TrasDevolucionReembolsada_SeRechazaSinDuplicarCajaNiStock()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        await DevolucionEnEstadoAsync(v, EstadoDevolucion.Completada);

        var antes = await FotoAsync(v);
        Assert.Equal(50m, antes.Egresos);   // reembolso de 1 unidad
        Assert.Equal(11m, antes.Stock);     // 10 + 1 devuelta
        Assert.Equal(EstadoVenta.Confirmada, antes.EstadoVenta);

        var mensaje = await RechazadaConAsync(sp => CancelarAsync(sp, v, "tras devolución"));
        Assert.Contains("devoluciones", mensaje, StringComparison.OrdinalIgnoreCase);

        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);
        Assert.Equal(50m, despues.Egresos);                  // sin el segundo egreso de 100
        Assert.True(despues.Egresos <= despues.Ingresos);
        Assert.Equal(11m, despues.Stock);                    // sin las 2 unidades reintegradas otra vez
        Assert.Equal(EstadoVenta.Confirmada, despues.EstadoVenta);
        Assert.Equal(EstadoDevolucion.Completada.ToString(), despues.EstadosDevolucion);

        // Reintento de la cancelación: mismo rechazo, mismos datos.
        await RechazadaConAsync(sp => CancelarAsync(sp, v, "reintento"));
        Assert.Equal(antes, await FotoAsync(v));
    }

    /// <summary>
    /// H-DEV-3 (CORREGIDO, FIN-DEV-P0). Completar una devolución aprobada sobre una venta Cancelada (registro
    /// histórico inconsistente: ya no se puede llegar a él por servicios) se rechaza antes de generar
    /// reintegro de stock, cambio de unidad o egreso de caja.
    /// </summary>
    [Fact(DisplayName = "REGRESION [H-DEV-3]: no se puede completar una devolucion aprobada sobre una venta cancelada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    [Trait("CausaRaiz", "CR-3")]
    [Trait("Estado", "CORREGIDO")]
    public async Task Regresion_H_DEV_3_CompletarDevolucionAprobada_SobreVentaCancelada_SeRechazaSinEfectos()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var unidadId = await SembrarUnidadVendidaAsync(v);
        int devId;
        byte[] rv;
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
            var dev = await CrearDevolucionPendienteAsync(scope.ServiceProvider, v, cantidad: 1, unidadId);
            var aprobada = await svc.AprobarDevolucionAsync(dev.Id, "qa", dev.RowVersion);
            devId = aprobada.Id;
            rv = aprobada.RowVersion;
        }

        await ForzarVentaCanceladaHistoricaAsync(v);
        var antes = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Cancelada, antes.EstadoVenta);
        Assert.Equal(EstadoDevolucion.Aprobada.ToString(), antes.EstadosDevolucion);
        Assert.Equal(EstadoUnidad.Vendida.ToString(), antes.EstadosUnidades);

        var mensaje = await RechazadaConAsync(sp =>
            sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));
        Assert.Contains("cancelada", mensaje, StringComparison.OrdinalIgnoreCase);

        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);
        Assert.Equal(EstadoDevolucion.Aprobada.ToString(), despues.EstadosDevolucion); // no quedó Completada a medias
        Assert.Equal(0m, despues.Egresos);
        Assert.Equal(10m, despues.Stock);

        // Reintento: mismo rechazo, mismos datos.
        await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));
        Assert.Equal(antes, await FotoAsync(v));
    }

    /// <summary>Simula un registro histórico inconsistente: la venta fue eliminada lógicamente con una devolución viva.</summary>
    private async Task ForzarVentaEliminadaHistoricaAsync(VentaContado v)
    {
        using var scope = _factory.Services.CreateScope();
        var n = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Ventas
            .Where(x => x.Id == v.VentaId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        Assert.Equal(1, n);
    }

    private async Task<(int id, byte[] rv)> DevolucionPendienteAsync(VentaContado v, TipoResolucionDevolucion tipo, int? unidadId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dev = await CrearDevolucionPendienteAsync(scope.ServiceProvider, v, cantidad: 1, unidadId, tipo);
        return (dev.Id, dev.RowVersion);
    }

    /// <summary>
    /// FIN-DEV-P0 (aprobación). Aprobar una devolución Pendiente histórica sobre una venta Cancelada se rechaza
    /// antes de cambiar el estado o emitir la nota de crédito. Sin escrituras parciales; el reintento rechaza igual.
    /// </summary>
    [Theory(DisplayName = "REGRESION [FIN-DEV-P0]: no se puede aprobar una devolucion historica sobre una venta cancelada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    [InlineData(TipoResolucionDevolucion.NotaCredito)]
    [InlineData(TipoResolucionDevolucion.ReembolsoDinero)]
    public async Task Regresion_AprobarDevolucionPendiente_SobreVentaCancelada_SeRechazaSinEfectos(TipoResolucionDevolucion tipo)
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var unidadId = await SembrarUnidadVendidaAsync(v);
        var (devId, rv) = await DevolucionPendienteAsync(v, tipo, unidadId);

        await ForzarVentaCanceladaHistoricaAsync(v);
        var antes = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Cancelada, antes.EstadoVenta);
        Assert.Equal(EstadoDevolucion.Pendiente.ToString(), antes.EstadosDevolucion);
        Assert.Equal(0, antes.NotasCredito);

        var mensaje = await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv));
        Assert.Contains("cancelada", mensaje, StringComparison.OrdinalIgnoreCase);

        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);                    // estado, NC, caja, stock, unidades y auditoría intactos
        Assert.Equal(0, despues.NotasCredito);           // ninguna nota de crédito vigente
        Assert.Equal(EstadoUnidad.Vendida.ToString(), despues.EstadosUnidades);

        using var check = _factory.Services.CreateScope();
        var ctx = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var dev = await ctx.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == devId);
        Assert.Null(dev.AprobadoPor);
        Assert.Null(dev.FechaAprobacion);
        Assert.False(dev.NotaCreditoGenerada);
        Assert.Null(dev.NotaCreditoId);
        Assert.Equal(0, await ctx.NotasCredito.IgnoreQueryFilters().CountAsync(n => n.DevolucionId == devId));

        // Reintento: mismo rechazo, mismos datos.
        await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv));
        Assert.Equal(antes, await FotoAsync(v));
    }

    /// <summary>Aprobar sobre una venta eliminada lógicamente se rechaza sin efectos.</summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: no se puede aprobar una devolucion sobre una venta eliminada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    public async Task Regresion_AprobarDevolucionPendiente_SobreVentaEliminada_SeRechazaSinEfectos()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var (devId, rv) = await DevolucionPendienteAsync(v, TipoResolucionDevolucion.NotaCredito);

        await ForzarVentaEliminadaHistoricaAsync(v);
        var antes = await FotoAsync(v);

        var mensaje = await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv));
        Assert.Contains("eliminada", mensaje, StringComparison.OrdinalIgnoreCase);

        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);
        Assert.Equal(EstadoDevolucion.Pendiente.ToString(), despues.EstadosDevolucion);
        Assert.Equal(0, despues.NotasCredito);
        using var check = _factory.Services.CreateScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<AppDbContext>().NotasCredito
            .IgnoreQueryFilters().CountAsync(n => n.DevolucionId == devId));
    }

    /// <summary>Completar una devolución aprobada sobre una venta eliminada lógicamente se rechaza sin efectos.</summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: no se puede completar una devolucion aprobada sobre una venta eliminada")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    public async Task Regresion_CompletarDevolucionAprobada_SobreVentaEliminada_SeRechazaSinEfectos()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var unidadId = await SembrarUnidadVendidaAsync(v);
        var (pendId, pendRv) = await DevolucionPendienteAsync(v, TipoResolucionDevolucion.ReembolsoDinero, unidadId);
        int devId;
        byte[] rv;
        using (var scope = _factory.Services.CreateScope())
        {
            var aprobada = await scope.ServiceProvider.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(pendId, "qa", pendRv);
            devId = aprobada.Id;
            rv = aprobada.RowVersion;
        }

        await ForzarVentaEliminadaHistoricaAsync(v);
        var antes = await FotoAsync(v);
        Assert.Equal(EstadoDevolucion.Aprobada.ToString(), antes.EstadosDevolucion);

        var mensaje = await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));
        Assert.Contains("eliminada", mensaje, StringComparison.OrdinalIgnoreCase);

        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);
        Assert.Equal(EstadoDevolucion.Aprobada.ToString(), despues.EstadosDevolucion);
        Assert.Equal(0m, despues.Egresos);
        Assert.Equal(10m, despues.Stock);
        Assert.Equal(EstadoUnidad.Vendida.ToString(), despues.EstadosUnidades);

        await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));
        Assert.Equal(antes, await FotoAsync(v));
    }

    /// <summary>Aprobación válida emite UNA nota de crédito vigente; su reintento se rechaza y NO la duplica.</summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: reintento de aprobacion no duplica la nota de credito")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    public async Task Regresion_AprobarDevolucion_Valida_Y_Reintento_NoDuplicaNotaCredito()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var (devId, rv) = await DevolucionPendienteAsync(v, TipoResolucionDevolucion.NotaCredito);

        using (var scope = _factory.Services.CreateScope())
        {
            var aprobada = await scope.ServiceProvider.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv);
            Assert.Equal(EstadoDevolucion.Aprobada, aprobada.Estado);
            Assert.True(aprobada.NotaCreditoGenerada);
        }
        var antes = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, antes.EstadoVenta);
        Assert.Equal(1, antes.NotasCredito);
        using (var check = _factory.Services.CreateScope())
            Assert.Equal(EstadoNotaCredito.Vigente, await check.ServiceProvider.GetRequiredService<AppDbContext>().NotasCredito
                .AsNoTracking().Where(n => n.DevolucionId == devId).Select(n => n.Estado).SingleAsync());

        await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv));

        Assert.Equal(antes, await FotoAsync(v));
        Assert.Equal(1, (await FotoAsync(v)).NotasCredito);
    }

    /// <summary>
    /// Rollback de la aprobación: una nota de crédito histórica soft-deleted con la misma DevolucionId choca contra el
    /// índice único de la relación 1:1 al insertar la nueva. Ya con el lock tomado y el estado modificado en memoria, el
    /// error debe dejar la devolución Pendiente, sin segunda nota y sin tocar caja/stock.
    /// </summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: error durante la aprobacion revierte todo")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    public async Task Regresion_AprobarDevolucion_ErrorDeInsercion_RevierteTodo()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var (devId, rv) = await DevolucionPendienteAsync(v, TipoResolucionDevolucion.NotaCredito);
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ctx.NotasCredito.Add(new NotaCredito
            {
                DevolucionId = devId, ClienteId = v.ClienteId, NumeroNotaCredito = "NC-HIST-" + devId,
                FechaEmision = DateTime.UtcNow, MontoTotal = 1m, Estado = EstadoNotaCredito.Cancelada, IsDeleted = true
            });
            await ctx.SaveChangesAsync();
        }
        var antes = await FotoAsync(v);

        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAnyAsync<Exception>(() =>
                scope.ServiceProvider.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(devId, "qa", rv));

        Assert.Equal(antes, await FotoAsync(v));
        using var check = _factory.Services.CreateScope();
        var ctx2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var dev = await ctx2.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == devId);
        Assert.Equal(EstadoDevolucion.Pendiente, dev.Estado);
        Assert.Null(dev.AprobadoPor);
        Assert.False(dev.NotaCreditoGenerada);
        Assert.Equal(1, await ctx2.NotasCredito.IgnoreQueryFilters().CountAsync(n => n.DevolucionId == devId)); // solo la histórica
    }

    /// <summary>Matriz: cualquier devolución viva (Pendiente, EnRevision, Aprobada, Completada) impide cancelar la venta.</summary>
    [Theory(DisplayName = "REGRESION [FIN-DEV-P0]: cancelar una venta con una devolucion viva se rechaza sin efectos")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-2")]
    [InlineData(EstadoDevolucion.Pendiente)]
    [InlineData(EstadoDevolucion.EnRevision)]
    [InlineData(EstadoDevolucion.Aprobada)]
    [InlineData(EstadoDevolucion.Completada)]
    public async Task Regresion_CancelarVenta_ConDevolucionViva_SeRechazaSinEfectos(EstadoDevolucion estado)
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        await DevolucionEnEstadoAsync(v, estado);
        var antes = await FotoAsync(v);

        var mensaje = await RechazadaConAsync(sp => CancelarAsync(sp, v));

        Assert.Contains("devoluciones", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, await FotoAsync(v));
        Assert.Equal(EstadoVenta.Confirmada, (await FotoAsync(v)).EstadoVenta);
    }

    /// <summary>Las devoluciones Rechazadas / Canceladas son históricas y no bloquean: no tuvieron efectos de stock ni caja.</summary>
    [Theory(DisplayName = "REGRESION [FIN-DEV-P0]: una devolucion rechazada o cancelada sin efectos no impide cancelar la venta")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-2")]
    [InlineData(EstadoDevolucion.Rechazada)]
    [InlineData(EstadoDevolucion.Cancelada)]
    public async Task Regresion_CancelarVenta_ConDevolucionHistoricaSinEfectos_EsPermitida(EstadoDevolucion estado)
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        await DevolucionEnEstadoAsync(v, estado);
        var antes = await FotoAsync(v);
        // Premisa de la regla: la devolución histórica no movió stock ni caja.
        Assert.Equal(10m, antes.Stock);
        Assert.Equal(0m, antes.Egresos);

        using (var scope = _factory.Services.CreateScope())
            await CancelarAsync(scope.ServiceProvider, v);

        var despues = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Cancelada, despues.EstadoVenta);
        Assert.Equal(100m, despues.Egresos);    // reversión íntegra: nunca hubo reembolso
        Assert.Equal(12m, despues.Stock);       // 10 + 2 vendidas
        Assert.Equal(estado.ToString(), despues.EstadosDevolucion);
    }

    /// <summary>Camino feliz intacto: devolución válida sobre venta activa se crea, aprueba y completa normalmente.</summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: devolucion valida sobre venta activa se crea, aprueba y completa")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-1")]
    public async Task Regresion_DevolucionValidaSobreVentaActiva_SeCreaApruebaYCompleta()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        await DevolucionEnEstadoAsync(v, EstadoDevolucion.Completada);

        var fin = await FotoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, fin.EstadoVenta);
        Assert.Equal(EstadoDevolucion.Completada.ToString(), fin.EstadosDevolucion);
        Assert.Equal(50m, fin.Egresos);
        Assert.Equal(11m, fin.Stock);
    }

    /// <summary>Reintento de cancelar una venta ya cancelada: se rechaza y no duplica contramovimiento ni stock.</summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: reintento de cancelacion sobre venta ya cancelada no duplica caja ni stock")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-2")]
    public async Task Regresion_ReintentoDeCancelacion_NoDuplicaCajaNiStock()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        using (var scope = _factory.Services.CreateScope())
            await CancelarAsync(scope.ServiceProvider, v);
        var antes = await FotoAsync(v);

        await RechazadaConAsync(sp => CancelarAsync(sp, v, "reintento"));

        Assert.Equal(antes, await FotoAsync(v));
    }

    /// <summary>
    /// Rollback ante error de caja: completar con un reembolso inválido falla DESPUÉS del reintegro de stock y de
    /// marcar la devolución Completada; todo debe deshacerse (la devolución sigue Aprobada y reintentable).
    /// </summary>
    [Fact(DisplayName = "REGRESION [FIN-DEV-P0]: error al registrar el reembolso revierte stock, unidades y estado de la devolucion")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-DEV-3")]
    public async Task Regresion_CompletarDevolucion_ErrorDeCaja_RevierteTodo()
    {
        var v = await SembrarVentaContadoAsync(stockInicial: 10m);
        var unidadId = await SembrarUnidadVendidaAsync(v);
        int devId;
        byte[] rv;
        using (var scope = _factory.Services.CreateScope())
        {
            var dev = await CrearDevolucionPendienteAsync(scope.ServiceProvider, v, cantidad: 1, unidadId);
            var aprobada = await scope.ServiceProvider.GetRequiredService<IDevolucionService>()
                .AprobarDevolucionAsync(dev.Id, "qa", dev.RowVersion);
            devId = aprobada.Id;
            rv = aprobada.RowVersion;
        }

        // Fuerza un error en el ÚLTIMO paso (el reembolso rechaza montos <= 0), cuando el reintegro de stock, el
        // cambio de unidad y el estado Completada ya se escribieron dentro de la transacción.
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Devoluciones.Where(d => d.Id == devId).ExecuteUpdateAsync(s => s.SetProperty(d => d.TotalDevolucion, 0m));
            rv = await ctx.Devoluciones.AsNoTracking().Where(d => d.Id == devId).Select(d => d.RowVersion).FirstAsync();
        }
        var antes = await FotoAsync(v);

        var mensaje = await RechazadaConAsync(sp => sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(devId, rv));

        Assert.Contains("reembolso", mensaje, StringComparison.OrdinalIgnoreCase);
        var despues = await FotoAsync(v);
        Assert.Equal(antes, despues);
        Assert.Equal(EstadoDevolucion.Aprobada.ToString(), despues.EstadosDevolucion);
        Assert.Equal(10m, despues.Stock);
        Assert.Equal(EstadoUnidad.Vendida.ToString(), despues.EstadosUnidades);
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
