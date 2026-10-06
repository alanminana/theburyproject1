using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ReporteService : IReporteService
    {
        #region Constructor y dependencias

        // Umbrales de margen para clasificación de productos
        // Tramos de antigüedad de mora (días)
        private const int TramoMora30Dias = 30;
        private const int TramoMora60Dias = 60;
        private const int TramoMora90Dias = 90;

        // Ventas que cuentan como operación comercial real en los reportes de ventas/márgenes.
        // Excluye cotizaciones, presupuestos, pendientes de requisitos/financiación y canceladas
        // (mismo criterio que el reporte de comisiones).
        private static readonly EstadoVenta[] EstadosVentaComerciales =
        {
            EstadoVenta.Confirmada,
            EstadoVenta.Facturada,
            EstadoVenta.Entregada
        };

        private readonly AppDbContext _context;
        private readonly ILogger<ReporteService> _logger;
        private readonly IConfiguracionRentabilidadService? _configuracionRentabilidadService;
        private readonly IRelojComercial _reloj;
        private static readonly object QuestPdfSettingsLock = new();
        private static bool questPdfSettingsConfigured;

        public ReporteService(
            AppDbContext context,
            ILogger<ReporteService> logger,
            IConfiguracionRentabilidadService? configuracionRentabilidadService = null,
            IRelojComercial? reloj = null)
        {
            _context = context;
            _logger = logger;
            _configuracionRentabilidadService = configuracionRentabilidadService;
            // PUN-ML7: fuente única de "hoy" para vencimiento de cuotas. La inyección obligatoria
            // sería preferible, pero ReporteServiceTests/ReporteMorosidadTests (fuera del alcance de
            // esta corrección) construyen este servicio sin pasar reloj — el fallback es inerte en
            // producción (Program.cs registra IRelojComercial como Singleton; DI siempre lo resuelve)
            // y solo se alcanza ahí.
            _reloj = reloj ?? RelojComercial.Sistema;
        }

        #endregion

        #region Generación de reportes

        private static void ConfigurarQuestPdf()
        {
            lock (QuestPdfSettingsLock)
            {
                if (questPdfSettingsConfigured)
                    return;

                QuestPDF.Settings.License = LicenseType.Community;
                QuestPDF.Settings.UseEnvironmentFonts = false;
                QuestPDF.Settings.FontDiscoveryPaths.Clear();
                questPdfSettingsConfigured = true;
            }
        }

        public async Task<ReporteVentasResultadoViewModel> GenerarReporteVentasAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                var query = _context.Ventas
                    .AsNoTracking()
                    .Include(v => v.Cliente)
                    .Include(v => v.VendedorUser)
                    .Include(v => v.DatosTarjeta)
                    .Include(v => v.Detalles.Where(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted))
                        .ThenInclude(d => d.Producto)
                            .ThenInclude(p => p!.Categoria)
                    .Where(v =>
                        !v.IsDeleted &&
                        EstadosVentaComerciales.Contains(v.Estado) &&
                        (v.Cliente == null || !v.Cliente.IsDeleted))
                    .AsQueryable();

                // Aplicar filtros. "Hasta" es un día completo (inclusivo), no medianoche.
                if (filtro.FechaDesde.HasValue)
                    query = query.Where(v => v.FechaVenta >= filtro.FechaDesde.Value);

                if (filtro.FechaHasta.HasValue)
                {
                    var hastaExclusivo = filtro.FechaHasta.Value.Date.AddDays(1);
                    query = query.Where(v => v.FechaVenta < hastaExclusivo);
                }

                if (filtro.ClienteId.HasValue)
                    query = query.Where(v => v.ClienteId == filtro.ClienteId.Value);

                if (filtro.TipoPago.HasValue)
                    query = query.Where(v => v.TipoPago == filtro.TipoPago.Value);

                if (!string.IsNullOrWhiteSpace(filtro.VendedorId))
                    query = query.Where(v => v.VendedorUserId == filtro.VendedorId);


                if (filtro.ProductoId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.ProductoId == filtro.ProductoId.Value &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted));

                if (filtro.CategoriaId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted &&
                        d.Producto.CategoriaId == filtro.CategoriaId.Value));

                if (filtro.MarcaId.HasValue)
                    query = query.Where(v => v.Detalles.Any(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted &&
                        d.Producto.MarcaId == filtro.MarcaId.Value));

                var ventas = await query.OrderByDescending(v => v.FechaVenta).ToListAsync();

                // Mapear a ViewModels
                var ventasItems = ventas.Select(v =>
                {
                    var detallesValidos = v.Detalles
                        .Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)
                        .ToList();

                    var costo = detallesValidos.Sum(CalcularCostoTotalDetalle);
                    var recargoDebitoAplicado = ResolverRecargoDebitoAplicado(v);
                    var totalProductos = v.Total - recargoDebitoAplicado;
                    var ganancia = totalProductos - costo;

                    return new VentaReporteItemViewModel
                    {
                        Id = v.Id,
                        NumeroVenta = v.Numero,
                        FechaVenta = v.FechaVenta,
                        // Cliente.NombreCompleto es un campo propio (nunca lo completa el alta estándar de
                        // Cliente/CreateAjax); usarlo acá dejaba el reporte con "Anónimo" para clientes reales.
                        // Se arma Apellido, Nombre directo (sin ToDisplayName(), que agrega "- DNI: ..." y
                        // duplicaba el documento en otras vistas — ver ficha del cliente en Venta/Index).
                        ClienteNombre = v.Cliente != null ? $"{v.Cliente.Apellido}, {v.Cliente.Nombre}" : v.NombreClienteLibre ?? "Anónimo",
                        VendedorNombre = !string.IsNullOrWhiteSpace(v.VendedorNombre)
                            ? v.VendedorNombre
                            : v.VendedorUser?.UserName ?? "Sin asignar",
                        TipoPago = v.TipoPago,
                        Subtotal = v.Subtotal,
                        Descuento = v.Descuento,
                        IVA = v.IVA,
                        RecargoDebitoAplicado = recargoDebitoAplicado,
                        Total = v.Total,
                        Costo = costo,
                        Ganancia = ganancia,
                        MargenPorcentaje = CalcularMargenPorcentaje(ganancia, totalProductos),
                        CantidadProductos = detallesValidos.Sum(d => (int)d.Cantidad)
                    };
                }).ToList();

                // Calcular estadísticas
                var totalVentas = ventasItems.Sum(v => v.Total);
                var totalRecargoDebito = ventasItems.Sum(v => v.RecargoDebitoAplicado);
                var totalCosto = ventasItems.Sum(v => v.Costo);
                var totalProductos = ventasItems.Sum(v => v.TotalProductos);
                var totalGanancia = ventasItems.Sum(v => v.Ganancia);
                var margenPromedio = CalcularMargenPorcentaje(totalGanancia, totalProductos);

                // Ventas por tipo de pago
                var ventasPorTipoPago = ventasItems
                    .GroupBy(v => v.TipoPago)
                    .ToDictionary(
                        g => g.Key.ToString(),
                        g => g.Sum(v => v.Total)
                    );

                // Productos más vendidos
                var productosMasVendidos = ObtenerProductosMasVendidos(ventas, filtro);

                // Clientes top
                var clientesTop = ObtenerClientesTop(ventas);
                var productosMenosVendidos = ObtenerProductosMasVendidos(ventas, filtro, menosVendidos: true);
                var clientesFrecuentes = ObtenerClientesTop(ventas, porFrecuencia: true);

                return new ReporteVentasResultadoViewModel
                {
                    Ventas = ventasItems,
                    TotalVentas = totalVentas,
                    TotalRecargoDebito = totalRecargoDebito,
                    TotalCosto = totalCosto,
                    TotalGanancia = totalGanancia,
                    MargenPromedio = margenPromedio,
                    CantidadVentas = ventasItems.Count,
                    CantidadProductosVendidos = ventasItems.Sum(v => v.CantidadProductos),
                    TicketPromedio = ventasItems.Any() ? totalVentas / ventasItems.Count : 0,
                    VentasPorTipoPago = ventasPorTipoPago,
                    ProductosMasVendidos = productosMasVendidos,
                    ClientesTop = clientesTop,
                    ProductosMenosVendidos = productosMenosVendidos,
                    ClientesFrecuentes = clientesFrecuentes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de ventas");
                throw;
            }
        }

        public async Task<ReporteIvaResultadoViewModel> GenerarReporteIvaAsync(ReporteIvaFiltroViewModel filtro)
        {
            try
            {
                var resultado = new ReporteIvaResultadoViewModel { Filtro = filtro };

                DateTime? desde = filtro.FechaDesde?.Date;
                DateTime? hastaExclusivo = filtro.FechaHasta?.Date.AddDays(1);

                if (filtro.IncluyeVentas)
                {
                    // Fuente: snapshot histórico de VentaDetalle (alícuota, neto e IVA al momento
                    // de la operación). Una venta genera un solo registro de IVA aunque se cobre
                    // en varios movimientos de Caja. Solo Facturada/Entregada tienen factura real
                    // detrás y discriminan IVA (mismo criterio que CajaConciliacionBuilder.DiscriminaIva);
                    // una Confirmada sin facturar es una venta informal que no declara IVA, y se
                    // excluye igual que borradores/cotizaciones y canceladas.
                    var ventasQuery = _context.VentaDetalles
                        .AsNoTracking()
                        .Where(d =>
                            !d.IsDeleted &&
                            !d.Venta.IsDeleted &&
                            (d.Venta.Estado == EstadoVenta.Facturada ||
                             d.Venta.Estado == EstadoVenta.Entregada));

                    if (desde.HasValue)
                        ventasQuery = ventasQuery.Where(d => d.Venta.FechaVenta >= desde.Value);
                    if (hastaExclusivo.HasValue)
                        ventasQuery = ventasQuery.Where(d => d.Venta.FechaVenta < hastaExclusivo.Value);
                    if (filtro.Alicuota.HasValue)
                        ventasQuery = ventasQuery.Where(d => d.PorcentajeIVA == filtro.Alicuota.Value);

                    // Agregación en memoria: SQLite (tests) no traduce Sum de decimal en el servidor.
                    var ventaRows = await ventasQuery
                        .Select(d => new
                        {
                            d.VentaId,
                            d.PorcentajeIVA,
                            d.SubtotalFinalNeto,
                            d.SubtotalFinalIVA,
                            d.SubtotalFinal
                        })
                        .ToListAsync();

                    // Líneas legacy sin snapshot de desglose (neto e IVA en cero con importe
                    // facturado): se excluyen de los buckets y se informan aparte. Una línea
                    // exenta legítima (0%) tiene neto = total, por lo que no cae acá.
                    var ventasSinDesglose = ventaRows
                        .Where(d => d.SubtotalFinalNeto == 0m && d.SubtotalFinalIVA == 0m && d.SubtotalFinal > 0m)
                        .ToList();
                    var ventasConDesglose = ventaRows
                        .Where(d => !(d.SubtotalFinalNeto == 0m && d.SubtotalFinalIVA == 0m && d.SubtotalFinal > 0m))
                        .ToList();

                    resultado.VentasSinDesglose = ventasSinDesglose.Select(d => d.VentaId).Distinct().Count();
                    resultado.VentasSinDesgloseTotal = ventasSinDesglose.Sum(d => d.SubtotalFinal);

                    resultado.VentasPorAlicuota = ventasConDesglose
                        .GroupBy(d => d.PorcentajeIVA)
                        .Select(g => new ReporteIvaAlicuotaItemViewModel
                        {
                            Porcentaje = g.Key,
                            Operaciones = g.Select(d => d.VentaId).Distinct().Count(),
                            Neto = g.Sum(d => d.SubtotalFinalNeto),
                            Iva = g.Sum(d => d.SubtotalFinalIVA),
                            Total = g.Sum(d => d.SubtotalFinal)
                        })
                        .OrderByDescending(x => x.Total)
                        .ToList();

                    resultado.VentasNeto = resultado.VentasPorAlicuota.Sum(x => x.Neto);
                    resultado.VentasIva = resultado.VentasPorAlicuota.Sum(x => x.Iva);
                    resultado.VentasTotal = resultado.VentasPorAlicuota.Sum(x => x.Total);
                    resultado.VentasCantidad = ventasConDesglose.Select(r => r.VentaId).Distinct().Count();
                }

                if (filtro.IncluyeCompras)
                {
                    // Fuente: snapshot por línea de OrdenCompraDetalle (alícuota e IVA incluido al
                    // momento de la operación). Órdenes legacy sin desglose por línea se agrupan a
                    // la alícuota general de cabecera (21%, la única aplicada en ese momento).
                    var comprasQuery = _context.OrdenesCompra
                        .AsNoTracking()
                        .Where(o =>
                            !o.IsDeleted &&
                            (o.Estado == EstadoOrdenCompra.Confirmada ||
                             o.Estado == EstadoOrdenCompra.EnTransito ||
                             o.Estado == EstadoOrdenCompra.Recibida));

                    if (desde.HasValue)
                        comprasQuery = comprasQuery.Where(o => o.FechaEmision >= desde.Value);
                    if (hastaExclusivo.HasValue)
                        comprasQuery = comprasQuery.Where(o => o.FechaEmision < hastaExclusivo.Value);

                    // Agregación en memoria: SQLite (tests) no traduce Sum de decimal en el servidor.
                    var compraRows = await comprasQuery
                        .Select(o => new
                        {
                            o.Id,
                            o.Iva,
                            o.Total,
                            Detalles = o.Detalles
                                .Where(d => !d.IsDeleted)
                                .Select(d => new { d.Subtotal, d.PorcentajeIVA, d.IvaImporte })
                                .ToList()
                        })
                        .ToListAsync();

                    var buckets = new List<(decimal Porcentaje, int OrdenId, decimal Neto, decimal Iva, decimal Total)>();

                    foreach (var orden in compraRows)
                    {
                        var tieneDesglosePorLinea = orden.Detalles.Any(d => d.PorcentajeIVA > 0m || d.IvaImporte > 0m);

                        if (tieneDesglosePorLinea)
                        {
                            var subtotalLineas = orden.Detalles.Sum(d => d.Subtotal);
                            var factorDescuento = subtotalLineas > 0 ? orden.Total / subtotalLineas : 0m;

                            foreach (var grupo in orden.Detalles.GroupBy(d => d.PorcentajeIVA))
                            {
                                var total = Math.Round(grupo.Sum(d => d.Subtotal) * factorDescuento, 2, MidpointRounding.AwayFromZero);
                                var iva = grupo.Sum(d => d.IvaImporte);
                                buckets.Add((grupo.Key, orden.Id, total - iva, iva, total));
                            }
                        }
                        else if (orden.Iva > 0m)
                        {
                            // Legacy: IVA de cabecera registrado a la alícuota general (21%)
                            // al momento de la operación. No se reconstruye por producto.
                            resultado.ComprasSinDesglosePorLinea++;
                            buckets.Add((21m, orden.Id, orden.Total - orden.Iva, orden.Iva, orden.Total));
                        }
                        else
                        {
                            // Sin IVA registrado: exenta o legacy en cero.
                            buckets.Add((0m, orden.Id, orden.Total, 0m, orden.Total));
                        }
                    }

                    if (filtro.Alicuota.HasValue)
                        buckets = buckets.Where(b => b.Porcentaje == filtro.Alicuota.Value).ToList();

                    resultado.ComprasPorAlicuota = buckets
                        .GroupBy(b => b.Porcentaje)
                        .Select(g => new ReporteIvaAlicuotaItemViewModel
                        {
                            Porcentaje = g.Key,
                            Operaciones = g.Select(b => b.OrdenId).Distinct().Count(),
                            Neto = g.Sum(b => b.Neto),
                            Iva = g.Sum(b => b.Iva),
                            Total = g.Sum(b => b.Total)
                        })
                        .OrderByDescending(x => x.Total)
                        .ToList();

                    resultado.ComprasNeto = resultado.ComprasPorAlicuota.Sum(x => x.Neto);
                    resultado.ComprasIva = resultado.ComprasPorAlicuota.Sum(x => x.Iva);
                    resultado.ComprasTotal = resultado.ComprasPorAlicuota.Sum(x => x.Total);
                    resultado.ComprasCantidad = buckets.Select(b => b.OrdenId).Distinct().Count();
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de IVA");
                throw;
            }
        }

        public async Task<ReporteMargenesViewModel> GenerarReporteMargenesAsync(int? categoriaId = null, int? marcaId = null)
        {
            try
            {
                var configRentabilidad = _configuracionRentabilidadService != null
                    ? await _configuracionRentabilidadService.GetConfiguracionAsync()
                    : new ConfiguracionRentabilidad();

                var query = _context.Productos
                    .Include(p => p.Categoria)
                    .Include(p => p.Marca)
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted)
                    .AsQueryable();

                if (categoriaId.HasValue)
                    query = query.Where(p => p.CategoriaId == categoriaId.Value);

                if (marcaId.HasValue)
                    query = query.Where(p => p.MarcaId == marcaId.Value);

                var productos = await query.ToListAsync();

                // Obtener ventas de los últimos 30 días para cada producto
                var hace30Dias = DateTime.UtcNow.AddDays(-30);
                var ventasRecientes = await _context.VentaDetalles
                    .AsNoTracking()
                    .Include(vd => vd.Venta)
                    .Where(vd => !vd.IsDeleted &&
                                 vd.Venta != null &&
                                 !vd.Venta.IsDeleted &&
                                 EstadosVentaComerciales.Contains(vd.Venta.Estado) &&
                                 vd.Venta.FechaVenta >= hace30Dias)
                    .GroupBy(vd => vd.ProductoId)
                    .Select(g => new
                    {
                        ProductoId = g.Key,
                        CantidadVendida = g.Sum(vd => vd.Cantidad)
                    })
                    .ToListAsync();

                var ventasPorProducto = ventasRecientes.ToDictionary(v => v.ProductoId, v => (int)v.CantidadVendida);

                var productosMargen = productos.Select(p =>
                {
                    var ganancia = p.PrecioVenta - p.PrecioCompra;
                    var margenPorcentaje = CalcularMargenPorcentaje(ganancia, p.PrecioVenta);

                    var ventasUltimos30Dias = ventasPorProducto.GetValueOrDefault(p.Id, 0);
                    var rotacionMensual = p.StockActual > 0
                        ? (ventasUltimos30Dias / p.StockActual) * 100
                        : 0;

                    return new ProductoMargenViewModel
                    {
                        Id = p.Id,
                        Codigo = p.Codigo,
                        Nombre = p.Nombre,
                        CategoriaNombre = p.Categoria?.Nombre ?? "",
                        MarcaNombre = p.Marca?.Nombre,
                        PrecioCompra = p.PrecioCompra,
                        PrecioVenta = p.PrecioVenta,
                        Ganancia = ganancia,
                        MargenPorcentaje = margenPorcentaje,
                        StockActual = p.StockActual,
                        GananciaPotencial = ganancia * p.StockActual,
                        VentasUltimos30Dias = ventasUltimos30Dias,
                        RotacionMensual = rotacionMensual,
                        MargenBajoMax = configRentabilidad.MargenBajoMax,
                        MargenAltoMin = configRentabilidad.MargenAltoMin
                    };
                }).ToList();

                return new ReporteMargenesViewModel
                {
                    Productos = productosMargen.OrderByDescending(p => p.GananciaPotencial).ToList(),
                    MargenPromedioGeneral = productosMargen.Any()
                        ? productosMargen.Average(p => p.MargenPorcentaje)
                        : 0,
                    GananciaTotalPotencial = productosMargen.Sum(p => p.GananciaPotencial),
                    ProductosConMargenBajo = productosMargen.Count(p => p.MargenPorcentaje < configRentabilidad.MargenBajoMax),
                    ProductosConMargenAlto = productosMargen.Count(p => p.MargenPorcentaje >= configRentabilidad.MargenAltoMin),
                    MargenBajoMax = configRentabilidad.MargenBajoMax,
                    MargenAltoMin = configRentabilidad.MargenAltoMin
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de márgenes");
                throw;
            }
        }

        public async Task<ReporteMorosidadViewModel> GenerarReporteMorosidadAsync()
        {
            try
            {
                // PUN-ML7: fecha comercial única (antes DateTime.UtcNow.Date — adelantaba
                // vencimiento hasta 3hs por el cruce de día UTC/Argentina, ver IRelojComercial).
                var hoy = _reloj.InicioDiaComercial;

                // Cuotas con saldo de capital/interés pendiente. El job ActualizarEstadoCuotasAsync pasa
                // Pendiente→Vencida y los pagos parciales quedan en Parcial: filtrar solo por Pendiente
                // hacía desaparecer del reporte justo las cuotas más atrasadas. La deuda es el saldo
                // (MontoTotal - MontoPagado), no el valor original de la cuota; el punitorio es un
                // concepto aparte y no se suma acá.
                var cuotasConSaldo = await _context.Cuotas
                    .AsNoTracking()
                    .Include(c => c.Credito)
                        .ThenInclude(cr => cr.Cliente)
                    .Where(c => !c.IsDeleted
                             && !c.Credito.IsDeleted
                             && !c.Credito.Cliente.IsDeleted
                             && (c.Estado == EstadoCuota.Pendiente
                                 || c.Estado == EstadoCuota.Vencida
                                 || c.Estado == EstadoCuota.Parcial)
                             && c.MontoPagado < c.MontoTotal)
                    .ToListAsync();

                var cuotasVencidas = cuotasConSaldo
                    .Where(c => c.FechaVencimiento < hoy)
                    .ToList();

                var deudaVigenteDict = cuotasConSaldo
                    .Where(c => c.FechaVencimiento >= hoy)
                    .GroupBy(c => c.Credito.ClienteId)
                    .ToDictionary(g => g.Key, g => g.Sum(CalcularSaldoCuota));

                // Agrupar por cliente
                var clientesMorosos = cuotasVencidas
                    .GroupBy(c => c.Credito.ClienteId)
                    .Select(g =>
                    {
                        var cuotaMasAntigua = g.OrderBy(c => c.FechaVencimiento).First();
                        var cliente = cuotaMasAntigua.Credito.Cliente;
                        var diasMaxAtraso = (hoy - cuotaMasAntigua.FechaVencimiento).Days;

                        return new ClienteMorosoViewModel
                        {
                            ClienteId = g.Key,
                            // Apellido, Nombre sin "- DNI:": el documento ya tiene su propia columna.
                            ClienteNombre = $"{cliente.Apellido}, {cliente.Nombre}",
                            ClienteDocumento = cliente.NumeroDocumento,
                            ClienteTelefono = cliente.Telefono,
                            CantidadCreditosVencidos = g.Select(c => c.CreditoId).Distinct().Count(),
                            TotalDeudaVencida = g.Sum(CalcularSaldoCuota),
                            TotalDeudaVigente = deudaVigenteDict.GetValueOrDefault(g.Key, 0m),
                            FechaPrimerVencimiento = cuotaMasAntigua.FechaVencimiento,
                            DiasMaximoAtraso = diasMaxAtraso,
                            MontoCuotaVencidaMasAntigua = CalcularSaldoCuota(cuotaMasAntigua),
                            CreditoIdMasAntiguo = cuotaMasAntigua.CreditoId
                        };
                    })
                    .OrderByDescending(c => c.DiasMaximoAtraso)
                    .ThenByDescending(c => c.TotalDeudaVencida)
                    .ToList();

                // Antigüedad por cuota (no por cliente): una cuota de 10 días no pasa a ">90" porque
                // el mismo cliente tenga otra de 100.
                decimal DeudaDesde(int dias) => cuotasVencidas
                    .Where(c => (hoy - c.FechaVencimiento).Days >= dias)
                    .Sum(CalcularSaldoCuota);

                var totalDeudaVencida = clientesMorosos.Sum(c => c.TotalDeudaVencida);
                var totalDeudaVigente = clientesMorosos.Sum(c => c.TotalDeudaVigente);

                return new ReporteMorosidadViewModel
                {
                    ClientesMorosos = clientesMorosos,
                    TotalDeudaVencida = totalDeudaVencida,
                    TotalDeudaVigente = totalDeudaVigente,
                    CantidadClientesMorosos = clientesMorosos.Count,
                    CantidadCreditosVencidos = clientesMorosos.Sum(c => c.CantidadCreditosVencidos),
                    PromedioDeudaPorCliente = clientesMorosos.Any()
                        ? totalDeudaVencida / clientesMorosos.Count
                        : 0,
                    DeudaMayor30Dias = DeudaDesde(TramoMora30Dias),
                    DeudaMayor60Dias = DeudaDesde(TramoMora60Dias),
                    DeudaMayor90Dias = DeudaDesde(TramoMora90Dias)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de morosidad");
                throw;
            }
        }

        #endregion

        #region Consultas y agregaciones

        public async Task<List<VentasAgrupadasViewModel>> ObtenerVentasAgrupadasAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            string agruparPor)
        {
            try
            {
                // "Hasta" es un día completo (inclusivo).
                var hastaExclusivo = fechaHasta.Date.AddDays(1);

                var ventas = await _context.Ventas
                    .AsNoTracking()
                    .Include(v => v.Detalles.Where(d =>
                        !d.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted))
                        .ThenInclude(d => d.Producto)
                            .ThenInclude(p => p.Categoria)
                    .Where(v =>
                        !v.IsDeleted &&
                        EstadosVentaComerciales.Contains(v.Estado) &&
                        v.FechaVenta >= fechaDesde &&
                        v.FechaVenta < hastaExclusivo)
                    .Include(v => v.DatosTarjeta)
                    .ToListAsync();

                return agruparPor?.ToLower() switch
                {
                    "dia" => ventas
                        .GroupBy(v => v.FechaVenta.Date)
                        .OrderBy(g => g.Key)
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = g.Key.ToString("dd/MM/yyyy"),
                            Monto = g.Sum(v => v.Total),
                            Cantidad = g.Count(),
                            Ganancia = g.Sum(v => (v.Total - ResolverRecargoDebitoAplicado(v)) - CalcularCostoDetalles(v.Detalles))
                        })
                        .ToList(),

                    "mes" => ventas
                        .GroupBy(v => new { v.FechaVenta.Year, v.FechaVenta.Month })
                        .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = $"{g.Key.Month:D2}/{g.Key.Year}",
                            Monto = g.Sum(v => v.Total),
                            Cantidad = g.Count(),
                            Ganancia = g.Sum(v => (v.Total - ResolverRecargoDebitoAplicado(v)) - CalcularCostoDetalles(v.Detalles))
                        })
                        .ToList(),

                    "categoria" => ventas
                        .SelectMany(v => v.Detalles)
                        .Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)
                        .GroupBy(d => d.Producto.Categoria.Nombre)
                        .Select(g => new VentasAgrupadasViewModel
                        {
                            Etiqueta = g.Key,
                            Monto = g.Sum(d => d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal),
                            Cantidad = (int)g.Sum(d => d.Cantidad),
                            Ganancia = g.Sum(d => (d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal) - CalcularCostoTotalDetalle(d))
                        })
                        .OrderByDescending(v => v.Monto)
                        .ToList(),

                    _ => new List<VentasAgrupadasViewModel>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener ventas agrupadas");
                throw;
            }
        }

        private static decimal CalcularSaldoCuota(Cuota cuota) =>
            Math.Max(0m, cuota.MontoTotal - cuota.MontoPagado);

        internal static decimal CalcularMargenPorcentaje(decimal ganancia, decimal @base) =>
            @base > 0 ? (ganancia / @base) * 100 : 0;

        internal static decimal CalcularCostoDetalles(IEnumerable<VentaDetalle> detalles) =>
            detalles.Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted)
                    .Sum(CalcularCostoTotalDetalle);

        internal static decimal CalcularCostoTotalDetalle(VentaDetalle detalle)
        {
            if (detalle.CostoTotalAlMomento > 0m)
                return detalle.CostoTotalAlMomento;

            var costoUnitario = detalle.CostoUnitarioAlMomento > 0m
                ? detalle.CostoUnitarioAlMomento
                : detalle.Producto?.PrecioCompra ?? 0m;

            return costoUnitario * detalle.Cantidad;
        }

        internal static decimal ResolverRecargoDebitoAplicado(Venta venta)
        {
            if (venta.DatosTarjeta?.TipoTarjeta != TipoTarjeta.Debito)
                return 0m;

            var recargo = venta.DatosTarjeta.RecargoAplicado.GetValueOrDefault();
            return recargo > 0m ? recargo : 0m;
        }

        public async Task<ComisionVendedorReporteViewModel> GenerarReporteComisionesVendedoresAsync(
            ComisionVendedorFilterViewModel filtro)
        {
            try
            {
                var query = _context.VentaDetalles
                    .AsNoTracking()
                    .Where(d =>
                        !d.IsDeleted &&
                        d.Venta != null &&
                        !d.Venta.IsDeleted &&
                        d.Producto != null &&
                        !d.Producto.IsDeleted)
                    .AsQueryable();

                if (filtro.FechaDesde.HasValue)
                {
                    var desde = filtro.FechaDesde.Value.Date;
                    query = query.Where(d => d.Venta.FechaVenta >= desde);
                }

                if (filtro.FechaHasta.HasValue)
                {
                    var hastaExclusivo = filtro.FechaHasta.Value.Date.AddDays(1);
                    query = query.Where(d => d.Venta.FechaVenta < hastaExclusivo);
                }

                if (!string.IsNullOrWhiteSpace(filtro.VendedorUserId))
                {
                    query = query.Where(d => d.Venta.VendedorUserId == filtro.VendedorUserId);
                }

                if (filtro.TipoPago.HasValue)
                {
                    query = query.Where(d => d.Venta.TipoPago == filtro.TipoPago.Value);
                }

                if (filtro.EstadoVenta.HasValue)
                {
                    query = query.Where(d => d.Venta.Estado == filtro.EstadoVenta.Value);
                }
                else
                {
                    query = query.Where(d =>
                        d.Venta.Estado == EstadoVenta.Confirmada ||
                        d.Venta.Estado == EstadoVenta.Facturada ||
                        d.Venta.Estado == EstadoVenta.Entregada);
                }

                if (filtro.ProductoId.HasValue)
                {
                    query = query.Where(d => d.ProductoId == filtro.ProductoId.Value);
                }

                if (!string.IsNullOrWhiteSpace(filtro.ClienteTexto))
                {
                    var texto = filtro.ClienteTexto.Trim();
                    query = query.Where(d =>
                        d.Venta.Numero.Contains(texto) ||
                        d.Producto.Nombre.Contains(texto) ||
                        d.Producto.Codigo.Contains(texto) ||
                        (d.Venta.Cliente != null && (d.Venta.Cliente.Nombre.Contains(texto) ||
                        d.Venta.Cliente.Apellido.Contains(texto) ||
                        d.Venta.Cliente.NumeroDocumento.Contains(texto))) ||
                        (d.Venta.NombreClienteLibre != null && d.Venta.NombreClienteLibre.Contains(texto)) ||
                        (d.Venta.DniClienteLibre != null && d.Venta.DniClienteLibre.Contains(texto)));
                }

                var items = await query
                    .OrderByDescending(d => d.Venta.FechaVenta)
                    .ThenByDescending(d => d.Venta.Id)
                    .ThenBy(d => d.Id)
                    .Select(d => new ComisionVendedorItemViewModel
                    {
                        FechaVenta = d.Venta.FechaVenta,
                        NumeroVenta = d.Venta.Numero,
                        VentaId = d.VentaId,
                        VendedorUserId = d.Venta.VendedorUserId,
                        VendedorNombre = !string.IsNullOrWhiteSpace(d.Venta.VendedorNombre)
                            ? d.Venta.VendedorNombre!
                            : d.Venta.VendedorUser != null && !string.IsNullOrWhiteSpace(d.Venta.VendedorUser.UserName)
                                ? d.Venta.VendedorUser.UserName!
                                : "Sin vendedor",
                        ClienteNombre = d.Venta.Cliente != null ? d.Venta.Cliente.Apellido + ", " + d.Venta.Cliente.Nombre : d.Venta.NombreClienteLibre ?? "Sin cliente",
                        ProductoId = d.ProductoId,
                        ProductoNombre = d.Producto.Nombre,
                        Cantidad = d.Cantidad,
                        PrecioUnitario = d.PrecioUnitario,
                        PrecioFinalItem = d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal,
                        TipoPago = d.Venta.TipoPago,
                        TipoPagoDescripcion = d.Venta.TipoPago.GetDisplayName(),
                        EstadoVenta = d.Venta.Estado,
                        EstadoVentaDescripcion = d.Venta.Estado.ToString(),
                        ComisionPorcentajeAplicada = d.ComisionPorcentajeAplicada,
                        ComisionMonto = d.ComisionMonto
                    })
                    .ToListAsync();

                var resumenPorVendedor = items
                    .GroupBy(i => i.VendedorUserId)
                    .Select(g => new ComisionVendedorResumenViewModel
                    {
                        VendedorUserId = g.Key,
                        VendedorNombre = g.First().VendedorNombre,
                        TotalVendido = g.Sum(i => i.PrecioFinalItem),
                        TotalComision = g.Sum(i => i.ComisionMonto),
                        CantidadVentas = g.Select(i => i.VentaId).Distinct().Count(),
                        CantidadProductosVendidos = g.Sum(i => i.Cantidad),
                        PromedioComisionPorcentaje = g.Any() ? g.Average(i => i.ComisionPorcentajeAplicada) : 0m,
                        Items = g.OrderByDescending(i => i.FechaVenta).ThenByDescending(i => i.VentaId).ToList()
                    })
                    .OrderByDescending(r => r.TotalComision)
                    .ToList();

                return new ComisionVendedorReporteViewModel
                {
                    Filtros = filtro,
                    Items = items,
                    ResumenPorVendedor = resumenPorVendedor,
                    TotalVendido = items.Sum(i => i.PrecioFinalItem),
                    TotalComision = items.Sum(i => i.ComisionMonto),
                    CantidadVentas = items.Select(i => i.VentaId).Distinct().Count(),
                    CantidadProductosVendidos = items.Sum(i => i.Cantidad),
                    PromedioComisionPorcentaje = items.Count > 0
                        ? items.Average(i => i.ComisionPorcentajeAplicada)
                        : 0m
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de comisiones por vendedor");
                throw;
            }
        }

        // Métodos auxiliares privados
        public async Task<ReporteMovimientosValorizadosViewModel> GenerarReporteMovimientosValorizadosAsync(
            ReporteMovimientosValorizadosFiltroViewModel filtro)
        {
            try
            {
                var query = _context.MovimientosStock
                    .AsNoTracking()
                    .Include(m => m.Producto)
                    .Where(m => !m.IsDeleted && m.Producto != null)
                    .AsQueryable();

                if (filtro.FechaDesde.HasValue)
                {
                    var desde = filtro.FechaDesde.Value.Date;
                    query = query.Where(m => m.CreatedAt >= desde);
                }

                if (filtro.FechaHasta.HasValue)
                {
                    var hastaExclusivo = filtro.FechaHasta.Value.Date.AddDays(1);
                    query = query.Where(m => m.CreatedAt < hastaExclusivo);
                }

                if (filtro.ProductoId.HasValue)
                    query = query.Where(m => m.ProductoId == filtro.ProductoId.Value);

                if (filtro.Tipo.HasValue)
                    query = query.Where(m => m.Tipo == filtro.Tipo.Value);

                if (!string.IsNullOrWhiteSpace(filtro.FuenteCosto))
                {
                    var fuente = filtro.FuenteCosto.Trim();
                    query = query.Where(m => m.FuenteCosto == fuente);
                }

                if (!string.IsNullOrWhiteSpace(filtro.Texto))
                {
                    var texto = filtro.Texto.Trim();
                    query = query.Where(m =>
                        m.Producto.Nombre.Contains(texto) ||
                        m.Producto.Codigo.Contains(texto) ||
                        (m.Referencia != null && m.Referencia.Contains(texto)) ||
                        (m.Motivo != null && m.Motivo.Contains(texto)));
                }

                var movimientos = await query
                    .OrderByDescending(m => m.CreatedAt)
                    .ThenByDescending(m => m.Id)
                    .Select(m => new
                    {
                        m.Id,
                        Fecha = m.CreatedAt,
                        m.Tipo,
                        m.ProductoId,
                        ProductoCodigo = m.Producto.Codigo,
                        ProductoNombre = m.Producto.Nombre,
                        m.Cantidad,
                        m.CostoUnitarioAlMomento,
                        m.CostoTotalAlMomento,
                        m.FuenteCosto,
                        m.Referencia,
                        m.Motivo,
                        m.CreatedBy
                    })
                    .ToListAsync();

                var items = movimientos.Select(m =>
                {
                    var impacto = CalcularImpactoValorizado(
                        m.Tipo,
                        m.Cantidad,
                        m.CostoTotalAlMomento);

                    return new ReporteMovimientoValorizadoItemViewModel
                    {
                        Id = m.Id,
                        Fecha = m.Fecha,
                        Tipo = m.Tipo,
                        ProductoId = m.ProductoId,
                        ProductoCodigo = m.ProductoCodigo ?? string.Empty,
                        ProductoNombre = m.ProductoNombre ?? string.Empty,
                        Cantidad = m.Cantidad,
                        CostoUnitarioAlMomento = m.CostoUnitarioAlMomento,
                        CostoTotalAlMomento = m.CostoTotalAlMomento,
                        ImpactoValorizado = impacto,
                        FuenteCosto = string.IsNullOrWhiteSpace(m.FuenteCosto) ? "NoInformado" : m.FuenteCosto,
                        Referencia = m.Referencia,
                        Motivo = m.Motivo,
                        CreatedBy = m.CreatedBy
                    };
                }).ToList();

                return new ReporteMovimientosValorizadosViewModel
                {
                    Filtros = filtro,
                    Items = items,
                    CantidadMovimientos = items.Count,
                    EntradasValorizadas = items
                        .Where(i => i.Tipo == TipoMovimiento.Entrada)
                        .Sum(i => i.CostoTotalAlMomento),
                    SalidasValorizadas = items
                        .Where(i => i.Tipo == TipoMovimiento.Salida)
                        .Sum(i => i.CostoTotalAlMomento),
                    AjustesValorizadosNetos = items
                        .Where(i => i.Tipo == TipoMovimiento.Ajuste)
                        .Sum(i => i.ImpactoValorizado),
                    NetoValorizado = items.Sum(i => i.ImpactoValorizado),
                    MovimientosSinCostoInformado = items.Count(i =>
                        i.CostoTotalAlMomento <= 0m ||
                        string.Equals(i.FuenteCosto, "NoInformado", StringComparison.OrdinalIgnoreCase))
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar reporte de movimientos valorizados");
                throw;
            }
        }

        internal static decimal CalcularImpactoValorizado(
            TipoMovimiento tipo,
            decimal cantidad,
            decimal costoTotalAlMomento)
        {
            var costoAbs = Math.Abs(costoTotalAlMomento);

            return tipo switch
            {
                TipoMovimiento.Entrada => costoAbs,
                TipoMovimiento.Salida => -costoAbs,
                TipoMovimiento.Ajuste when cantidad > 0m => costoAbs,
                TipoMovimiento.Ajuste when cantidad < 0m => -costoAbs,
                _ => 0m
            };
        }

        // Top de productos/clientes se calcula sobre las mismas ventas ya filtradas del reporte,
        // para que los paneles sean coherentes con los totales y filtros seleccionados.
        private static List<ProductoMasVendidoViewModel> ObtenerProductosMasVendidos(
            IEnumerable<Venta> ventas,
            ReporteVentasFiltroViewModel filtro,
            bool menosVendidos = false)
        {
            var lineas = ventas
                .SelectMany(v => v.Detalles)
                .Where(d => !d.IsDeleted && d.Producto != null && !d.Producto.IsDeleted);

            if (filtro.ProductoId.HasValue)
                lineas = lineas.Where(d => d.ProductoId == filtro.ProductoId.Value);
            if (filtro.CategoriaId.HasValue)
                lineas = lineas.Where(d => d.Producto.CategoriaId == filtro.CategoriaId.Value);
            if (filtro.MarcaId.HasValue)
                lineas = lineas.Where(d => d.Producto.MarcaId == filtro.MarcaId.Value);

            var agrupados = lineas
                .GroupBy(d => d.ProductoId)
                .Select(g =>
                {
                    var producto = g.First().Producto;
                    var montoTotal = g.Sum(d => d.SubtotalFinal > 0m ? d.SubtotalFinal : d.Subtotal);
                    var costoTotal = g.Sum(CalcularCostoTotalDetalle);
                    var gananciaTotal = montoTotal - costoTotal;

                    return new ProductoMasVendidoViewModel
                    {
                        ProductoId = g.Key,
                        ProductoCodigo = producto.Codigo,
                        ProductoNombre = producto.Nombre,
                        CategoriaNombre = producto.Categoria?.Nombre ?? string.Empty,
                        CantidadVendida = (int)g.Sum(d => d.Cantidad),
                        MontoTotal = montoTotal,
                        GananciaTotal = gananciaTotal,
                        MargenPromedio = CalcularMargenPorcentaje(gananciaTotal, montoTotal)
                    };
                })
                .ToList();

            // "Menos vendidos" = los de menor rotación entre los que tuvieron ventas en el período.
            var ordenados = menosVendidos
                ? agrupados.OrderBy(p => p.CantidadVendida).ThenBy(p => p.MontoTotal)
                : agrupados.OrderByDescending(p => p.CantidadVendida).ThenByDescending(p => p.MontoTotal);

            return ordenados.Take(10).ToList();
        }

        private static List<ClienteTopViewModel> ObtenerClientesTop(IEnumerable<Venta> ventas, bool porFrecuencia = false)
        {
            var clientes = ventas
                .Where(v => v.Cliente != null && v.ClienteId.HasValue)
                .GroupBy(v => v.ClienteId!.Value)
                .Select(g =>
                {
                    var cliente = g.First().Cliente!;
                    return new ClienteTopViewModel
                    {
                        ClienteId = g.Key,
                        // Apellido, Nombre sin "- DNI:": el documento ya se muestra en su propia columna.
                        ClienteNombre = $"{cliente.Apellido}, {cliente.Nombre}",
                        ClienteDocumento = cliente.NumeroDocumento,
                        CantidadCompras = g.Count(),
                        MontoTotal = g.Sum(v => v.Total),
                        TicketPromedio = g.Average(v => v.Total),
                        UltimaCompra = g.Max(v => v.FechaVenta)
                    };
                })
                .ToList();

            var ordenados = porFrecuencia
                ? clientes.OrderByDescending(c => c.CantidadCompras).ThenByDescending(c => c.MontoTotal)
                : clientes.OrderByDescending(c => c.MontoTotal);

            return ordenados.Take(10).ToList();
        }

        #endregion

        #region Exportación Excel
        public async Task<byte[]> ExportarVentasExcelAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                var reporte = await GenerarReporteVentasAsync(filtro);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Ventas");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Nº Venta";
                worksheet.Cell(1, 2).Value = "Fecha";
                worksheet.Cell(1, 3).Value = "Cliente";
                worksheet.Cell(1, 4).Value = "Vendedor";
                worksheet.Cell(1, 5).Value = "Tipo Pago";
                worksheet.Cell(1, 6).Value = "Subtotal Neto";
                worksheet.Cell(1, 7).Value = "Descuento ($)";
                worksheet.Cell(1, 8).Value = "IVA";
                worksheet.Cell(1, 9).Value = "Recargo débito";
                worksheet.Cell(1, 10).Value = "Total";
                worksheet.Cell(1, 11).Value = "Ganancia";
                worksheet.Cell(1, 12).Value = "Margen %";

                // Estilo de encabezados
                var headerRange = worksheet.Range("A1:L1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

                // Datos
                int row = 2;
                foreach (var venta in reporte.Ventas)
                {
                    worksheet.Cell(row, 1).Value = venta.NumeroVenta;
                    worksheet.Cell(row, 2).Value = venta.FechaVenta;
                    worksheet.Cell(row, 3).Value = venta.ClienteNombre;
                    worksheet.Cell(row, 4).Value = venta.VendedorNombre;
                    worksheet.Cell(row, 5).Value = venta.TipoPagoDescripcion;
                    worksheet.Cell(row, 6).Value = venta.Subtotal;
                    worksheet.Cell(row, 7).Value = venta.Descuento;
                    worksheet.Cell(row, 8).Value = venta.IVA;
                    worksheet.Cell(row, 9).Value = venta.RecargoDebitoAplicado;
                    worksheet.Cell(row, 10).Value = venta.Total;
                    worksheet.Cell(row, 11).Value = venta.Ganancia;
                    worksheet.Cell(row, 12).Value = venta.MargenPorcentaje;
                    row++;
                }

                // Totales
                worksheet.Cell(row, 5).Value = "TOTALES:";
                worksheet.Cell(row, 5).Style.Font.Bold = true;
                worksheet.Cell(row, 6).Value = reporte.Ventas.Sum(v => v.Subtotal);
                worksheet.Cell(row, 7).Value = reporte.Ventas.Sum(v => v.Descuento);
                worksheet.Cell(row, 8).Value = reporte.Ventas.Sum(v => v.IVA);
                worksheet.Cell(row, 9).Value = reporte.TotalRecargoDebito;
                worksheet.Cell(row, 10).Value = reporte.TotalVentas;
                worksheet.Cell(row, 11).Value = reporte.TotalGanancia;
                worksheet.Cell(row, 12).Value = reporte.MargenPromedio;

                // Formatear columnas monetarias y porcentuales
                worksheet.Range($"F2:K{row}").Style.NumberFormat.Format = "$#,##0.00";  // Subtotal neto, Descuento, IVA, Recargo, Total, Ganancia
                worksheet.Range($"L2:L{row}").Style.NumberFormat.Format = "0.00";        // Margen %

                // Ajustar ancho de columnas
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar ventas a Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportarMargenesExcelAsync(int? categoriaId = null, int? marcaId = null)
        {
            try
            {
                var reporte = await GenerarReporteMargenesAsync(categoriaId, marcaId);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Margenes");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Código";
                worksheet.Cell(1, 2).Value = "Producto";
                worksheet.Cell(1, 3).Value = "Categoría";
                worksheet.Cell(1, 4).Value = "Marca";
                worksheet.Cell(1, 5).Value = "P. Compra";
                worksheet.Cell(1, 6).Value = "P. Venta";
                worksheet.Cell(1, 7).Value = "Ganancia";
                worksheet.Cell(1, 8).Value = "Margen %";
                worksheet.Cell(1, 9).Value = "Stock";
                worksheet.Cell(1, 10).Value = "Ganancia Potencial";

                var headerRange = worksheet.Range("A1:J1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightGreen;

                int row = 2;
                foreach (var producto in reporte.Productos)
                {
                    worksheet.Cell(row, 1).Value = producto.Codigo;
                    worksheet.Cell(row, 2).Value = producto.Nombre;
                    worksheet.Cell(row, 3).Value = producto.CategoriaNombre;
                    worksheet.Cell(row, 4).Value = producto.MarcaNombre;
                    worksheet.Cell(row, 5).Value = producto.PrecioCompra;
                    worksheet.Cell(row, 6).Value = producto.PrecioVenta;
                    worksheet.Cell(row, 7).Value = producto.Ganancia;
                    worksheet.Cell(row, 8).Value = producto.MargenPorcentaje;
                    worksheet.Cell(row, 9).Value = producto.StockActual;
                    worksheet.Cell(row, 10).Value = producto.GananciaPotencial;
                    row++;
                }

                worksheet.Range($"E2:G{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Range($"H2:H{row}").Style.NumberFormat.Format = "0.00";
                worksheet.Range($"J2:J{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar márgenes a Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportarMovimientosValorizadosExcelAsync(
            ReporteMovimientosValorizadosFiltroViewModel filtro)
        {
            try
            {
                var reporte = await GenerarReporteMovimientosValorizadosAsync(filtro);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Movimientos valorizados");

                worksheet.Cell(1, 1).Value = "Fecha";
                worksheet.Cell(1, 2).Value = "Tipo";
                worksheet.Cell(1, 3).Value = "Codigo";
                worksheet.Cell(1, 4).Value = "Producto";
                worksheet.Cell(1, 5).Value = "Cantidad";
                worksheet.Cell(1, 6).Value = "Costo unitario";
                worksheet.Cell(1, 7).Value = "Costo total";
                worksheet.Cell(1, 8).Value = "Impacto valorizado";
                worksheet.Cell(1, 9).Value = "Fuente costo";
                worksheet.Cell(1, 10).Value = "Referencia";
                worksheet.Cell(1, 11).Value = "Motivo";
                worksheet.Cell(1, 12).Value = "Usuario";

                var headerRange = worksheet.Range("A1:L1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

                var row = 2;
                foreach (var item in reporte.Items)
                {
                    worksheet.Cell(row, 1).Value = item.Fecha;
                    worksheet.Cell(row, 2).Value = item.TipoDescripcion;
                    worksheet.Cell(row, 3).Value = item.ProductoCodigo;
                    worksheet.Cell(row, 4).Value = item.ProductoNombre;
                    worksheet.Cell(row, 5).Value = item.Cantidad;
                    worksheet.Cell(row, 6).Value = item.CostoUnitarioAlMomento;
                    worksheet.Cell(row, 7).Value = item.CostoTotalAlMomento;
                    worksheet.Cell(row, 8).Value = item.ImpactoValorizado;
                    worksheet.Cell(row, 9).Value = item.FuenteCosto;
                    worksheet.Cell(row, 10).Value = item.Referencia ?? string.Empty;
                    worksheet.Cell(row, 11).Value = item.Motivo ?? string.Empty;
                    worksheet.Cell(row, 12).Value = item.CreatedBy ?? string.Empty;
                    row++;
                }

                worksheet.Cell(row, 4).Value = "TOTALES:";
                worksheet.Cell(row, 4).Style.Font.Bold = true;
                worksheet.Cell(row, 7).Value = reporte.Items.Sum(i => i.CostoTotalAlMomento);
                worksheet.Cell(row, 8).Value = reporte.NetoValorizado;

                worksheet.Range($"A2:A{row}").Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                worksheet.Range($"F2:H{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar movimientos valorizados a Excel");
                throw;
            }
        }

        public async Task<byte[]> ExportarMorosidadExcelAsync()
        {
            try
            {
                var reporte = await GenerarReporteMorosidadAsync();

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Morosidad");

                // Encabezados
                worksheet.Cell(1, 1).Value = "Cliente";
                worksheet.Cell(1, 2).Value = "Documento";
                worksheet.Cell(1, 3).Value = "Teléfono";
                worksheet.Cell(1, 4).Value = "Créditos Vencidos";
                worksheet.Cell(1, 5).Value = "Deuda Vencida";
                worksheet.Cell(1, 6).Value = "Deuda Vigente";
                worksheet.Cell(1, 7).Value = "Días Atraso";
                worksheet.Cell(1, 8).Value = "Nivel Riesgo";

                var headerRange = worksheet.Range("A1:H1");
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightCoral;

                int row = 2;
                foreach (var cliente in reporte.ClientesMorosos)
                {
                    worksheet.Cell(row, 1).Value = cliente.ClienteNombre;
                    worksheet.Cell(row, 2).Value = cliente.ClienteDocumento;
                    worksheet.Cell(row, 3).Value = cliente.ClienteTelefono;
                    worksheet.Cell(row, 4).Value = cliente.CantidadCreditosVencidos;
                    worksheet.Cell(row, 5).Value = cliente.TotalDeudaVencida;
                    worksheet.Cell(row, 6).Value = cliente.TotalDeudaVigente;
                    worksheet.Cell(row, 7).Value = cliente.DiasMaximoAtraso;
                    worksheet.Cell(row, 8).Value = cliente.NivelRiesgo;
                    row++;
                }

                worksheet.Range($"E2:F{row}").Style.NumberFormat.Format = "$#,##0.00";
                worksheet.Columns().AdjustToContents();

                using var stream = new MemoryStream();
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar morosidad a Excel");
                throw;
            }
        }

        #endregion

        #region Exportación PDF

        public async Task<byte[]> GenerarVentasPdfAsync(ReporteVentasFiltroViewModel filtro)
        {
            try
            {
                ConfigurarQuestPdf();
                var reporte = await GenerarReporteVentasAsync(filtro);

                var documento = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Text("Reporte de Ventas")
                            .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                        {
                            // Resumen
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Total Ventas: ${reporte.TotalVentas:N2}").Bold();
                                row.RelativeItem().Text($"Total Recargo débito: ${reporte.TotalRecargoDebito:N2}").Bold();
                                row.RelativeItem().Text($"Ganancia: ${reporte.TotalGanancia:N2}").Bold();
                                row.RelativeItem().Text($"Margen: {reporte.MargenPromedio:N1}%").Bold();
                            });

                            col.Item().PaddingVertical(5);

                            // Tabla
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(55);
                                    columns.ConstantColumn(60);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(65);
                                    columns.ConstantColumn(65);
                                    columns.ConstantColumn(65);
                                    columns.ConstantColumn(55);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Nº Venta").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Fecha").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Cliente").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Tipo Pago").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Rec. débito").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Total").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Ganancia").FontSize(8).Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Margen %").FontSize(8).Bold();
                                });

                                foreach (var venta in reporte.Ventas.Take(50)) // Limitar a 50 para PDF
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(venta.NumeroVenta).FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(venta.FechaVenta.ToString("dd/MM/yy")).FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(venta.ClienteNombre).FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(venta.TipoPagoDescripcion).FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text($"${venta.RecargoDebitoAplicado:N2}").FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text($"${venta.Total:N2}").FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text($"${venta.Ganancia:N2}").FontSize(8);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text($"{venta.MargenPorcentaje:N1}%").FontSize(8);
                                }
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("Página ");
                            text.CurrentPageNumber();
                            text.Span(" de ");
                            text.TotalPages();
                        });
                    });
                });

                return documento.GeneratePdf();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF de ventas");
                throw;
            }
        }

        public async Task<byte[]> GenerarMorosidadPdfAsync()
        {
            try
            {
                ConfigurarQuestPdf();
                var reporte = await GenerarReporteMorosidadAsync();

                var documento = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Header().Text("Reporte de Morosidad")
                            .SemiBold().FontSize(20).FontColor(Colors.Red.Medium);

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Deuda Vencida: ${reporte.TotalDeudaVencida:N2}").Bold();
                                row.RelativeItem().Text($"Clientes: {reporte.CantidadClientesMorosos}").Bold();
                            });

                            col.Item().PaddingVertical(5);

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(70);
                                    columns.ConstantColumn(70);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Cliente").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Documento").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Deuda").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Días").Bold();
                                    header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Riesgo").Bold();
                                });

                                foreach (var cliente in reporte.ClientesMorosos)
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.ClienteNombre);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.ClienteDocumento);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"${cliente.TotalDeudaVencida:N2}");
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.DiasMaximoAtraso.ToString());
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cliente.NivelRiesgo);
                                }
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span("Generado el ");
                            text.Span(DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm"));
                        });
                    });
                });

                return documento.GeneratePdf();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF de morosidad");
                throw;
            }
        }

        #endregion
    }
}
