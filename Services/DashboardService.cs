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
        private readonly AppDbContext _context;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(AppDbContext context, ILogger<DashboardService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<DashboardViewModel> GetDashboardDataAsync()
        {
            var hoy = DateTime.Today;
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var inicioAnio = new DateTime(hoy.Year, 1, 1);

            var dashboard = new DashboardViewModel
            {
                // KPIs de Clientes
                TotalClientes = await _context.Clientes.CountAsync(c => !c.IsDeleted),
                ClientesActivos = await _context.Clientes.CountAsync(c => !c.IsDeleted && c.Activo),
                ClientesNuevosMes = await _context.Clientes.CountAsync(c => !c.IsDeleted && c.CreatedAt >= inicioMes),

                // KPIs de Ventas
                VentasTotalesHoy = await _context.Ventas
                    .Where(v => !v.IsDeleted && v.FechaVenta.Date == hoy)
                    .SumAsync(v => (decimal?)v.Total) ?? 0,

                VentasTotalesMes = await _context.Ventas
                    .Where(v => !v.IsDeleted && v.FechaVenta >= inicioMes)
                    .SumAsync(v => (decimal?)v.Total) ?? 0,

                CantidadVentasMes = await _context.Ventas
                    .CountAsync(v => !v.IsDeleted && v.FechaVenta >= inicioMes),

                VentasTotalesAnio = await _context.Ventas
                    .Where(v => !v.IsDeleted && v.FechaVenta >= inicioAnio)
                    .SumAsync(v => (decimal?)v.Total) ?? 0,

                TicketPromedio = await CalcularTicketPromedioAsync(),

                // KPIs de Créditos - USANDO EstadoCredito.Activo
                CreditosActivos = await _context.Creditos
                    .CountAsync(c => !c.IsDeleted &&
                                   c.Cliente != null &&
                                   !c.Cliente.IsDeleted &&
                                   c.Estado == EstadoCredito.Activo),

                // USANDO TotalAPagar en lugar de MontoTotal
                MontoTotalCreditos = await _context.Creditos
                    .Where(c => !c.IsDeleted &&
                                c.Cliente != null &&
                                !c.Cliente.IsDeleted &&
                                c.Estado == EstadoCredito.Activo)
                    .SumAsync(c => (decimal?)c.TotalAPagar) ?? 0,

                SaldoPendienteTotal = await _context.Creditos
                    .Where(c => !c.IsDeleted &&
                                c.Cliente != null &&
                                !c.Cliente.IsDeleted &&
                                c.Estado == EstadoCredito.Activo)
                    .SumAsync(c => (decimal?)c.SaldoPendiente) ?? 0,

                CuotasVencidasTotal = await _context.Cuotas
                    .CountAsync(c => !c.IsDeleted &&
                                c.Credito != null &&
                                !c.Credito.IsDeleted &&
                                c.Credito.Cliente != null &&
                                !c.Credito.Cliente.IsDeleted &&
                                c.Estado == EstadoCuota.Pendiente &&
                                c.FechaVencimiento < hoy),

                // USANDO MontoTotal en lugar de Monto
                MontoVencidoTotal = await _context.Cuotas
                      .Where(c => !c.IsDeleted &&
                          c.Credito != null &&
                          !c.Credito.IsDeleted &&
                          c.Credito.Cliente != null &&
                          !c.Credito.Cliente.IsDeleted &&
                           c.Estado == EstadoCuota.Pendiente &&
                           c.FechaVencimiento < hoy)
                    .SumAsync(c => (decimal?)c.MontoTotal) ?? 0,

                // KPIs de Cobranza
                CobranzaHoy = await _context.Cuotas
                      .Where(c => !c.IsDeleted &&
                          c.Credito != null &&
                          !c.Credito.IsDeleted &&
                          c.Credito.Cliente != null &&
                          !c.Credito.Cliente.IsDeleted &&
                           c.Estado == EstadoCuota.Pagada &&
                           c.FechaPago.HasValue && c.FechaPago.Value.Date == hoy)
                    .SumAsync(c => (decimal?)c.MontoPagado) ?? 0,

                CobranzaMes = await _context.Cuotas
                      .Where(c => !c.IsDeleted &&
                          c.Credito != null &&
                          !c.Credito.IsDeleted &&
                          c.Credito.Cliente != null &&
                          !c.Credito.Cliente.IsDeleted &&
                           c.Estado == EstadoCuota.Pagada &&
                           c.FechaPago.HasValue && c.FechaPago.Value >= inicioMes)
                    .SumAsync(c => (decimal?)c.MontoPagado) ?? 0,

                CobranzaAnio = await _context.Cuotas
                      .Where(c => !c.IsDeleted &&
                          c.Credito != null &&
                          !c.Credito.IsDeleted &&
                          c.Credito.Cliente != null &&
                          !c.Credito.Cliente.IsDeleted &&
                           c.Estado == EstadoCuota.Pagada &&
                           c.FechaPago.HasValue && c.FechaPago.Value >= inicioAnio)
                    .SumAsync(c => (decimal?)c.MontoPagado) ?? 0,

                TasaMorosidad = await CalcularTasaMorosidadAsync(),
                EfectividadCobranza = await CalcularEfectividadCobranzaAsync(),

                // KPIs de Stock
                ProductosTotales = await _context.Productos.CountAsync(p => !p.IsDeleted),
                ProductosStockBajo = await _context.Productos
                    .CountAsync(p => !p.IsDeleted && p.StockActual < p.StockMinimo),

                ValorTotalStock = await _context.Productos
                    .Where(p => !p.IsDeleted)
                    .SumAsync(p => (decimal?)(p.StockActual * p.PrecioVenta)) ?? 0,

                // Datos para gráficos
                VentasUltimos7Dias = await GetVentasUltimos7DiasAsync(),
                VentasUltimos12Meses = await GetVentasUltimos12MesesAsync(),
                ProductosMasVendidos = await GetProductosMasVendidosAsync(),
                CreditosPorEstado = await GetCreditosPorEstadoAsync(),
                CobranzaUltimos6Meses = await GetCobranzaUltimos6MesesAsync(),

                // ✅ Alertas de cuotas
                CuotasProximasVencer = await GetCuotasProximasVencerAsync(),
                CuotasVencidasLista = await GetCuotasVencidasListaAsync(),

                // ✅ Órdenes de compra pendientes (Pagos Proveedores)
                OrdenesCompraPendientes = await GetOrdenesCompraPendientesAsync()
            };

            // Calcular contadores adicionales
            dashboard.CuotasProximasVencerCount = dashboard.CuotasProximasVencer.Count;
            dashboard.MontoCuotasProximasVencer = dashboard.CuotasProximasVencer.Sum(c => c.Monto);
            dashboard.OrdenesCompraPendientesCount = dashboard.OrdenesCompraPendientes.Count;
            dashboard.MontoOrdenesCompraPendientes = dashboard.OrdenesCompraPendientes.Sum(o => o.Total);

            return dashboard;
        }

        private async Task<decimal> CalcularTicketPromedioAsync()
        {
            var inicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var totalVentas = await _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioMes)
                .CountAsync();

            if (totalVentas == 0) return 0;

            var montoTotal = await _context.Ventas
                .Where(v => !v.IsDeleted && v.FechaVenta >= inicioMes)
                .SumAsync(v => (decimal?)v.Total) ?? 0;

            return montoTotal / totalVentas;
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

        private async Task<List<VentasPorDiaDto>> GetVentasUltimos7DiasAsync()
        {
            var hace7Dias = DateTime.Today.AddDays(-7);

            var ventas = await _context.Ventas
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

        /// <summary>
        /// Obtiene las cuotas que vencen en los próximos 7 días
        /// </summary>
        private async Task<List<CuotaProximaVencerDto>> GetCuotasProximasVencerAsync()
        {
            var hoy = DateTime.Today;
            var en7Dias = hoy.AddDays(7);

            // Consulta a la base de datos sin cálculos de fechas complejos
            var cuotasDb = await _context.Cuotas
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
                    ClienteNombre = c.Credito.Cliente.Nombre + " " + c.Credito.Cliente.Apellido,
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
                    ClienteNombre = c.Credito.Cliente.Nombre + " " + c.Credito.Cliente.Apellido,
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
    }
}