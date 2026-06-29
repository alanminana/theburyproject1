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
            var alertasStock    = await GetAlertasStockRecientesAsync();
            var destacados      = await GetProductosDestacadosAsync();
            var actividad       = await GetActividadRecienteAsync();

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
                MontoOrdenesCompraPendientes    = ordenes.Sum(o => o.Total),

                AlertasStockRecientes  = alertasStock,
                ProductosDestacados    = destacados,
                ActividadReciente      = actividad
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
            var cuotas = cuotasDb.Select(c => new CuotaProximaVencerDto
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

            var productosPorCredito = await ObtenerProductosAsociadosPorCreditoAsync(cuotas.Select(c => c.CreditoId));
            foreach (var cuota in cuotas)
            {
                cuota.ProductosAsociados = productosPorCredito.TryGetValue(cuota.CreditoId, out var productos)
                    ? productos
                    : new List<CreditoProductoAsociadoViewModel>();
            }

            return cuotas;
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
            var cuotas = cuotasDb.Select(c => new CuotaVencidaDto
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

            var productosPorCredito = await ObtenerProductosAsociadosPorCreditoAsync(cuotas.Select(c => c.CreditoId));
            foreach (var cuota in cuotas)
            {
                cuota.ProductosAsociados = productosPorCredito.TryGetValue(cuota.CreditoId, out var productos)
                    ? productos
                    : new List<CreditoProductoAsociadoViewModel>();
            }

            return cuotas;
        }

        private async Task<Dictionary<int, List<CreditoProductoAsociadoViewModel>>> ObtenerProductosAsociadosPorCreditoAsync(
            IEnumerable<int> creditoIds)
        {
            var ids = creditoIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return new Dictionary<int, List<CreditoProductoAsociadoViewModel>>();

            var detalles = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted &&
                            v.CreditoId.HasValue &&
                            ids.Contains(v.CreditoId.Value))
                .SelectMany(
                    v => v.Detalles.Where(d => !d.IsDeleted),
                    (venta, detalle) => new
                    {
                        CreditoId = venta.CreditoId!.Value,
                        detalle.ProductoId,
                        ProductoNombre = detalle.Producto != null ? detalle.Producto.Nombre : null,
                        ProductoCodigo = detalle.Producto != null ? detalle.Producto.Codigo : null,
                        detalle.Cantidad,
                        Total = detalle.SubtotalFinal != 0 ? detalle.SubtotalFinal : detalle.Subtotal
                    })
                .ToListAsync();

            return detalles
                .GroupBy(d => d.CreditoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => grupo
                        .GroupBy(d => new
                        {
                            d.ProductoId,
                            d.ProductoNombre,
                            d.ProductoCodigo
                        })
                        .Select(producto => new CreditoProductoAsociadoViewModel
                        {
                            ProductoId = producto.Key.ProductoId,
                            ProductoNombre = string.IsNullOrWhiteSpace(producto.Key.ProductoNombre)
                                ? $"Producto #{producto.Key.ProductoId}"
                                : producto.Key.ProductoNombre,
                            ProductoCodigo = producto.Key.ProductoCodigo,
                            Cantidad = producto.Sum(x => x.Cantidad),
                            Total = producto.Sum(x => x.Total)
                        })
                        .OrderBy(p => p.ProductoNombre)
                        .ToList());
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

        /// <summary>Factor sobre StockMinimo para clasificar stock como crítico (alineado con AlertaStockService).</summary>
        private const decimal FactorStockCritico = 0.3m;

        /// <summary>
        /// Productos activos por debajo (o en) el mínimo, clasificados por severidad,
        /// para el panel "Alertas de stock" del dashboard.
        /// </summary>
        private async Task<List<StockAlertaDto>> GetAlertasStockRecientesAsync()
        {
            // Filtro traducible en SQL; la clasificación por factor se hace en memoria.
            var productosRaw = await _context.Productos
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Activo && p.StockActual <= p.StockMinimo)
                .Select(p => new { p.Id, p.Codigo, p.Nombre, p.StockActual, p.StockMinimo })
                .ToListAsync();

            return productosRaw
                .Select(p =>
                {
                    string severidad;
                    string severidadTexto;
                    if (p.StockActual <= 0)
                    {
                        severidad = "agotado";
                        severidadTexto = "Agotado";
                    }
                    else if (p.StockActual <= p.StockMinimo * FactorStockCritico)
                    {
                        severidad = "critico";
                        severidadTexto = "Stock crítico";
                    }
                    else
                    {
                        severidad = "bajo";
                        severidadTexto = "Reponer pronto";
                    }

                    var sugerida = (int)Math.Ceiling((p.StockMinimo * 3) - p.StockActual);
                    if (sugerida < 1) sugerida = 1;

                    return new StockAlertaDto
                    {
                        ProductoId = p.Id,
                        Codigo = p.Codigo,
                        Nombre = p.Nombre,
                        StockActual = p.StockActual,
                        StockMinimo = p.StockMinimo,
                        Severidad = severidad,
                        SeveridadTexto = severidadTexto,
                        CantidadSugerida = sugerida
                    };
                })
                .OrderBy(p => p.Severidad == "agotado" ? 0 : p.Severidad == "critico" ? 1 : 2)
                .ThenBy(p => p.StockActual)
                .Take(6)
                .ToList();
        }

        /// <summary>
        /// Productos activos marcados como destacados (EsDestacado) para el panel
        /// "Productos destacados" del dashboard.
        /// </summary>
        private async Task<List<ProductoDestacadoDto>> GetProductosDestacadosAsync()
        {
            return await _context.Productos
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Activo && p.EsDestacado)
                .OrderBy(p => p.Nombre)
                .Take(6)
                .Select(p => new ProductoDestacadoDto
                {
                    ProductoId = p.Id,
                    Codigo = p.Codigo,
                    Nombre = p.Nombre,
                    PrecioVenta = p.PrecioVenta,
                    StockActual = p.StockActual,
                    StockMinimo = p.StockMinimo
                })
                .ToListAsync();
        }

        /// <summary>
        /// Feed de actividad reciente combinando últimas ventas, altas de cliente y alertas de stock pendientes.
        /// </summary>
        private async Task<List<ActividadRecienteDto>> GetActividadRecienteAsync()
        {
            var cultura = CultureInfo.GetCultureInfo("es-AR");

            var ventas = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted)
                .OrderByDescending(v => v.FechaVenta)
                .Take(6)
                .Select(v => new { v.Numero, v.Total, v.FechaVenta })
                .ToListAsync();

            var clientes = await _context.Clientes
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.CreatedAt)
                .Take(6)
                .Select(c => new { c.Apellido, c.Nombre, c.CreatedAt })
                .ToListAsync();

            var alertas = await _context.AlertasStock
                .AsNoTracking()
                .Where(a => !a.IsDeleted && a.Estado == EstadoAlerta.Pendiente &&
                            a.Producto != null && !a.Producto.IsDeleted)
                .OrderByDescending(a => a.FechaAlerta)
                .Take(6)
                .Select(a => new { a.Producto.Nombre, a.Producto.Codigo, a.FechaAlerta })
                .ToListAsync();

            var eventos = new List<ActividadRecienteDto>();

            eventos.AddRange(ventas.Select(v => new ActividadRecienteDto
            {
                Tipo = "venta",
                Titulo = "Venta registrada",
                Detalle = $"#{v.Numero} por {v.Total.ToString("C", cultura)}",
                Fecha = v.FechaVenta,
                Color = "emerald"
            }));

            eventos.AddRange(clientes.Select(c => new ActividadRecienteDto
            {
                Tipo = "cliente",
                Titulo = "Cliente nuevo",
                Detalle = $"{c.Apellido}, {c.Nombre}".Trim(' ', ','),
                Fecha = c.CreatedAt,
                Color = "primary"
            }));

            eventos.AddRange(alertas.Select(a => new ActividadRecienteDto
            {
                Tipo = "stock",
                Titulo = "Stock bajo",
                Detalle = $"{a.Nombre} ({a.Codigo})",
                Fecha = a.FechaAlerta,
                Color = "orange"
            }));

            var ahora = DateTime.UtcNow;
            return eventos
                .OrderByDescending(e => e.Fecha)
                .Take(6)
                .Select(e =>
                {
                    e.TiempoRelativo = FormatHace(e.Fecha, ahora);
                    return e;
                })
                .ToList();
        }

        /// <summary>Texto relativo en español a partir de una fecha UTC.</summary>
        private static string FormatHace(DateTime fechaUtc, DateTime ahoraUtc)
        {
            var delta = ahoraUtc - fechaUtc;
            if (delta.TotalSeconds < 60) return "Recién";
            if (delta.TotalMinutes < 60) return $"Hace {(int)delta.TotalMinutes} min";
            if (delta.TotalHours < 24) return $"Hace {(int)delta.TotalHours} h";
            if (delta.TotalDays < 30) return $"Hace {(int)delta.TotalDays} d";
            return fechaUtc.ToLocalTime().ToString("dd MMM", CultureInfo.GetCultureInfo("es-AR"));
        }

        #endregion
    }
}
