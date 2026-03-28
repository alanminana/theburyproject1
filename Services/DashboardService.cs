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
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var inicioAnio = new DateTime(hoy.Year, 1, 1);

            // Lanzar todas las queries independientes en paralelo
            var totalClientesTask      = _context.Clientes.CountAsync(c => !c.IsDeleted);
            var clientesActivosTask    = _context.Clientes.CountAsync(c => !c.IsDeleted && c.Activo);
            var clientesNuevosMesTask  = _context.Clientes.CountAsync(c => !c.IsDeleted && c.CreatedAt >= inicioMes);

            var ventasHoyTask = _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta.Date == hoy)
                .SumAsync(v => (decimal?)v.Total);
            var ventasMesTask = _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioMes)
                .SumAsync(v => (decimal?)v.Total);
            var cantVentasMesTask = _context.Ventas
                .CountAsync(v => !v.IsDeleted && v.FechaVenta >= inicioMes);
            var ventasAnioTask = _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioAnio)
                .SumAsync(v => (decimal?)v.Total);
            var ticketPromedioTask = CalcularTicketPromedioAsync();

            var creditosActivosTask = _context.Creditos
                .CountAsync(c => !c.IsDeleted &&
                                 c.Cliente != null && !c.Cliente.IsDeleted &&
                                 c.Estado == EstadoCredito.Activo);
            var montoTotalCreditosTask = _context.Creditos
                .Where(c => !c.IsDeleted &&
                            c.Cliente != null && !c.Cliente.IsDeleted &&
                            c.Estado == EstadoCredito.Activo)
                .SumAsync(c => (decimal?)c.TotalAPagar);
            var saldoPendienteTask = _context.Creditos
                .Where(c => !c.IsDeleted &&
                            c.Cliente != null && !c.Cliente.IsDeleted &&
                            c.Estado == EstadoCredito.Activo)
                .SumAsync(c => (decimal?)c.SaldoPendiente);

            var cuotasVencidasCountTask = _context.Cuotas
                .CountAsync(c => !c.IsDeleted &&
                                 c.Credito != null && !c.Credito.IsDeleted &&
                                 c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                                 c.Estado == EstadoCuota.Pendiente &&
                                 c.FechaVencimiento < hoy);
            var montoVencidoTask = _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pendiente &&
                            c.FechaVencimiento < hoy)
                .SumAsync(c => (decimal?)c.MontoTotal);

            var cobranzaHoyTask = _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pagada &&
                            c.FechaPago.HasValue && c.FechaPago.Value.Date == hoy)
                .SumAsync(c => (decimal?)c.MontoPagado);
            var cobranzaMesTask = _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pagada &&
                            c.FechaPago.HasValue && c.FechaPago.Value >= inicioMes)
                .SumAsync(c => (decimal?)c.MontoPagado);
            var cobranzaAnioTask = _context.Cuotas
                .Where(c => !c.IsDeleted &&
                            c.Credito != null && !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null && !c.Credito.Cliente.IsDeleted &&
                            c.Estado == EstadoCuota.Pagada &&
                            c.FechaPago.HasValue && c.FechaPago.Value >= inicioAnio)
                .SumAsync(c => (decimal?)c.MontoPagado);

            var tasaMorosidadTask       = CalcularTasaMorosidadAsync();
            var efectividadCobranzaTask = CalcularEfectividadCobranzaAsync();

            var productosTotalesTask  = _context.Productos.CountAsync(p => !p.IsDeleted);
            var productosStockBajoTask = _context.Productos
                .CountAsync(p => !p.IsDeleted && p.StockActual < p.StockMinimo);
            var valorStockTask = _context.Productos
                .Where(p => !p.IsDeleted)
                .SumAsync(p => (decimal?)(p.StockActual * p.PrecioVenta));

            var ventasUlt7Task      = GetVentasUltimos7DiasAsync();
            var ventasUlt12Task     = GetVentasUltimos12MesesAsync();
            var prodMasVendidosTask = GetProductosMasVendidosAsync();
            var creditosEstadoTask  = GetCreditosPorEstadoAsync();
            var cobranzaUlt6Task    = GetCobranzaUltimos6MesesAsync();
            var cuotasProxTask      = GetCuotasProximasVencerAsync();
            var cuotasVencListaTask = GetCuotasVencidasListaAsync();
            var ordenesTask         = GetOrdenesCompraPendientesAsync();

            await Task.WhenAll(
                totalClientesTask, clientesActivosTask, clientesNuevosMesTask,
                ventasHoyTask, ventasMesTask, cantVentasMesTask, ventasAnioTask, ticketPromedioTask,
                creditosActivosTask, montoTotalCreditosTask, saldoPendienteTask,
                cuotasVencidasCountTask, montoVencidoTask,
                cobranzaHoyTask, cobranzaMesTask, cobranzaAnioTask,
                tasaMorosidadTask, efectividadCobranzaTask,
                productosTotalesTask, productosStockBajoTask, valorStockTask,
                ventasUlt7Task, ventasUlt12Task, prodMasVendidosTask,
                creditosEstadoTask, cobranzaUlt6Task,
                cuotasProxTask, cuotasVencListaTask, ordenesTask);

            var cuotasProximas = cuotasProxTask.Result;
            var ordenes        = ordenesTask.Result;

            var dashboard = new DashboardViewModel
            {
                TotalClientes        = totalClientesTask.Result,
                ClientesActivos      = clientesActivosTask.Result,
                ClientesNuevosMes    = clientesNuevosMesTask.Result,

                VentasTotalesHoy     = ventasHoyTask.Result ?? 0,
                VentasTotalesMes     = ventasMesTask.Result ?? 0,
                CantidadVentasMes    = cantVentasMesTask.Result,
                VentasTotalesAnio    = ventasAnioTask.Result ?? 0,
                TicketPromedio       = ticketPromedioTask.Result,

                CreditosActivos      = creditosActivosTask.Result,
                MontoTotalCreditos   = montoTotalCreditosTask.Result ?? 0,
                SaldoPendienteTotal  = saldoPendienteTask.Result ?? 0,

                CuotasVencidasTotal  = cuotasVencidasCountTask.Result,
                MontoVencidoTotal    = montoVencidoTask.Result ?? 0,

                CobranzaHoy          = cobranzaHoyTask.Result ?? 0,
                CobranzaMes          = cobranzaMesTask.Result ?? 0,
                CobranzaAnio         = cobranzaAnioTask.Result ?? 0,

                TasaMorosidad        = tasaMorosidadTask.Result,
                EfectividadCobranza  = efectividadCobranzaTask.Result,

                ProductosTotales     = productosTotalesTask.Result,
                ProductosStockBajo   = productosStockBajoTask.Result,
                ValorTotalStock      = valorStockTask.Result ?? 0,

                VentasUltimos7Dias      = ventasUlt7Task.Result,
                VentasUltimos12Meses    = ventasUlt12Task.Result,
                ProductosMasVendidos    = prodMasVendidosTask.Result,
                CreditosPorEstado       = creditosEstadoTask.Result,
                CobranzaUltimos6Meses   = cobranzaUlt6Task.Result,

                CuotasProximasVencer    = cuotasProximas,
                CuotasVencidasLista     = cuotasVencListaTask.Result,
                OrdenesCompraPendientes = ordenes,

                CuotasProximasVencerCount      = cuotasProximas.Count,
                MontoCuotasProximasVencer       = cuotasProximas.Sum(c => c.Monto),
                OrdenesCompraPendientesCount    = ordenes.Count,
                MontoOrdenesCompraPendientes    = ordenes.Sum(o => o.Total)
            };

            return dashboard;
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
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            // USANDO MontoTotal en lugar de Monto
            var montoEsperado = await _context.Cuotas
                  .Where(c => !c.IsDeleted &&
                      c.Credito != null &&
                      !c.Credito.IsDeleted &&
                      c.Credito.Cliente != null &&
                      !c.Credito.Cliente.IsDeleted &&
                       c.FechaVencimiento >= inicioMes &&
                       c.FechaVencimiento < DateTime.Today)
                .SumAsync(c => (decimal?)c.MontoTotal) ?? 0;

            if (montoEsperado == 0) return 0;

            var montoRecaudado = await _context.Cuotas
                  .Where(c => !c.IsDeleted &&
                      c.Credito != null &&
                      !c.Credito.IsDeleted &&
                      c.Credito.Cliente != null &&
                      !c.Credito.Cliente.IsDeleted &&
                       c.Estado == EstadoCuota.Pagada &&
                       c.FechaPago.HasValue &&
                       c.FechaPago.Value >= inicioMes)
                .SumAsync(c => (decimal?)c.MontoPagado) ?? 0;

            return (montoRecaudado / montoEsperado) * 100;
        }

        #endregion

        #region Datos para gráficos

        private async Task<List<VentasPorDiaDto>> GetVentasUltimos7DiasAsync()
        {
            var hace7Dias = DateTime.Today.AddDays(-7);

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted && v.FechaVenta >= hace7Dias)
                .GroupBy(v => v.FechaVenta.Date)
                .Select(g => new VentasPorDiaDto
                {
                    Fecha = g.Key,
                    Total = g.Sum(v => v.Total),
                    Cantidad = g.Count()
                })
                .OrderBy(v => v.Fecha)
                .ToListAsync();

            // Rellenar días faltantes
            for (var fecha = hace7Dias.Date; fecha <= DateTime.Today; fecha = fecha.AddDays(1))
            {
                if (!ventas.Any(v => v.Fecha == fecha))
                {
                    ventas.Add(new VentasPorDiaDto
                    {
                        Fecha = fecha,
                        Total = 0,
                        Cantidad = 0
                    });
                }
            }

            return ventas.OrderBy(v => v.Fecha).ToList();
        }

        private async Task<List<VentasPorMesDto>> GetVentasUltimos12MesesAsync()
        {
            var hace12Meses = DateTime.Today.AddMonths(-12);

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted && v.FechaVenta >= hace12Meses)
                .GroupBy(v => new { v.FechaVenta.Year, v.FechaVenta.Month })
                .Select(g => new VentasPorMesDto
                {
                    Anio = g.Key.Year,
                    Mes = g.Key.Month,
                    MesNombre = CultureInfo.GetCultureInfo("es-AR")
                        .DateTimeFormat.GetMonthName(g.Key.Month),
                    Total = g.Sum(v => v.Total),
                    Cantidad = g.Count()
                })
                .OrderBy(v => v.Anio).ThenBy(v => v.Mes)
                .ToListAsync();

            return ventas;
        }

        private async Task<List<ProductoMasVendidoDto>> GetProductosMasVendidosAsync()
        {
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

            var productos = await _context.VentaDetalles
                .AsNoTracking()
                .Where(vd => !vd.IsDeleted &&
                    vd.Producto != null &&
                    !vd.Producto.IsDeleted &&
                        vd.Venta != null &&
                        !vd.Venta.IsDeleted &&
                        vd.Venta.FechaVenta >= inicioMes)
                .GroupBy(vd => new { vd.ProductoId, vd.Producto!.Nombre })
                .Select(g => new ProductoMasVendidoDto
                {
                    ProductoId = g.Key.ProductoId,
                    ProductoNombre = g.Key.Nombre,
                    Cantidad = g.Sum(vd => vd.Cantidad),
                    TotalVendido = g.Sum(vd => vd.Subtotal)
                })
                .OrderByDescending(p => p.Cantidad)
                .Take(10)
                .ToListAsync();

            return productos;
        }

        private async Task<List<EstadoCreditoDto>> GetCreditosPorEstadoAsync()
        {
            // USANDO TotalAPagar en lugar de MontoTotal
            var creditos = await _context.Creditos
                .AsNoTracking()
                .Where(c => !c.IsDeleted &&
                            c.Cliente != null &&
                            !c.Cliente.IsDeleted)
                .GroupBy(c => c.Estado)
                .Select(g => new EstadoCreditoDto
                {
                    Estado = g.Key.ToString(),
                    Cantidad = g.Count(),
                    Monto = g.Sum(c => c.TotalAPagar)
                })
                .ToListAsync();

            return creditos;
        }

        private async Task<List<CobranzaPorMesDto>> GetCobranzaUltimos6MesesAsync()
        {
            var hace6Meses = DateTime.Today.AddMonths(-6);
            var inicioMes = new DateTime(hace6Meses.Year, hace6Meses.Month, 1);

            // USANDO MontoTotal en lugar de Monto
            var cobranza = await _context.Cuotas
                .AsNoTracking()
                .Where(c => !c.IsDeleted &&
                            c.Credito != null &&
                            !c.Credito.IsDeleted &&
                            c.Credito.Cliente != null &&
                            !c.Credito.Cliente.IsDeleted &&
                            c.FechaVencimiento >= inicioMes)
                .GroupBy(c => new { c.FechaVencimiento.Year, c.FechaVencimiento.Month })
                .Select(g => new CobranzaPorMesDto
                {
                    Anio = g.Key.Year,
                    Mes = g.Key.Month,
                    MesNombre = CultureInfo.GetCultureInfo("es-AR")
                        .DateTimeFormat.GetMonthName(g.Key.Month),
                    MontoEsperado = g.Sum(c => c.MontoTotal),
                    MontoRecaudado = g.Where(c => c.Estado == EstadoCuota.Pagada)
                        .Sum(c => c.MontoPagado),
                    PorcentajeEfectividad = g.Sum(c => c.MontoTotal) > 0
                        ? (g.Where(c => c.Estado == EstadoCuota.Pagada)
                              .Sum(c => c.MontoPagado) / g.Sum(c => c.MontoTotal)) * 100
                        : 0
                })
                .OrderBy(c => c.Anio).ThenBy(c => c.Mes)
                .ToListAsync();

            return cobranza;
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