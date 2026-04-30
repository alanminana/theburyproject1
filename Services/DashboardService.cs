using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class DashboardService : IDashboardService
    {
        #region Constructor y dependencias

        private readonly AppDbContext _context;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(AppDbContext context, ILogger<DashboardService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #endregion

        #region Dashboard principal

        public async Task<DashboardViewModel> GetDashboardDataAsync()
        {
            var hoy = DateTime.Today;
            var manana = hoy.AddDays(1);
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var inicioAnio = new DateTime(hoy.Year, 1, 1);

            // DbContext no es thread-safe: ejecutar queries secuencialmente
            var totalClientes     = await _context.Clientes.CountAsync(c => !c.IsDeleted);
            var clientesActivos   = await _context.Clientes.CountAsync(c => !c.IsDeleted && c.Activo);
            var clientesNuevosMes = await _context.Clientes.CountAsync(c => !c.IsDeleted && c.CreatedAt >= inicioMes);

            // Ventas: fetch raw para evitar expresiones no traducibles (.Date, decimal cast)
            var ventasRaw = await _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioAnio)
                .Select(v => new { v.FechaVenta, v.Total })
                .ToListAsync();
            var ventasHoy     = ventasRaw.Where(v => v.FechaVenta >= hoy && v.FechaVenta < manana).Sum(v => v.Total);
            var ventasMes     = ventasRaw.Where(v => v.FechaVenta >= inicioMes).Sum(v => v.Total);
            var cantVentasMes = ventasRaw.Count(v => v.FechaVenta >= inicioMes);
            var ventasAnio    = ventasRaw.Sum(v => v.Total);
            var ticketPromedio = cantVentasMes > 0
                ? ventasRaw.Where(v => v.FechaVenta >= inicioMes).Average(v => v.Total)
                : 0;

            // Créditos: fetch raw para sumas — incluye todos los estados operativos
            var estadosOperativos = new[]
            {
                EstadoCredito.Activo,
                EstadoCredito.Generado,
                EstadoCredito.Configurado,
                EstadoCredito.Aprobado,
                EstadoCredito.PendienteConfiguracion
            };
            var creditosRaw = await _context.Creditos
                .Where(c => !c.IsDeleted && c.Cliente != null && !c.Cliente.IsDeleted && estadosOperativos.Contains(c.Estado))
                .Select(c => new { c.TotalAPagar, c.SaldoPendiente })
                .ToListAsync();
            var creditosActivos    = creditosRaw.Count;
            var montoTotalCreditos = creditosRaw.Sum(c => c.TotalAPagar);
            var saldoPendiente     = creditosRaw.Sum(c => c.SaldoPendiente);

            // Cuotas pendientes
            var cuotasPendientesRaw = await _context.Cuotas
                .Where(c => !c.IsDeleted && c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pendiente)
                .Select(c => new { c.FechaVencimiento, c.MontoTotal })
                .ToListAsync();
            var cuotasVencidasCount = cuotasPendientesRaw.Count(c => c.FechaVencimiento < hoy);
            var montoVencido        = cuotasPendientesRaw.Where(c => c.FechaVencimiento < hoy).Sum(c => c.MontoTotal);

            // Cuotas pagadas: fetch raw para evitar .Value.Date
            var cuotasPagadasRaw = await _context.Cuotas
                .Where(c => !c.IsDeleted && c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pagada && c.FechaPago.HasValue &&
                            c.FechaPago >= inicioAnio)
                .Select(c => new { c.FechaPago, c.MontoPagado })
                .ToListAsync();
            var cobranzaHoy  = cuotasPagadasRaw.Where(c => c.FechaPago!.Value >= hoy && c.FechaPago.Value < manana).Sum(c => c.MontoPagado);
            var cobranzaMes  = cuotasPagadasRaw.Where(c => c.FechaPago!.Value >= inicioMes).Sum(c => c.MontoPagado);
            var cobranzaAnio = cuotasPagadasRaw.Sum(c => c.MontoPagado);

            var tasaMorosidad       = await CalcularTasaMorosidadAsync();
            var efectividadCobranza = await CalcularEfectividadCobranzaAsync();

            // Productos: fetch raw para evitar multiplicación no traducible
            var productosRaw = await _context.Productos
                .Where(p => !p.IsDeleted)
                .Select(p => new { p.StockActual, p.PrecioVenta, p.PrecioCompra, p.StockMinimo })
                .ToListAsync();
            var productosTotales   = productosRaw.Count;
            var productosStockBajo = productosRaw.Count(p => p.StockActual < p.StockMinimo);
            var valorStockPrecioVenta = productosRaw.Sum(p => p.StockActual * p.PrecioVenta);
            var valorStockCostoActual = productosRaw.Sum(p => p.StockActual * p.PrecioCompra);

            var ventasUlt7      = await GetVentasUltimos7DiasAsync();
            var ventasUlt12     = await GetVentasUltimos12MesesAsync();
            var prodMasVendidos = await GetProductosMasVendidosAsync();
            var creditosEstado  = await GetCreditosPorEstadoAsync();
            var cobranzaUlt6    = await GetCobranzaUltimos6MesesAsync();
            var cuotasProximas  = await GetCuotasProximasVencerAsync();
            var cuotasVencLista = await GetCuotasVencidasListaAsync();
            var ordenes         = await GetOrdenesCompraPendientesAsync();

            return new DashboardViewModel
            {
                TotalClientes        = totalClientes,
                ClientesActivos      = clientesActivos,
                ClientesNuevosMes    = clientesNuevosMes,

                VentasTotalesHoy     = ventasHoy,
                VentasTotalesMes     = ventasMes,
                CantidadVentasMes    = cantVentasMes,
                VentasTotalesAnio    = ventasAnio,
                TicketPromedio       = ticketPromedio,

                CreditosActivos      = creditosActivos,
                MontoTotalCreditos   = montoTotalCreditos,
                SaldoPendienteTotal  = saldoPendiente,

                CuotasVencidasTotal  = cuotasVencidasCount,
                MontoVencidoTotal    = montoVencido,

                CobranzaHoy          = cobranzaHoy,
                CobranzaMes          = cobranzaMes,
                CobranzaAnio         = cobranzaAnio,

                TasaMorosidad        = tasaMorosidad,
                EfectividadCobranza  = efectividadCobranza,

                ProductosTotales     = productosTotales,
                ProductosStockBajo   = productosStockBajo,
                ValorStockPrecioVenta = valorStockPrecioVenta,
                ValorStockCostoActual = valorStockCostoActual,
                ValorTotalStock       = valorStockPrecioVenta,

                VentasUltimos7Dias      = ventasUlt7,
                VentasUltimos12Meses    = ventasUlt12,
                ProductosMasVendidos    = prodMasVendidos,
                CreditosPorEstado       = creditosEstado,
                CobranzaUltimos6Meses   = cobranzaUlt6,

                CuotasProximasVencer    = cuotasProximas,
                CuotasVencidasLista     = cuotasVencLista,
                OrdenesCompraPendientes = ordenes,

                CuotasProximasVencerCount      = cuotasProximas.Count,
                MontoCuotasProximasVencer       = cuotasProximas.Sum(c => c.Monto),
                OrdenesCompraPendientesCount    = ordenes.Count,
                MontoOrdenesCompraPendientes    = ordenes.Sum(o => o.Total)
            };
        }

        #endregion

        #region KPIs

        private async Task<decimal> CalcularTicketPromedioAsync()
        {
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var agregados = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioMes)
                .Select(v => new { v.Total })
                .ToListAsync();

            if (agregados.Count == 0) return 0;

            return agregados.Sum(v => v.Total) / agregados.Count;
        }

        private async Task<decimal> CalcularTasaMorosidadAsync()
        {
            var totalCuotas = await _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pendiente)
                .CountAsync();

            if (totalCuotas == 0) return 0;

            var cuotasVencidas = await _context.Cuotas
                .CountAsync(c => !c.IsDeleted &&
                           c.Credito != null &&
                           !c.Credito.IsDeleted &&
                           c.Credito.Cliente != null &&
                           !c.Credito.Cliente.IsDeleted &&
                           c.Estado == EstadoCuota.Pendiente &&
                           c.FechaVencimiento < DateTime.Today);

            return ((decimal)cuotasVencidas / totalCuotas) * 100;
        }

        private async Task<decimal> CalcularEfectividadCobranzaAsync()
        {
            var hoy = DateTime.Today;
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

            var cuotasRaw = await _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            c.FechaVencimiento >= inicioMes)
                .Select(c => new { c.FechaVencimiento, c.MontoTotal, c.Estado, c.FechaPago, c.MontoPagado })
                .ToListAsync();

            var montoEsperado = cuotasRaw
                .Where(c => c.FechaVencimiento < hoy)
                .Sum(c => c.MontoTotal);

            if (montoEsperado == 0) return 0;

            var montoRecaudado = cuotasRaw
                .Where(c => c.Estado == EstadoCuota.Pagada && c.FechaPago.HasValue && c.FechaPago.Value >= inicioMes)
                .Sum(c => c.MontoPagado);

            return (montoRecaudado / montoEsperado) * 100;
        }

        #endregion

        #region Datos para gráficos

        private async Task<List<VentasPorDiaDto>> GetVentasUltimos7DiasAsync()
        {
            var hoy = DateTime.Today;
            var hace7Dias = hoy.AddDays(-7);

            var raw = await _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= hace7Dias)
                .Select(v => new { v.FechaVenta, v.Total })
                .ToListAsync();

            var agrupado = raw
                .GroupBy(v => v.FechaVenta.Date)
                .Select(g => new VentasPorDiaDto
                {
                    Fecha = g.Key,
                    Total = g.Sum(v => v.Total),
                    Cantidad = g.Count()
                })
                .ToDictionary(v => v.Fecha);

            // Rellenar días faltantes
            var resultado = new List<VentasPorDiaDto>();
            for (var fecha = hace7Dias.Date; fecha <= hoy; fecha = fecha.AddDays(1))
            {
                resultado.Add(agrupado.TryGetValue(fecha, out var d) ? d : new VentasPorDiaDto { Fecha = fecha, Total = 0, Cantidad = 0 });
            }

            return resultado;
        }

        private async Task<List<VentasPorMesDto>> GetVentasUltimos12MesesAsync()
        {
            var hace12Meses = DateTime.Today.AddMonths(-12);
            var cultura = CultureInfo.GetCultureInfo("es-AR");

            var raw = await _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= hace12Meses)
                .Select(v => new { v.FechaVenta, v.Total })
                .ToListAsync();

            return raw
                .GroupBy(v => new { v.FechaVenta.Year, v.FechaVenta.Month })
                .Select(g => new VentasPorMesDto
                {
                    Anio = g.Key.Year,
                    Mes = g.Key.Month,
                    MesNombre = cultura.DateTimeFormat.GetMonthName(g.Key.Month),
                    Total = g.Sum(v => v.Total),
                    Cantidad = g.Count()
                })
                .OrderBy(v => v.Anio).ThenBy(v => v.Mes)
                .ToList();
        }

        private async Task<List<ProductoMasVendidoDto>> GetProductosMasVendidosAsync()
        {
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            var raw = await _context.VentaDetalles
                .Where(vd => !vd.IsDeleted &&
                             vd.Producto != null &&
                             !vd.Producto.IsDeleted &&
                             vd.Venta != null &&
                             !vd.Venta.IsDeleted &&
                             vd.Venta.FechaVenta >= inicioMes)
                .Select(vd => new { vd.ProductoId, vd.Producto!.Nombre, vd.Cantidad, vd.Subtotal, vd.SubtotalFinal })
                .ToListAsync();

            return raw
                .GroupBy(vd => new { vd.ProductoId, vd.Nombre })
                .Select(g => new ProductoMasVendidoDto
                {
                    ProductoId = g.Key.ProductoId,
                    ProductoNombre = g.Key.Nombre,
                    Cantidad = g.Sum(vd => vd.Cantidad),
                    TotalVendido = g.Sum(vd => vd.SubtotalFinal > 0m ? vd.SubtotalFinal : vd.Subtotal)
                })
                .OrderByDescending(p => p.Cantidad)
                .Take(10)
                .ToList();
        }

        private async Task<List<EstadoCreditoDto>> GetCreditosPorEstadoAsync()
        {
            var raw = await _context.Creditos
                .Where(c => !c.IsDeleted && c.Cliente != null && !c.Cliente.IsDeleted)
                .Select(c => new { c.Estado, c.TotalAPagar })
                .ToListAsync();

            return raw
                .GroupBy(c => c.Estado)
                .Select(g => new EstadoCreditoDto
                {
                    Estado = g.Key.ToString(),
                    Cantidad = g.Count(),
                    Monto = g.Sum(c => c.TotalAPagar)
                })
                .ToList();
        }

        private async Task<List<CobranzaPorMesDto>> GetCobranzaUltimos6MesesAsync()
        {
            var hace6Meses = DateTime.Today.AddMonths(-6);
            var inicioMes = new DateTime(hace6Meses.Year, hace6Meses.Month, 1);
            var cultura = CultureInfo.GetCultureInfo("es-AR");

            var raw = await _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            c.FechaVencimiento >= inicioMes)
                .Select(c => new { c.FechaVencimiento, c.MontoTotal, c.Estado, c.MontoPagado })
                .ToListAsync();

            return raw
                .GroupBy(c => new { c.FechaVencimiento.Year, c.FechaVencimiento.Month })
                .Select(g =>
                {
                    var esperado = g.Sum(c => c.MontoTotal);
                    var recaudado = g.Where(c => c.Estado == EstadoCuota.Pagada).Sum(c => c.MontoPagado);
                    return new CobranzaPorMesDto
                    {
                        Anio = g.Key.Year,
                        Mes = g.Key.Month,
                        MesNombre = cultura.DateTimeFormat.GetMonthName(g.Key.Month),
                        MontoEsperado = esperado,
                        MontoRecaudado = recaudado,
                        PorcentajeEfectividad = esperado > 0 ? (recaudado / esperado) * 100 : 0
                    };
                })
                .OrderBy(c => c.Anio).ThenBy(c => c.Mes)
                .ToList();
        }

        #endregion

        #region Listas de detalle

        /// <summary>
        /// Obtiene las cuotas que vencen en los próximos 7 días
        /// </summary>
        private async Task<List<CuotaProximaVencerDto>> GetCuotasProximasVencerAsync()
        {
            var hoy = DateTime.Today;
            var en7Dias = hoy.AddDays(7);

            // Consulta a la base de datos sin cálculos de fechas complejos
            var cuotasDb = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Credito)
                    .ThenInclude(cr => cr.Cliente)
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Parcial) &&
                            c.FechaVencimiento >= hoy &&
                            c.FechaVencimiento <= en7Dias)
                .OrderBy(c => c.FechaVencimiento)
                .Take(20)
                .Select(c => new 
                {
                    c.Id,
                    c.CreditoId,
                    CreditoNumero = c.Credito.Numero,
                    c.NumeroCuota,
                    ClienteNombre = c.Credito.Cliente.Apellido + ", " + c.Credito.Cliente.Nombre + " - DNI: " + c.Credito.Cliente.NumeroDocumento,
                    ClienteId = c.Credito.ClienteId,
                    c.FechaVencimiento,
                    Monto = c.MontoTotal - c.MontoPagado
                })
                .ToListAsync();

            // Calcular días en memoria
            return cuotasDb.Select(c => new CuotaProximaVencerDto
            {
                CuotaId = c.Id,
                CreditoId = c.CreditoId,
                CreditoNumero = c.CreditoNumero,
                NumeroCuota = c.NumeroCuota,
                ClienteNombre = c.ClienteNombre,
                ClienteId = c.ClienteId,
                FechaVencimiento = c.FechaVencimiento,
                Monto = c.Monto,
                DiasParaVencer = (c.FechaVencimiento - hoy).Days
            }).ToList();
        }

        /// <summary>
        /// Obtiene las cuotas vencidas sin pagar (ordenadas por más días vencidas)
        /// </summary>
        private async Task<List<CuotaVencidaDto>> GetCuotasVencidasListaAsync()
        {
            var hoy = DateTime.Today;

            // Consulta a la base de datos sin cálculos de fechas complejos
            var cuotasDb = await _context.Cuotas
                .AsNoTracking()
                .Include(c => c.Credito)
                    .ThenInclude(cr => cr.Cliente)
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            (c.Estado == EstadoCuota.Pendiente || c.Estado == EstadoCuota.Vencida || c.Estado == EstadoCuota.Parcial) &&
                            c.FechaVencimiento < hoy)
                .OrderBy(c => c.FechaVencimiento) // Más antiguas primero
                .Take(20)
                .Select(c => new 
                {
                    c.Id,
                    c.CreditoId,
                    CreditoNumero = c.Credito.Numero,
                    c.NumeroCuota,
                    ClienteNombre = c.Credito.Cliente.Apellido + ", " + c.Credito.Cliente.Nombre + " - DNI: " + c.Credito.Cliente.NumeroDocumento,
                    ClienteId = c.Credito.ClienteId,
                    c.FechaVencimiento,
                    Monto = c.MontoTotal - c.MontoPagado,
                    c.MontoPunitorio
                })
                .ToListAsync();

            // Calcular días en memoria
            return cuotasDb.Select(c => new CuotaVencidaDto
            {
                CuotaId = c.Id,
                CreditoId = c.CreditoId,
                CreditoNumero = c.CreditoNumero,
                NumeroCuota = c.NumeroCuota,
                ClienteNombre = c.ClienteNombre,
                ClienteId = c.ClienteId,
                FechaVencimiento = c.FechaVencimiento,
                Monto = c.Monto,
                DiasVencidos = (hoy - c.FechaVencimiento).Days,
                MontoPunitorio = c.MontoPunitorio
            }).ToList();
        }

        /// <summary>
        /// Obtiene las órdenes de compra pendientes (no recibidas ni canceladas)
        /// para el panel "Pagos Proveedores"
        /// </summary>
        private async Task<List<OrdenCompraPendienteDto>> GetOrdenesCompraPendientesAsync()
        {
            var estadosActivos = new[]
            {
                EstadoOrdenCompra.Enviada,
                EstadoOrdenCompra.Confirmada,
                EstadoOrdenCompra.EnTransito
            };

            var ordenes = await _context.OrdenesCompra
                .AsNoTracking()
                .Include(oc => oc.Proveedor)
                .Where(oc => !oc.IsDeleted &&
                             oc.Proveedor != null &&
                             !oc.Proveedor.IsDeleted &&
                             estadosActivos.Contains(oc.Estado))
                .OrderByDescending(oc => oc.FechaEmision)
                .Take(10)
                .Select(oc => new OrdenCompraPendienteDto
                {
                    OrdenCompraId = oc.Id,
                    Numero = oc.Numero,
                    ProveedorNombre = oc.Proveedor!.RazonSocial,
                    ProveedorId = oc.ProveedorId,
                    FechaEmision = oc.FechaEmision,
                    FechaEntregaEstimada = oc.FechaEntregaEstimada,
                    Total = oc.Total,
                    Estado = oc.Estado.ToString(),
                    EstadoColor = oc.Estado == EstadoOrdenCompra.EnTransito ? "info"
                                : oc.Estado == EstadoOrdenCompra.Confirmada ? "success"
                                : oc.Estado == EstadoOrdenCompra.Enviada ? "warning"
                                : "neutral"
                })
                .ToListAsync();

            return ordenes;
        }

        #endregion
    }
}
